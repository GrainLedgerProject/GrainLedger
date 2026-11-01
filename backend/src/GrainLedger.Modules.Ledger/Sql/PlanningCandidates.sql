-- Owner-scoped, facility-wide candidate evidence. Called inside a read-only
-- repeatable-read transaction; never changes positions, holds, or plans.
CREATE FUNCTION ledger.planning_candidates(a uuid, o uuid, f uuid) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger,capture,catalog AS $$
DECLARE result jsonb; position_count bigint; node_count bigint; edge_count bigint;
BEGIN
    IF ledger.role(a) NOT IN ('ExportStaff','CustomerManager') OR NOT ledger.own(a,o) THEN
        RAISE EXCEPTION 'Candidate access denied' USING ERRCODE='42501';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM ledger.facilities WHERE id=f) THEN
        RAISE EXCEPTION 'Unknown facility' USING ERRCODE='22023';
    END IF;
    SELECT count(*) INTO position_count
    FROM ledger.positions p JOIN ledger.locations loc ON loc.id=p.location_id
    JOIN ledger.lots l ON l.id=p.lot_id
    WHERE l.owner_id=o AND loc.facility_id=f AND p.quantity_kg>0;
    IF position_count>10000 THEN
        RAISE EXCEPTION 'Candidate position scope too large' USING ERRCODE='GL014';
    END IF;

    WITH RECURSIVE
    stock AS MATERIALIZED (
        SELECT p.lot_id,p.location_id,p.quantity_kg,loc.name location_name,
            loc.kind location_kind,loc.cleared,loc.mixed_bulk,loc.owner_id location_owner_id,
            pool.lot_id current_pool_lot_id,pool.revision pool_revision
        FROM ledger.positions p JOIN ledger.locations loc ON loc.id=p.location_id
        JOIN ledger.lots lot ON lot.id=p.lot_id
        LEFT JOIN ledger.mixed_bulk_pools pool ON pool.location_id=loc.id
        WHERE lot.owner_id=o AND loc.facility_id=f AND p.quantity_kg>0
    ),
    graph_edges AS MATERIALIZED (
        SELECT 'CUSTODY_'||ce.action kind,ca.event_id,NULL::uuid operation_id,
            ca.input_lot_id,ca.output_lot_id,ca.source_lot_id,ca.quantity_kg attributed_kg,
            ce.created_at occurred_at
        FROM ledger.custody_allocations ca JOIN ledger.custody_events ce ON ce.event_id=ca.event_id
        JOIN ledger.lots outlot ON outlot.id=ca.output_lot_id AND outlot.owner_id=o
        WHERE ca.quantity_kg>0
        UNION ALL
        SELECT 'PROCESSING',cpa.event_id,cpa.operation_id,cpa.input_lot_id,cpa.output_lot_id,
            cpa.source_lot_id,cpa.quantity_kg,op.occurred_at
        FROM ledger.custody_processing_allocations cpa
        JOIN ledger.operations op ON op.id=cpa.operation_id AND op.owner_id=o
        WHERE cpa.quantity_kg>0
        UNION ALL
        -- Pre-mixed-bulk operations have input/output links, but no exact
        -- input-to-source allocation. Expose them as unproven, never fabricate it.
        SELECT 'PROCESSING_UNATTRIBUTED',ev.id,op.id,(input.value->>'lotId')::uuid,
            output_lot.id,NULL::uuid,NULL::numeric,op.occurred_at
        FROM ledger.lots output_lot JOIN ledger.operations op ON op.id=output_lot.operation_id
        JOIN LATERAL jsonb_array_elements(op.content->'inputs') input(value) ON true
        LEFT JOIN LATERAL (SELECT id FROM ledger.events WHERE operation_id=op.id ORDER BY occurred_at,id LIMIT 1) ev ON true
        WHERE output_lot.owner_id=o AND op.status IN ('COMMITTED','CORRECTED')
          AND NOT EXISTS(SELECT 1 FROM ledger.custody_processing_allocations cpa WHERE cpa.output_lot_id=output_lot.id)
    ),
    ancestors(id) AS (
        SELECT DISTINCT lot_id FROM stock
        UNION
        SELECT edge.input_lot_id FROM graph_edges edge JOIN ancestors n ON n.id=edge.output_lot_id
    ),
    scoped_edges AS MATERIALIZED (
        SELECT edge.* FROM graph_edges edge JOIN ancestors n ON n.id=edge.output_lot_id
    ),
    scoped_nodes AS MATERIALIZED (
        SELECT lot.id,lot.code,lot.stage,lot.product_identity,lot.operation_id,
            source.id capture_source_lot_id
        FROM ancestors n JOIN ledger.lots lot ON lot.id=n.id AND lot.owner_id=o
        LEFT JOIN capture.source_lots source ON source.lot_id=lot.id AND source.company_id=o
    ),
    scope_counts AS (
        SELECT (SELECT count(*) FROM scoped_nodes) nodes,(SELECT count(*) FROM scoped_edges) edges
    )
    SELECT jsonb_build_object(
        'positionCount',position_count,
        'nodeCount',(SELECT nodes FROM scope_counts),
        'edgeCount',(SELECT edges FROM scope_counts),
        'positions',COALESCE((SELECT jsonb_agg(
            ledger.lot_json(a,s.lot_id,false) || jsonb_build_object(
                'locationId',s.location_id,'locationName',s.location_name,
                'locationKind',s.location_kind,'locationCleared',s.cleared,
                'locationMixedBulk',s.mixed_bulk,'locationOwnerId',s.location_owner_id,
                'onHandKg',s.quantity_kg::text,
                'isCurrentPoolAtLocation',COALESCE(s.current_pool_lot_id=s.lot_id,false),
                'positionPoolRevision',CASE WHEN s.current_pool_lot_id=s.lot_id THEN s.pool_revision::text END,
                'catalogCommodityId',mapping.catalog_commodity_id)
            ORDER BY s.lot_id,s.location_id)
            FROM stock s JOIN ledger.lots lot ON lot.id=s.lot_id
            LEFT JOIN ledger.catalog_commodities mapping ON mapping.ledger_commodity_id=lot.commodity_id),'[]'::jsonb),
        'nodes',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'lotId',n.id,'lotCode',n.code,'stage',n.stage,'productIdentity',n.product_identity,
            'operationId',n.operation_id,'captureSourceLotId',n.capture_source_lot_id)
            ORDER BY n.id) FROM scoped_nodes n),'[]'::jsonb),
        'edges',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'kind',e.kind,'eventId',e.event_id,'operationId',e.operation_id,
            'inputLotId',e.input_lot_id,'outputLotId',e.output_lot_id,
            'sourceLotId',e.source_lot_id,'attributedQuantityKg',e.attributed_kg::text,
            'occurredAt',e.occurred_at) ORDER BY e.output_lot_id,e.input_lot_id,e.source_lot_id,e.event_id)
            FROM scoped_edges e),'[]'::jsonb),
        'composition',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'lotId',c.lot_id,'ledgerSourceLotId',c.source_lot_id,
            'captureSourceLotId',source.id,'lotAttributedKg',c.quantity_kg::text)
            ORDER BY c.lot_id,c.source_lot_id)
            FROM ledger.contributions c JOIN (SELECT DISTINCT lot_id FROM stock) s ON s.lot_id=c.lot_id
            LEFT JOIN capture.source_lots source ON source.lot_id=c.source_lot_id AND source.company_id=o
            WHERE c.quantity_kg>0),'[]'::jsonb),
        'operations',COALESCE((SELECT jsonb_agg(jsonb_build_object(
            'id',op.id,'kind',op.kind,'status',op.status,'occurredAt',op.occurred_at,
            'content',op.content,'varianceKg',op.variance_kg::text,
            'allowedVarianceKg',op.allowed_variance_kg::text)
            ORDER BY op.id)
            FROM ledger.operations op WHERE op.id IN
                (SELECT DISTINCT operation_id FROM scoped_nodes WHERE operation_id IS NOT NULL)),'[]'::jsonb)
    ) INTO result;

    node_count:=(result->>'nodeCount')::bigint;
    edge_count:=(result->>'edgeCount')::bigint;
    IF node_count>50000 OR edge_count>100000 THEN
        RAISE EXCEPTION 'Candidate ancestry scope too large' USING ERRCODE='GL014';
    END IF;
    RETURN result;
END $$;

REVOKE ALL ON FUNCTION ledger.planning_candidates(uuid,uuid,uuid) FROM PUBLIC;
