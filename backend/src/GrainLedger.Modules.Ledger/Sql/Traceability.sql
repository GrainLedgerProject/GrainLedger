-- Read-only mass graph plus retained query audit. Does not take the stock-write lock.
CREATE FUNCTION ledger.trace_lot(a uuid, target uuid, direction text, view_mode text) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE
    actor_role text;
    owner uuid;
    result jsonb;
    trace_id uuid:=gen_random_uuid();
BEGIN
    actor_role:=ledger.role(a);
    IF actor_role IS NULL OR actor_role NOT IN ('CustomerManager','ExportStaff','Administrator') THEN
        RAISE EXCEPTION 'Trace permission required' USING ERRCODE='42501';
    END IF;
    IF direction NOT IN ('forward','backward') OR view_mode NOT IN ('current','history')
       OR direction IS NULL OR view_mode IS NULL THEN
        RAISE EXCEPTION 'Unsupported trace direction or view' USING ERRCODE='22023';
    END IF;
    IF direction='backward' AND actor_role='ExportStaff' THEN
        RAISE EXCEPTION 'Source-contribution trace requires Customer Manager authority' USING ERRCODE='42501';
    END IF;
    SELECT owner_id INTO owner FROM ledger.lots WHERE id=target;
    -- Same error for an absent or inaccessible identity. Pending sources are not Ledger lots.
    IF owner IS NULL OR (actor_role<>'Administrator' AND NOT ledger.own(a,owner)) THEN
        RAISE EXCEPTION 'Trace lot unavailable in current scope; use an accepted Ledger lot' USING ERRCODE='42501';
    END IF;

    -- One statement captures graph, stock, restrictions and retained source weights together.
    WITH RECURSIVE
    actor AS MATERIALIZED (
        SELECT u."Role" role FROM identity.users u
        WHERE u."Id"=a AND u."IsActive" AND u."Role"=actor_role
          AND (u."Role"='Administrator' OR u."OrganizationId"=owner)
    ),
    operations AS MATERIALIZED (
        SELECT op.* FROM ledger.operations op
        WHERE op.owner_id=owner AND op.status IN ('COMMITTED','CORRECTED')
          AND (view_mode='history' OR op.status='COMMITTED')
          AND EXISTS(SELECT 1 FROM actor)
    ),
    edges AS MATERIALIZED (
        -- Aggregate multiple locations of the same input lot, retaining every relationship,
        -- including those whose source-attributed micro-kg later rounded to zero.
        SELECT op.id operation_id,(i.value->>'lotId')::uuid input_lot_id,
               (o.value->>'id')::uuid output_lot_id,
               sum((i.value->>'quantityKg')::numeric) input_kg,
               (o.value->>'quantityKg')::numeric output_kg
        FROM operations op
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'inputs') i
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') o
        JOIN ledger.lots il ON il.id=(i.value->>'lotId')::uuid AND il.owner_id=owner
        JOIN ledger.lots ol ON ol.id=(o.value->>'id')::uuid AND ol.owner_id=owner AND ol.operation_id=op.id
        GROUP BY op.id,(i.value->>'lotId')::uuid,(o.value->>'id')::uuid,(o.value->>'quantityKg')::numeric
    ),
    reached(id) AS (
        SELECT target WHERE EXISTS(SELECT 1 FROM actor)
        UNION
        SELECT CASE WHEN direction='forward' THEN e.output_lot_id ELSE e.input_lot_id END
        FROM reached r JOIN edges e ON
            (direction='forward' AND e.input_lot_id=r.id)
            OR (direction='backward' AND e.output_lot_id=r.id)
    ),
    trace_edges AS MATERIALIZED (
        SELECT e.* FROM edges e JOIN reached i ON i.id=e.input_lot_id JOIN reached o ON o.id=e.output_lot_id
    ),
    source_weights AS MATERIALIZED (
        SELECT c.* FROM ledger.contributions c JOIN reached r ON r.id=c.lot_id
        WHERE actor_role<>'ExportStaff'
    )
    SELECT jsonb_build_object(
        'traceId',trace_id,'targetLotId',target,'ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',statement_timestamp(),'snapshot',pg_current_snapshot()::text,
        'sourceContributionsVisible',actor_role<>'ExportStaff',
        'lotCount',(SELECT count(*) FROM reached),'edgeCount',(SELECT count(*) FROM trace_edges),
        'lots',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',l.id,'code',l.code,'stage',l.stage,'operationId',l.operation_id,
            'producedKg',(SELECT coalesce(sum(c.quantity_kg),0)::text FROM ledger.contributions c WHERE c.lot_id=l.id),
            'positions',coalesce((SELECT jsonb_agg(jsonb_build_object(
                'locationId',p.location_id,'facilityId',loc.facility_id,
                'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN ledger.blocked(l.id) THEN '0.000000' ELSE p.quantity_kg::text END
            ) ORDER BY p.location_id) FROM ledger.positions p JOIN ledger.locations loc ON loc.id=p.location_id
                WHERE p.lot_id=l.id),'[]'::jsonb),
            'restrictions',to_jsonb(array_remove(ARRAY[
                CASE WHEN EXISTS(SELECT 1 FROM ledger.holds WHERE lot_id=l.id) THEN 'HOLD' END,
                CASE WHEN EXISTS(SELECT 1 FROM ledger.corrections WHERE operation_id=l.operation_id AND status IN ('REQUESTED','RECONCILED'))
                    OR EXISTS(SELECT 1 FROM ledger.receipt_correction_blocks WHERE receipt_id=l.receipt_id) THEN 'CORRECTION' END,
                CASE WHEN ledger.receiving_restricted(l.id) THEN 'RECEIVING_DISCREPANCY' END,
                CASE WHEN EXISTS(SELECT 1 FROM ledger.operations WHERE id=l.operation_id AND status='CORRECTED')
                    OR EXISTS(SELECT 1 FROM ledger.receipt_reversals WHERE receipt_id=l.receipt_id) THEN 'SUPERSEDED' END
            ],NULL)),
            'contributions',CASE WHEN actor_role='ExportStaff' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,'quantityKg',s.quantity_kg::text,
                    'denominatorKg',(SELECT sum(c.quantity_kg)::text FROM ledger.contributions c WHERE c.lot_id=l.id),
                    'percentage',round(100*s.quantity_kg/NULLIF((SELECT sum(c.quantity_kg) FROM ledger.contributions c WHERE c.lot_id=l.id),0),6)::text
                ) ORDER BY s.source_lot_id) FROM source_weights s WHERE s.lot_id=l.id
            ),'[]'::jsonb) END
        ) ORDER BY l.id) FROM reached r JOIN ledger.lots l ON l.id=r.id
            WHERE (SELECT count(*) FROM reached)<=5000),'[]'::jsonb),
        'edges',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'operationId',operation_id,'inputLotId',input_lot_id,'outputLotId',output_lot_id,
            'inputConsumedKg',input_kg::text,'outputProducedKg',output_kg::text
        ) ORDER BY operation_id,input_lot_id,output_lot_id) FROM trace_edges
            WHERE (SELECT count(*) FROM trace_edges)<=20000),'[]'::jsonb),
        'operations',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',op.id,'kind',op.kind,'status',op.status,'occurredAt',op.occurred_at,
            'declaredLossKg',op.content->>'declaredLossKg','varianceKg',op.variance_kg::text
        ) ORDER BY op.id) FROM operations op WHERE EXISTS(SELECT 1 FROM trace_edges e WHERE e.operation_id=op.id)),'[]'::jsonb),
        'corrections',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',c.id,'originalOperationId',c.operation_id,'replacementOperationId',c.replacement_operation_id,'status',c.status
        ) ORDER BY c.id) FROM ledger.corrections c JOIN ledger.operations op ON op.id=c.operation_id
            WHERE op.owner_id=owner AND EXISTS(SELECT 1 FROM reached r JOIN ledger.lots l ON l.id=r.id
                WHERE l.operation_id=c.operation_id OR l.operation_id=c.replacement_operation_id)),'[]'::jsonb)
    ) INTO result WHERE EXISTS(SELECT 1 FROM actor);

    IF result IS NULL THEN RAISE EXCEPTION 'Trace permission changed' USING ERRCODE='42501'; END IF;
    IF (result->>'lotCount')::integer>5000 OR (result->>'edgeCount')::integer>20000 THEN
        RAISE EXCEPTION 'Trace exceeds interactive limit (5000 lots / 20000 edges); no complete result returned' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.events(id,kind,actor_id,lot_id,details)
    VALUES(trace_id,'TRACEBACK',a,target,jsonb_build_object(
        'schemaVersion',1,'ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',result->'asOf','snapshot',result->'snapshot','lotCount',result->'lotCount',
        'lotIds',(SELECT jsonb_agg(x->'id') FROM jsonb_array_elements(result->'lots') x)
    ));
    RETURN result||jsonb_build_object('complete',true);
END $$;
REVOKE ALL ON FUNCTION ledger.trace_lot(uuid,uuid,text,text) FROM PUBLIC;
