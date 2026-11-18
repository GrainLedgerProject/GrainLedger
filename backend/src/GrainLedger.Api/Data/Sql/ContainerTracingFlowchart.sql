-- Bulk material graph shared by lot tracing and container backward tracing.
-- Private: callers validate scope and append exactly one audit in their transaction.
CREATE FUNCTION grainledger.ledger_trace_graph(a uuid, targets uuid[], direction text, view_mode text) RETURNS jsonb
    LANGUAGE plpgsql SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
DECLARE
    actor_role text;
    owner uuid;
    result jsonb;
    trace_id uuid:=gen_random_uuid();
BEGIN
    IF targets IS NULL OR cardinality(targets) NOT BETWEEN 1 AND 5000 OR array_position(targets,NULL) IS NOT NULL THEN
        RAISE EXCEPTION 'Valid trace roots required' USING ERRCODE='22023';
    END IF;
    actor_role:=grainledger.ledger_role(a);
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
    SELECT owner_id INTO owner FROM grainledger.ledger_lots WHERE id=targets[1];
    -- Same error for an absent or inaccessible identity. Pending sources are not Ledger lots.
    IF owner IS NULL OR (actor_role<>'Administrator' AND NOT grainledger.ledger_own(a,owner))
       OR EXISTS(SELECT 1 FROM unnest(targets) t LEFT JOIN grainledger.ledger_lots l ON l.id=t WHERE l.id IS NULL OR l.owner_id<>owner) THEN
        RAISE EXCEPTION 'Trace lot unavailable in current scope; use an accepted Ledger lot' USING ERRCODE='42501';
    END IF;

    -- One statement captures graph, stock, restrictions and retained source weights together.
    WITH RECURSIVE
    actor AS MATERIALIZED (
        SELECT u."Role" role FROM grainledger.identity_users u
        WHERE u."Id"=a AND u."IsActive" AND u."Role"=actor_role
          AND (u."Role"='Administrator' OR u."OrganizationId"=owner)
    ),
    operations AS MATERIALIZED (
        SELECT op.* FROM grainledger.ledger_operations op
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
            SELECT e.id,e.details FROM grainledger.ledger_events e
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
        JOIN grainledger.ledger_lots il ON il.id=(pi.value->>'lotId')::uuid AND il.owner_id=owner
        JOIN grainledger.ledger_lots ol ON ol.id=(o.value->>'id')::uuid AND ol.owner_id=owner AND ol.operation_id=op.id
        GROUP BY op.id,pi.event_id,pi.basis,(pi.value->>'lotId')::uuid,(o.value->>'id')::uuid,(o.value->>'quantityKg')::numeric
        UNION ALL
        SELECT 'CUSTODY',ce.event_id,NULL::uuid,ce.event_id,'CUSTODY_ALLOCATION',ce.input_lot_id,ce.output_lot_id,
               ce.quantity_kg,(SELECT sum(c.quantity_kg) FROM grainledger.ledger_contributions c WHERE c.lot_id=ce.output_lot_id)
        FROM grainledger.ledger_custody_edges ce
        JOIN grainledger.ledger_lots il ON il.id=ce.input_lot_id AND il.owner_id=owner
        JOIN grainledger.ledger_lots ol ON ol.id=ce.output_lot_id AND ol.owner_id=owner
        WHERE EXISTS(SELECT 1 FROM actor)
    ),
    reached(id) AS (
        SELECT unnest(targets) WHERE EXISTS(SELECT 1 FROM actor)
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
        FROM trace_edges e JOIN grainledger.ledger_custody_allocations ca
          ON e.relation_type='CUSTODY' AND ca.event_id=e.event_id
          AND ca.input_lot_id=e.input_lot_id AND ca.output_lot_id=e.output_lot_id
        JOIN grainledger.ledger_lots source ON source.id=ca.source_lot_id AND source.owner_id=owner
        WHERE actor_role<>'ExportStaff'
        UNION ALL
        SELECT e.relation_type,e.relation_id,e.input_lot_id,e.output_lot_id,pa.source_lot_id,pa.quantity_kg
        FROM trace_edges e JOIN grainledger.ledger_custody_processing_allocations pa
          ON e.relation_type='PROCESSING' AND pa.event_id=e.event_id AND pa.operation_id=e.operation_id
          AND pa.input_lot_id=e.input_lot_id AND pa.output_lot_id=e.output_lot_id
        JOIN grainledger.ledger_lots source ON source.id=pa.source_lot_id AND source.owner_id=owner
        WHERE actor_role<>'ExportStaff'
    ),
    positive_paths(lot_id,source_lot_id) AS (
        SELECT c.lot_id,c.source_lot_id FROM grainledger.ledger_contributions c
        JOIN grainledger.ledger_lots source ON source.id=c.source_lot_id AND source.owner_id=owner
        WHERE c.lot_id=ANY(targets) AND c.quantity_kg>0 AND actor_role<>'ExportStaff'
        UNION
        SELECT CASE WHEN direction='forward' THEN allocation.output_lot_id ELSE allocation.input_lot_id END,path.source_lot_id
        FROM positive_paths path JOIN allocations allocation
          ON allocation.source_lot_id=path.source_lot_id AND allocation.quantity_kg>0
          AND ((direction='forward' AND allocation.input_lot_id=path.lot_id)
            OR (direction='backward' AND allocation.output_lot_id=path.lot_id))
    ),
    lot_metadata AS MATERIALIZED (SELECT * FROM grainledger.ledger_trace_lot_metadata(a,(SELECT array_agg(id) FROM reached))),
    source_weights AS MATERIALIZED (
        SELECT c.* FROM grainledger.ledger_contributions c JOIN reached r ON r.id=c.lot_id
        WHERE actor_role<>'ExportStaff'
    )
    SELECT jsonb_build_object(
        'schemaVersion',2,'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS',
        'positivePathCoverage',CASE WHEN EXISTS(SELECT 1 FROM trace_edges WHERE basis='RECORDED_CONTENT') THEN 'PARTIAL_LEGACY' ELSE 'PERSISTED_ALLOCATIONS' END,
        'traceId',trace_id,'targetLotId',CASE WHEN cardinality(targets)=1 THEN targets[1] ELSE NULL END,'ownerId',owner,'direction',direction,'view',view_mode,
        'asOf',statement_timestamp(),'snapshot',pg_current_snapshot()::text,
        'sourceContributionsVisible',actor_role<>'ExportStaff',
        'lotCount',(SELECT count(*) FROM reached),'edgeCount',(SELECT count(*) FROM trace_edges),
        'lots',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',l.id,'code',l.code,'stage',l.stage,'operationId',l.operation_id,
            'kind',meta.lot_kind,'enteredPool',meta.entered_pool,
            'directHold',EXISTS(SELECT 1 FROM grainledger.ledger_holds h WHERE h.lot_id=l.id),
            'effectiveHold',EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l.id)),
            'positivePathSourceIds',CASE WHEN actor_role='ExportStaff' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(p.source_lot_id ORDER BY p.source_lot_id) FROM positive_paths p WHERE p.lot_id=l.id),'[]'::jsonb) END,
            'isCurrentPool',meta.is_current_pool,'poolRevision',meta.pool_revision,
            'producedKg',(SELECT coalesce(sum(c.quantity_kg),0)::text FROM grainledger.ledger_contributions c WHERE c.lot_id=l.id),
            'positions',meta.positions,
            'restrictions',to_jsonb(array_remove(ARRAY[
                CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l.id)) THEN 'HOLD' END,
                CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_corrections WHERE operation_id=l.operation_id AND status IN ('REQUESTED','RECONCILED'))
                    OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_correction_blocks WHERE receipt_id=l.receipt_id) THEN 'CORRECTION' END,
                CASE WHEN grainledger.ledger_receiving_restricted(l.id) THEN 'RECEIVING_DISCREPANCY' END,
                CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_operations WHERE id=l.operation_id AND status='CORRECTED')
                    OR EXISTS(SELECT 1 FROM grainledger.ledger_receipt_reversals WHERE receipt_id=l.receipt_id) THEN 'SUPERSEDED' END
            ],NULL)),
            'contributions',CASE WHEN actor_role='ExportStaff' THEN NULL ELSE coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,'quantityKg',s.quantity_kg::text,
                    'denominatorKg',(SELECT sum(c.quantity_kg)::text FROM grainledger.ledger_contributions c WHERE c.lot_id=l.id),
                    'percentage',round(100*s.quantity_kg/NULLIF((SELECT sum(c.quantity_kg) FROM grainledger.ledger_contributions c WHERE c.lot_id=l.id),0),6)::text
                ) ORDER BY s.source_lot_id) FROM source_weights s WHERE s.lot_id=l.id
            ),'[]'::jsonb) END
        ) ORDER BY l.id) FROM reached r JOIN grainledger.ledger_lots l ON l.id=r.id JOIN lot_metadata meta ON meta.lot_id=l.id
            WHERE (SELECT count(*) FROM reached)<=5000),'[]'::jsonb),
        'edges',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'relationType',relation_type,'relationId',relation_id,'basis',basis,'eventId',event_id,
            'operationId',operation_id,'inputLotId',input_lot_id,'outputLotId',output_lot_id,
            'inputConsumedKg',input_kg::text,'outputProducedKg',output_kg::text,
            'inputEventKg',CASE WHEN relation_type='CUSTODY' THEN (SELECT sum(ce.quantity_kg)::text FROM grainledger.ledger_custody_edges ce
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
        ) ORDER BY ce.event_id) FROM grainledger.ledger_custody_events ce WHERE EXISTS(
            SELECT 1 FROM trace_edges e WHERE e.relation_type='CUSTODY' AND e.event_id=ce.event_id)),'[]'::jsonb),
        'operations',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',op.id,'kind',op.kind,'status',op.status,'occurredAt',op.occurred_at,
            'declaredLossKg',op.content->>'declaredLossKg','varianceKg',op.variance_kg::text
        ) ORDER BY op.id) FROM operations op WHERE EXISTS(SELECT 1 FROM trace_edges e WHERE e.operation_id=op.id)),'[]'::jsonb),
        'corrections',coalesce((SELECT jsonb_agg(jsonb_build_object(
            'id',c.id,'originalOperationId',c.operation_id,'replacementOperationId',c.replacement_operation_id,'status',c.status
        ) ORDER BY c.id) FROM grainledger.ledger_corrections c JOIN grainledger.ledger_operations op ON op.id=c.operation_id
            WHERE op.owner_id=owner AND EXISTS(SELECT 1 FROM reached r JOIN grainledger.ledger_lots l ON l.id=r.id JOIN lot_metadata meta ON meta.lot_id=l.id
                WHERE l.operation_id=c.operation_id OR l.operation_id=c.replacement_operation_id)),'[]'::jsonb)
    ) INTO result WHERE EXISTS(SELECT 1 FROM actor);

    IF result IS NULL THEN RAISE EXCEPTION 'Trace permission changed' USING ERRCODE='42501'; END IF;
    IF (result->>'lotCount')::integer>5000 OR (result->>'edgeCount')::integer>20000 THEN
        RAISE EXCEPTION 'Trace exceeds interactive limit (5000 lots / 20000 edges); no complete result returned' USING ERRCODE='23514';
    END IF;
    RETURN result||jsonb_build_object('complete',true);
END $$;

REVOKE ALL ON FUNCTION grainledger.ledger_trace_graph(uuid,uuid[],text,text) FROM PUBLIC;

-- Preserve the existing public lot route, audit shape, and runtime EXECUTE grant.
CREATE OR REPLACE FUNCTION grainledger.ledger_trace_lot(a uuid, target uuid, direction text, view_mode text) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE result jsonb;
BEGIN
 result:=grainledger.ledger_trace_graph(a,ARRAY[target],direction,view_mode);
 INSERT INTO grainledger.ledger_events(id,kind,actor_id,lot_id,details)
 VALUES((result->>'traceId')::uuid,'TRACEBACK',a,target,jsonb_build_object(
  'schemaVersion',2,'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS','ownerId',result->'ownerId','direction',direction,'view',view_mode,
  'asOf',result->'asOf','snapshot',result->'snapshot','lotCount',result->'lotCount',
  'lotIds',(SELECT jsonb_agg(x->'id') FROM jsonb_array_elements(result->'lots') x)));
 RETURN result;
END $$;

CREATE OR REPLACE FUNCTION grainledger.ledger_container_targets(a uuid,root uuid,lot_ids uuid[]) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE r text:=grainledger.ledger_role(a); result jsonb; source_root boolean;
BEGIN
 IF r IS NULL OR r NOT IN ('CustomerManager','ExportStaff','Administrator') OR lot_ids IS NULL OR root IS NULL OR array_position(lot_ids,NULL) IS NOT NULL OR cardinality(lot_ids) NOT BETWEEN 1 AND 10000
  OR NOT root=ANY(lot_ids) OR EXISTS(SELECT 1 FROM unnest(lot_ids) x WHERE r<>'Administrator' AND NOT grainledger.ledger_can_read_lot(a,x)) THEN
  RAISE EXCEPTION 'Actual container targets unavailable in current trace scope' USING ERRCODE='42501'; END IF;
 SELECT EXISTS(SELECT 1 FROM grainledger.ledger_contributions WHERE lot_id=root AND source_lot_id=root AND quantity_kg>0) INTO source_root;
 WITH linked AS MATERIALIZED (
  SELECT c.id,c.internal_code,c.status,l.quantity_kg,l.id line_id,l.lot_id,l.location_id,h.id load_id,h.proposal_id,h.loaded_at
  FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
  JOIN grainledger.exports_containers c ON c.id=h.container_id
  WHERE l.lot_id=ANY(lot_ids) AND (r='Administrator' OR grainledger.ledger_own(a,c.company_id))
   AND (NOT source_root OR EXISTS(SELECT 1 FROM grainledger.ledger_container_load_sources s WHERE s.line_id=l.id AND s.source_lot_id=root AND s.quantity_kg>0))
 ), totals AS (
  SELECT id,internal_code,status,sum(quantity_kg) linked_kg FROM linked GROUP BY id,internal_code,status
 ), all_manifest_lots AS MATERIALIZED (
  SELECT DISTINCT h.container_id,l.lot_id FROM grainledger.ledger_container_loads h
  JOIN totals t ON t.id=h.container_id JOIN grainledger.ledger_container_load_lines l ON l.load_id=h.id
 ), restrictions AS MATERIALIZED (
  SELECT lot_id,grainledger.ledger_container_lot_blocked(lot_id) blocked FROM (SELECT DISTINCT lot_id FROM all_manifest_lots) lots
 ) SELECT jsonb_build_object('complete',(SELECT count(*) FROM totals)<=10000 AND (SELECT count(*) FROM linked)<=20000,
  'loadingEdges',coalesce((SELECT jsonb_agg(edge ORDER BY container_id,lot_id) FROM (
   SELECT id container_id,lot_id,jsonb_build_object('containerId',id,'lotId',lot_id,
    'quantityKg',sum(quantity_kg)::numeric(20,6)::text,'basis','ACTUAL_LOADING',
    'lines',jsonb_agg(jsonb_build_object('lineId',line_id,'loadId',load_id,'locationId',location_id,
     'proposalId',proposal_id,'loadedAt',loaded_at,'quantityKg',quantity_kg::numeric(20,6)::text) ORDER BY loaded_at,load_id,line_id)) edge
   FROM linked GROUP BY id,lot_id) grouped),'[]'::jsonb), 'items',
  CASE WHEN (SELECT count(*) FROM totals)>10000 THEN '[]'::jsonb ELSE coalesce((SELECT jsonb_agg(jsonb_build_object('containerId',id,'code',internal_code,'status',status,
    'linkedLoadedKg',linked_kg::numeric(20,6)::text,'basis','ACTUAL_LOADING',
    'held',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id),
    'restricted',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds h WHERE h.container_id=totals.id) OR EXISTS(SELECT 1 FROM all_manifest_lots l JOIN restrictions r ON r.lot_id=l.lot_id WHERE l.container_id=totals.id AND r.blocked)) ORDER BY internal_code,id) FROM totals),'[]'::jsonb) END) INTO result;
 IF NOT (result->>'complete')::boolean THEN RAISE EXCEPTION 'Actual container target set exceeds complete result limit' USING ERRCODE='22023'; END IF;
 RETURN result;
END $$;

-- Public read-only graph routine. Existing execution/trace appends the single container audit.
CREATE FUNCTION grainledger.ledger_container_trace_graph(a uuid,target uuid) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,grainledger,pg_temp AS $$
DECLARE r text:=grainledger.ledger_role(a); c grainledger.exports_containers; roots uuid[];
 result jsonb; loading jsonb; header jsonb;
BEGIN
 SELECT * INTO c FROM grainledger.exports_containers WHERE id=target;
 IF r IS NULL OR r NOT IN ('CustomerManager','Administrator') OR c.id IS NULL
  OR (r<>'Administrator' AND NOT grainledger.ledger_own(a,c.company_id)) THEN
  RAISE EXCEPTION 'Container sources unavailable in current scope' USING ERRCODE='42501'; END IF;
 IF (SELECT count(*) FROM grainledger.ledger_container_load_lines l
  JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target)>20000 THEN
  RAISE EXCEPTION 'Container trace exceeds complete loading limit' USING ERRCODE='23514'; END IF;
 SELECT array_agg(DISTINCT l.lot_id) INTO roots FROM grainledger.ledger_container_load_lines l
 JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target;
 IF roots IS NULL THEN
  result:=jsonb_build_object('schemaVersion',2,'traceId',gen_random_uuid(),'ownerId',c.company_id,
   'asOf',statement_timestamp(),'snapshot',pg_current_snapshot()::text,'direction','backward','view','current',
   'graphBasis','RECORDED_MATERIAL_RELATIONSHIPS','sourceContributionsVisible',true,'complete',true,
   'lotCount',0,'edgeCount',0,'lots','[]'::jsonb,'edges','[]'::jsonb,'operations','[]'::jsonb,
   'custodyEvents','[]'::jsonb,'corrections','[]'::jsonb);
 ELSE
  result:=grainledger.ledger_trace_graph(a,roots,'backward','current');
  IF (result->>'ownerId')::uuid<>c.company_id THEN
   RAISE EXCEPTION 'Container manifest owner mismatch' USING ERRCODE='23514'; END IF;
 END IF;
 SELECT coalesce(jsonb_agg(edge ORDER BY lot_id),'[]'::jsonb) INTO loading FROM (
  SELECT l.lot_id,jsonb_build_object('containerId',target,'lotId',l.lot_id,'basis','ACTUAL_LOADING',
   'quantityKg',sum(l.quantity_kg)::numeric(20,6)::text,
   'lines',jsonb_agg(jsonb_build_object('lineId',l.id,'loadId',h.id,'locationId',l.location_id,
    'proposalId',h.proposal_id,'loadedAt',h.loaded_at,'quantityKg',l.quantity_kg::numeric(20,6)::text,
    'sourceContributions',coalesce((SELECT jsonb_agg(jsonb_build_object('sourceLotId',s.source_lot_id,
      'quantityKg',s.quantity_kg::numeric(20,6)::text) ORDER BY s.source_lot_id)
     FROM grainledger.ledger_container_load_sources s WHERE s.line_id=l.id),'[]'::jsonb)) ORDER BY h.loaded_at,h.id,l.id)) edge
  FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id
  WHERE h.container_id=target GROUP BY l.lot_id) grouped;
 header:=jsonb_build_object('containerId',target,'code',c.internal_code,'status',c.status,'basis','ACTUAL_LOADING',
  'linkedLoadedKg',coalesce((SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l
   JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=target),0)::numeric(20,6)::text,
  'held',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target),
  'restricted',EXISTS(SELECT 1 FROM grainledger.ledger_container_holds WHERE container_id=target)
   OR EXISTS(SELECT 1 FROM unnest(roots) lot WHERE grainledger.ledger_container_lot_blocked(lot)));
 RETURN (result-'targetLotId')||jsonb_build_object('root',jsonb_build_object('type','container','id',target),
  'actualContainers',jsonb_build_array(header),'loadingEdges',loading,'containerCoverage','ACTUAL_LOADING');
END $$;
REVOKE ALL ON FUNCTION grainledger.ledger_container_trace_graph(uuid,uuid) FROM PUBLIC;
