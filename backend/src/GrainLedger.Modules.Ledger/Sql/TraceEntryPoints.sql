-- Private bulk trace projection. Names are current labels; identities/history stay immutable.
CREATE OR REPLACE FUNCTION ledger.trace_lot_metadata(a uuid, ids uuid[])
RETURNS TABLE(lot_id uuid,code text,owner_id uuid,stage text,lot_kind text,entered_pool boolean,
    is_current_pool boolean,pool_revision bigint,positions jsonb,quantity_kg numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
    WITH actor AS MATERIALIZED (
        SELECT u."Role" role,u."OrganizationId" owner_id FROM identity.users u
        WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('Administrator','CustomerManager','ExportStaff')
    ), scoped AS MATERIALIZED (
        SELECT l.* FROM ledger.lots l CROSS JOIN actor
        WHERE (actor.role='Administrator' OR l.owner_id=actor.owner_id) AND (ids IS NULL OR l.id=ANY(ids))
    ), origins AS MATERIALIZED (
        SELECT edge.output_lot_id, max(ce.revision) revision,
            max(CASE WHEN ce.action='INGRESS' THEN 'POOL_GENERATION'
                     WHEN edge.output_lot_id=ce.output_lot_id THEN 'ALIQUOT' ELSE 'REMAINDER' END) kind
        FROM ledger.custody_edges edge JOIN ledger.custody_events ce ON ce.event_id=edge.event_id
        JOIN scoped s ON s.id=edge.output_lot_id GROUP BY edge.output_lot_id
    ), balances AS MATERIALIZED (
        SELECT p.lot_id,sum(p.quantity_kg) quantity,
            jsonb_agg(jsonb_build_object('locationId',loc.id,'locationName',loc.name,'locationKind',loc.kind,
                'facilityId',facility.id,'facilityName',facility.name,'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN ledger.blocked(p.lot_id) THEN '0.000000' ELSE p.quantity_kg::text END
            ) ORDER BY loc.id) positions
        FROM ledger.positions p JOIN scoped s ON s.id=p.lot_id
        JOIN ledger.locations loc ON loc.id=p.location_id JOIN ledger.facilities facility ON facility.id=loc.facility_id
        GROUP BY p.lot_id
    )
    SELECT s.id,s.code,s.owner_id,s.stage,
        CASE WHEN pool.lot_id IS NOT NULL THEN 'CURRENT_POOL' WHEN s.receipt_id IS NOT NULL THEN 'SOURCE'
             WHEN origins.kind IS NOT NULL THEN origins.kind WHEN s.operation_id IS NOT NULL THEN 'PROCESSING_OUTPUT' ELSE 'LOT' END,
        EXISTS(SELECT 1 FROM ledger.custody_edges e JOIN ledger.custody_events ce ON ce.event_id=e.event_id
               WHERE e.input_lot_id=s.id AND ce.action='INGRESS'),
        pool.lot_id IS NOT NULL,COALESCE(pool.revision,origins.revision),COALESCE(balances.positions,'[]'::jsonb),COALESCE(balances.quantity,0)
    FROM scoped s LEFT JOIN ledger.mixed_bulk_pools pool ON pool.lot_id=s.id
    LEFT JOIN origins ON origins.output_lot_id=s.id LEFT JOIN balances ON balances.lot_id=s.id
$$;
REVOKE ALL ON FUNCTION ledger.trace_lot_metadata(uuid,uuid[]) FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.trace_starts(a uuid,mode text,search text,facility uuid,location uuid,page integer,size integer)
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE result jsonb;
BEGIN
    IF ledger.role(a) IS NULL OR ledger.role(a) NOT IN ('Administrator','CustomerManager','ExportStaff') THEN
        RAISE EXCEPTION 'Trace permission required' USING ERRCODE='42501';
    END IF;
    IF mode IS NULL OR mode NOT IN ('pools','sources','all') OR page IS NULL OR page<1 OR page>1000000
       OR size IS NULL OR size<1 OR size>100 OR length(COALESCE(search,''))>200 THEN
        RAISE EXCEPTION 'Invalid trace starting-lot filters' USING ERRCODE='22023';
    END IF;
    WITH base AS MATERIALIZED (SELECT * FROM ledger.trace_lot_metadata(a,NULL)),
    matches AS MATERIALIZED (
        SELECT b.* FROM base b
        WHERE (mode='all' OR mode='pools' AND b.is_current_pool AND b.quantity_kg>0 OR mode='sources' AND b.lot_kind='SOURCE')
          AND (COALESCE(search,'')='' OR position(lower(search) in lower(b.code))>0
               OR EXISTS(SELECT 1 FROM jsonb_array_elements(b.positions) p WHERE position(lower(search) in lower(p->>'locationName'))>0))
          AND ((facility IS NULL AND location IS NULL) OR EXISTS(
              SELECT 1 FROM jsonb_array_elements(b.positions) p
              WHERE (facility IS NULL OR (p->>'facilityId')::uuid=facility)
                AND (location IS NULL OR (p->>'locationId')::uuid=location)))
    ), selected AS (SELECT * FROM matches ORDER BY code,lot_id OFFSET (page-1)*size LIMIT size),
    places AS (SELECT DISTINCT p->>'locationId' id,p->>'locationName' name,p->>'facilityId' facility_id,p->>'facilityName' facility_name
               FROM base CROSS JOIN LATERAL jsonb_array_elements(positions) p)
    SELECT jsonb_build_object('items',COALESCE((SELECT jsonb_agg(jsonb_build_object(
        'id',lot_id,'code',code,'stage',stage,'kind',lot_kind,'enteredPool',entered_pool,
        'ownerId',owner_id,'isCurrentPool',is_current_pool,'poolRevision',pool_revision,'positions',positions,
        'quantityKg',quantity_kg::text) ORDER BY code,lot_id) FROM selected),'[]'::jsonb),
        'page',page,'pageSize',size,'totalCount',(SELECT count(*) FROM matches),'hasMore',(SELECT count(*)>page*size FROM matches),
        'locations',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',id,'name',name,'facilityId',facility_id) ORDER BY name,id) FROM places),'[]'::jsonb),
        'facilities',COALESCE((SELECT jsonb_agg(jsonb_build_object('id',facility_id,'name',facility_name) ORDER BY facility_name,facility_id)
            FROM (SELECT DISTINCT facility_id,facility_name FROM places) f),'[]'::jsonb)) INTO result;
    RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.trace_starts(uuid,text,text,uuid,uuid,integer,integer) FROM PUBLIC;

-- Read-only mass graph plus retained query audit. Does not take the stock-write lock.
CREATE OR REPLACE FUNCTION ledger.trace_lot(a uuid, target uuid, direction text, view_mode text) RETURNS jsonb
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
    processing_inputs AS MATERIALIZED (
        -- Commit evidence resolves pool drafts to actual aliquots. Retain structural
        -- input/output pairs even when their allocated source quantity rounded to zero.
        SELECT op.id operation_id, ev.id event_id,
            CASE WHEN ev.details ? 'resolvedInputs' THEN 'RESOLVED_INPUTS' ELSE 'RECORDED_CONTENT' END basis,
            i.value
        FROM operations op
        LEFT JOIN LATERAL (
            SELECT e.id,e.details FROM ledger.events e
            WHERE e.operation_id=op.id AND e.kind=op.kind AND e.details ? 'resolvedInputs'
            ORDER BY e.occurred_at,e.id LIMIT 1
        ) ev ON true
        CROSS JOIN LATERAL jsonb_array_elements(COALESCE(ev.details->'resolvedInputs',op.content->'inputs')) i
    ),
    edges AS MATERIALIZED (
        SELECT 'PROCESSING'::text relation_type,op.id relation_id,op.id operation_id,pi.event_id,
               pi.basis,(pi.value->>'lotId')::uuid input_lot_id,
               (o.value->>'id')::uuid output_lot_id,
               sum((pi.value->>'quantityKg')::numeric) input_kg,
               (o.value->>'quantityKg')::numeric output_kg
        FROM operations op JOIN processing_inputs pi ON pi.operation_id=op.id
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') o
        JOIN ledger.lots il ON il.id=(pi.value->>'lotId')::uuid AND il.owner_id=owner
        JOIN ledger.lots ol ON ol.id=(o.value->>'id')::uuid AND ol.owner_id=owner AND ol.operation_id=op.id
        GROUP BY op.id,pi.event_id,pi.basis,(pi.value->>'lotId')::uuid,(o.value->>'id')::uuid,(o.value->>'quantityKg')::numeric
        UNION ALL
        SELECT 'CUSTODY',ce.event_id,NULL::uuid,ce.event_id,'CUSTODY_ALLOCATION',ce.input_lot_id,ce.output_lot_id,
               ce.quantity_kg,(SELECT sum(c.quantity_kg) FROM ledger.contributions c WHERE c.lot_id=ce.output_lot_id)
        FROM ledger.custody_edges ce
        JOIN ledger.lots il ON il.id=ce.input_lot_id AND il.owner_id=owner
        JOIN ledger.lots ol ON ol.id=ce.output_lot_id AND ol.owner_id=owner
        WHERE EXISTS(SELECT 1 FROM actor)
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
    allocations AS MATERIALIZED (
        SELECT e.relation_type,e.relation_id,e.input_lot_id,e.output_lot_id,ca.source_lot_id,ca.quantity_kg
        FROM trace_edges e JOIN ledger.custody_allocations ca
          ON e.relation_type='CUSTODY' AND ca.event_id=e.event_id
          AND ca.input_lot_id=e.input_lot_id AND ca.output_lot_id=e.output_lot_id
        JOIN ledger.lots source ON source.id=ca.source_lot_id AND source.owner_id=owner
        WHERE actor_role<>'ExportStaff'
        UNION ALL
        SELECT e.relation_type,e.relation_id,e.input_lot_id,e.output_lot_id,pa.source_lot_id,pa.quantity_kg
        FROM trace_edges e JOIN ledger.custody_processing_allocations pa
          ON e.relation_type='PROCESSING' AND pa.event_id=e.event_id AND pa.operation_id=e.operation_id
          AND pa.input_lot_id=e.input_lot_id AND pa.output_lot_id=e.output_lot_id
        JOIN ledger.lots source ON source.id=pa.source_lot_id AND source.owner_id=owner
        WHERE actor_role<>'ExportStaff'
    ),
    positive_paths(lot_id,source_lot_id) AS (
        SELECT target,c.source_lot_id FROM ledger.contributions c
        JOIN ledger.lots source ON source.id=c.source_lot_id AND source.owner_id=owner
        WHERE c.lot_id=target AND c.quantity_kg>0 AND actor_role<>'ExportStaff'
        UNION
        SELECT CASE WHEN direction='forward' THEN allocation.output_lot_id ELSE allocation.input_lot_id END,path.source_lot_id
        FROM positive_paths path JOIN allocations allocation
          ON allocation.source_lot_id=path.source_lot_id AND allocation.quantity_kg>0
          AND ((direction='forward' AND allocation.input_lot_id=path.lot_id)
            OR (direction='backward' AND allocation.output_lot_id=path.lot_id))
    ),
    lot_metadata AS MATERIALIZED (SELECT * FROM ledger.trace_lot_metadata(a,(SELECT array_agg(id) FROM reached))),
    source_weights AS MATERIALIZED (
        SELECT c.* FROM ledger.contributions c JOIN reached r ON r.id=c.lot_id
        WHERE actor_role<>'ExportStaff'
    )
    SELECT jsonb_build_object(
        'schemaVersion',2,'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS',
        'positivePathCoverage',CASE WHEN EXISTS(SELECT 1 FROM trace_edges WHERE basis='RECORDED_CONTENT') THEN 'PARTIAL_LEGACY' ELSE 'PERSISTED_ALLOCATIONS' END,
        'traceId',trace_id,'targetLotId',target,'ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',statement_timestamp(),'snapshot',pg_current_snapshot()::text,
        'sourceContributionsVisible',actor_role<>'ExportStaff',
        'lotCount',(SELECT count(*) FROM reached),'edgeCount',(SELECT count(*) FROM trace_edges),
        'lots',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',l.id,'code',l.code,'stage',l.stage,'operationId',l.operation_id,
            'kind',meta.lot_kind,'enteredPool',meta.entered_pool,
            'directHold',EXISTS(SELECT 1 FROM ledger.holds h WHERE h.lot_id=l.id),
            'effectiveHold',EXISTS(SELECT 1 FROM ledger.effective_hold_ids(l.id)),
            'positivePathSourceIds',CASE WHEN actor_role='ExportStaff' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(p.source_lot_id ORDER BY p.source_lot_id) FROM positive_paths p WHERE p.lot_id=l.id),'[]'::jsonb) END,
            'isCurrentPool',meta.is_current_pool,'poolRevision',meta.pool_revision,
            'producedKg',(SELECT coalesce(sum(c.quantity_kg),0)::text FROM ledger.contributions c WHERE c.lot_id=l.id),
            'positions',meta.positions,
            'restrictions',to_jsonb(array_remove(ARRAY[
                CASE WHEN EXISTS(SELECT 1 FROM ledger.effective_hold_ids(l.id)) THEN 'HOLD' END,
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
        ) ORDER BY l.id) FROM reached r JOIN ledger.lots l ON l.id=r.id JOIN lot_metadata meta ON meta.lot_id=l.id
            WHERE (SELECT count(*) FROM reached)<=5000),'[]'::jsonb),
        'edges',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'relationType',relation_type,'relationId',relation_id,'basis',basis,'eventId',event_id,
            'operationId',operation_id,'inputLotId',input_lot_id,'outputLotId',output_lot_id,
            'inputConsumedKg',input_kg::text,'outputProducedKg',output_kg::text,
            'inputEventKg',CASE WHEN relation_type='CUSTODY' THEN (SELECT sum(ce.quantity_kg)::text FROM ledger.custody_edges ce
                WHERE ce.event_id=e.event_id AND ce.input_lot_id=e.input_lot_id) ELSE input_kg::text END,
            'sourceAllocations',CASE WHEN actor_role='ExportStaff' OR basis='RECORDED_CONTENT' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',x.source_lot_id,'quantityKg',x.quantity_kg::text) ORDER BY x.source_lot_id)
                FROM allocations x WHERE x.relation_type=e.relation_type AND x.relation_id=e.relation_id
                  AND x.input_lot_id=e.input_lot_id AND x.output_lot_id=e.output_lot_id),'[]'::jsonb) END
        ) ORDER BY relation_type,relation_id,input_lot_id,output_lot_id) FROM trace_edges e
            WHERE (SELECT count(*) FROM trace_edges)<=20000),'[]'::jsonb),
        'custodyEvents',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',ce.event_id,'action',ce.action,'locationId',ce.location_id,'revision',ce.revision,
            'occurredAt',ce.created_at,'primaryOutputLotId',ce.output_lot_id
        ) ORDER BY ce.event_id) FROM ledger.custody_events ce WHERE EXISTS(
            SELECT 1 FROM trace_edges e WHERE e.relation_type='CUSTODY' AND e.event_id=ce.event_id)),'[]'::jsonb),
        'operations',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',op.id,'kind',op.kind,'status',op.status,'occurredAt',op.occurred_at,
            'declaredLossKg',op.content->>'declaredLossKg','varianceKg',op.variance_kg::text
        ) ORDER BY op.id) FROM operations op WHERE EXISTS(SELECT 1 FROM trace_edges e WHERE e.operation_id=op.id)),'[]'::jsonb),
        'corrections',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',c.id,'originalOperationId',c.operation_id,'replacementOperationId',c.replacement_operation_id,'status',c.status
        ) ORDER BY c.id) FROM ledger.corrections c JOIN ledger.operations op ON op.id=c.operation_id
            WHERE op.owner_id=owner AND EXISTS(SELECT 1 FROM reached r JOIN ledger.lots l ON l.id=r.id JOIN lot_metadata meta ON meta.lot_id=l.id
                WHERE l.operation_id=c.operation_id OR l.operation_id=c.replacement_operation_id)),'[]'::jsonb)
    ) INTO result WHERE EXISTS(SELECT 1 FROM actor);

    IF result IS NULL THEN RAISE EXCEPTION 'Trace permission changed' USING ERRCODE='42501'; END IF;
    IF (result->>'lotCount')::integer>5000 OR (result->>'edgeCount')::integer>20000 THEN
        RAISE EXCEPTION 'Trace exceeds interactive limit (5000 lots / 20000 edges); no complete result returned' USING ERRCODE='23514';
    END IF;
    INSERT INTO ledger.events(id,kind,actor_id,lot_id,details)
    VALUES(trace_id,'TRACEBACK',a,target,jsonb_build_object(
        'schemaVersion',2,'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS','ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',result->'asOf','snapshot',result->'snapshot','lotCount',result->'lotCount',
        'lotIds',(SELECT jsonb_agg(x->'id') FROM jsonb_array_elements(result->'lots') x)
    ));
    RETURN result||jsonb_build_object('complete',true);
END $$;
REVOKE ALL ON FUNCTION ledger.trace_lot(uuid,uuid,text,text) FROM PUBLIC;
