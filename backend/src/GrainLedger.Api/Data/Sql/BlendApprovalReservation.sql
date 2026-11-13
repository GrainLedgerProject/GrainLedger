CREATE FUNCTION grainledger.ledger_blend_reserved_kg(lot uuid, location uuid) RETURNS numeric
LANGUAGE sql STABLE SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
AS $$
    SELECT COALESCE(SUM(r.quantity_kg - COALESCE((
        SELECT SUM(e.quantity_kg)
        FROM grainledger.ledger_blend_reservation_events e
        WHERE (e.proposal_id,e.lot_id,e.location_id) = (r.proposal_id,r.lot_id,r.location_id)
    ),0)),0)
    FROM grainledger.ledger_blend_reservations r
    WHERE r.lot_id = lot AND r.location_id = location
$$;

CREATE FUNCTION grainledger.ledger_protect_blend_reserved_position() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
AS $$
DECLARE remaining numeric;
BEGIN
    IF TG_OP = 'UPDATE' AND NEW.lot_id = OLD.lot_id AND NEW.location_id = OLD.location_id
       AND NEW.quantity_kg >= OLD.quantity_kg THEN RETURN NEW; END IF;
    remaining := grainledger.ledger_blend_reserved_kg(OLD.lot_id, OLD.location_id);
    IF remaining > 0 AND (TG_OP = 'DELETE' OR NEW.lot_id <> OLD.lot_id
        OR NEW.location_id <> OLD.location_id OR NEW.quantity_kg < remaining) THEN
        RAISE EXCEPTION 'Position is protected by an export blend reservation' USING ERRCODE='23514';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER ledger_protect_blend_reserved_position
BEFORE UPDATE OR DELETE ON grainledger.ledger_positions
FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_protect_blend_reserved_position();

CREATE FUNCTION grainledger.ledger_reserve_approved_blend(a uuid, proposal uuid) RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
AS $$
DECLARE approved record; allocation record; available numeric; allocation_count integer := 0;
BEGIN
    PERFORM grainledger.ledger_lock_writes();
    SELECT p.company_id, (v.request_json->>'FacilityId')::uuid AS facility_id,
           p.snapshot_json->'selection'->'allocations' AS allocations
    INTO approved
    FROM grainledger.exports_blend_plan_proposals p
    JOIN grainledger.exports_blend_plan_previews v ON v.id = p.preview_id AND v.company_id = p.company_id
    JOIN grainledger.exports_active_blend_approvals active ON active.proposal_id = p.id AND active.company_id = p.company_id
    JOIN grainledger.exports_blend_plan_decisions d ON d.proposal_id = p.id AND d.version = 1
    WHERE p.id = proposal AND d.action = 'APPROVED' AND d.actor_id = a;
    IF NOT FOUND OR grainledger.ledger_role(a) IS DISTINCT FROM 'CustomerManager'
       OR NOT COALESCE(grainledger.ledger_own(a,approved.company_id),false) THEN
        RAISE EXCEPTION 'Blend reservation access denied' USING ERRCODE='42501';
    END IF;
    IF approved.facility_id IS NULL OR jsonb_typeof(approved.allocations) IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Missing approved stock allocations' USING ERRCODE='23514';
    END IF;
    FOR allocation IN
        SELECT x."lotId" AS lot_id, x."locationId" AS location_id, x."quantityKg" AS quantity_kg
        FROM jsonb_to_recordset(approved.allocations) AS x("lotId" uuid, "locationId" uuid, "quantityKg" numeric)
        ORDER BY 1, 2
    LOOP
        IF allocation.quantity_kg IS NULL OR allocation.quantity_kg <= 0
            OR allocation.quantity_kg > 99999999999999.999999
            OR trunc(allocation.quantity_kg,6) <> allocation.quantity_kg THEN
            RAISE EXCEPTION 'Invalid approved stock quantity' USING ERRCODE='23514';
        END IF;
        SELECT pos.quantity_kg - grainledger.ledger_blend_reserved_kg(pos.lot_id,pos.location_id)
        INTO available
        FROM grainledger.ledger_positions pos
        JOIN grainledger.ledger_lots lot ON lot.id = pos.lot_id
        JOIN grainledger.ledger_locations loc ON loc.id = pos.location_id
        LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id = loc.id
        WHERE pos.lot_id = allocation.lot_id AND pos.location_id = allocation.location_id
            AND lot.owner_id = approved.company_id AND loc.facility_id = approved.facility_id AND NOT loc.is_deleted
            AND (loc.owner_id IS NULL OR loc.owner_id = approved.company_id)
            AND (NOT loc.mixed_bulk OR pool.lot_id = pos.lot_id)
            AND NOT grainledger.ledger_blocked(pos.lot_id)
        FOR UPDATE OF pos;
        IF NOT FOUND OR available < allocation.quantity_kg THEN
            RAISE EXCEPTION 'Approved stock position is no longer available' USING ERRCODE='23514';
        END IF;
        INSERT INTO grainledger.ledger_blend_reservations(proposal_id,lot_id,location_id,quantity_kg)
        VALUES (proposal, allocation.lot_id, allocation.location_id, allocation.quantity_kg);
        allocation_count := allocation_count + 1;
    END LOOP;
    IF allocation_count = 0 THEN
        RAISE EXCEPTION 'Missing approved stock allocations' USING ERRCODE='23514';
    END IF;
    RETURN allocation_count;
END;
$$;

REVOKE ALL ON FUNCTION grainledger.ledger_blend_reserved_kg(uuid,uuid),
    grainledger.ledger_protect_blend_reserved_position(),
    grainledger.ledger_reserve_approved_blend(uuid,uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION grainledger.ledger_lot_json(a uuid, l uuid, detail boolean DEFAULT false) RETURNS jsonb
    LANGUAGE sql STABLE
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT jsonb_build_object(
        'id',x.id,'code',x.code,'ownerId',x.owner_id,'commodityId',x.commodity_id,
        'stage',x.stage,'productIdentity',x.product_identity,
        'positions',coalesce((
            SELECT jsonb_agg(jsonb_build_object(
                'locationId',p.location_id,'facilityId',lc.facility_id,
                'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN grainledger.ledger_blocked(l) THEN '0.000000' ELSE GREATEST(0,p.quantity_kg-grainledger.ledger_blend_reserved_kg(p.lot_id,p.location_id))::numeric(20,6)::text END
            ) || CASE WHEN lc.mixed_bulk AND pool.lot_id=l THEN jsonb_build_object('poolRevision',pool.revision) ELSE '{}'::jsonb END)
            FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations lc ON lc.id=p.location_id
            LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
            WHERE p.lot_id=l AND (grainledger.ledger_own(a,x.owner_id) OR grainledger.ledger_assigned(a,lc.facility_id))
        ),'[]'::jsonb),
        'restrictions',to_jsonb(array_remove(ARRAY[
            CASE WHEN EXISTS(SELECT 1 FROM grainledger.ledger_effective_hold_ids(l)) THEN 'HOLD' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM grainledger.ledger_corrections
                WHERE operation_id=x.operation_id AND status IN ('REQUESTED','RECONCILED')
            ) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_receipt_correction_blocks WHERE receipt_id=x.receipt_id
            ) THEN 'CORRECTION' END,
            CASE WHEN grainledger.ledger_receiving_restricted(l) THEN 'RECEIVING_DISCREPANCY' END,
            CASE WHEN EXISTS (
                SELECT 1 FROM grainledger.ledger_receipt_reversals WHERE receipt_id=x.receipt_id
            ) OR EXISTS (
                SELECT 1 FROM grainledger.ledger_operations WHERE id=x.operation_id AND status='CORRECTED'
            ) THEN 'SUPERSEDED' END
        ],NULL)),
        'mixedBulk',EXISTS(SELECT 1 FROM grainledger.ledger_custody_edges ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l)
            OR EXISTS(SELECT 1 FROM grainledger.ledger_positions p JOIN grainledger.ledger_locations lc ON lc.id=p.location_id WHERE p.lot_id=l AND lc.mixed_bulk),
        'poolLocationId',coalesce(
            (SELECT pool.location_id FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
            (SELECT ce.location_id FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l ORDER BY ce.created_at DESC LIMIT 1)),
        'poolRevision',(SELECT pool.revision FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
        'isCurrentPool',EXISTS(SELECT 1 FROM grainledger.ledger_mixed_bulk_pools pool WHERE pool.lot_id=l),
        'custodyDerived',grainledger.ledger_mixed_bulk_custody_derived(l),
        'sourceComposition',CASE WHEN detail THEN coalesce((
            SELECT jsonb_agg(jsonb_build_object('sourceLotId',shares.source_lot_id,'sourceLotCode',source.code,'quantityKg',shares.quantity_kg::text) ORDER BY shares.source_lot_id)
            FROM (
                SELECT ca.source_lot_id,sum(ca.quantity_kg) quantity_kg FROM grainledger.ledger_custody_allocations ca
                WHERE ca.output_lot_id=l GROUP BY ca.source_lot_id
                UNION ALL
                SELECT cpa.source_lot_id,sum(cpa.quantity_kg) FROM grainledger.ledger_custody_processing_allocations cpa
                WHERE cpa.output_lot_id=l GROUP BY cpa.source_lot_id
                UNION ALL
                SELECT c.source_lot_id,sum(c.quantity_kg) FROM grainledger.ledger_contributions c
                WHERE c.lot_id=l
                  AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_allocations ca WHERE ca.output_lot_id=l)
                  AND NOT EXISTS(SELECT 1 FROM grainledger.ledger_custody_processing_allocations cpa WHERE cpa.output_lot_id=l)
                GROUP BY c.source_lot_id
            ) shares JOIN grainledger.ledger_lots source ON source.id=shares.source_lot_id
        ),'[]'::jsonb) ELSE '[]'::jsonb END
    ) || CASE WHEN detail AND grainledger.ledger_role(a)<>'MillOperator' THEN
        jsonb_build_object(
            'contributions',coalesce((
                SELECT jsonb_agg(jsonb_build_object('sourceLotId',source_lot_id,'quantityKg',quantity_kg::text))
                FROM grainledger.ledger_contributions WHERE lot_id=l
            ),'[]'::jsonb),
            'custodyLinks',coalesce((
                SELECT jsonb_agg(jsonb_build_object('eventId',links.event_id,'inputLotId',links.input_lot_id,'outputLotId',links.output_lot_id,'quantityKg',links.quantity_kg::text,'locationId',links.location_id,'poolRevision',links.revision) ORDER BY links.created_at,links.event_id)
                FROM (
                    SELECT ce.event_id,ce.input_lot_id,ce.output_lot_id,ce.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM grainledger.ledger_custody_events ce WHERE ce.input_lot_id=l OR ce.output_lot_id=l
                    UNION ALL
                    SELECT ce.event_id,cpa.input_lot_id,cpa.output_lot_id,cpa.quantity_kg,ce.location_id,ce.revision,ce.created_at
                    FROM grainledger.ledger_custody_processing_allocations cpa JOIN grainledger.ledger_custody_events ce ON ce.event_id=cpa.event_id
                    WHERE cpa.input_lot_id=l OR cpa.output_lot_id=l
                ) links
                WHERE grainledger.ledger_can_read_lot(a,coalesce(links.input_lot_id,links.output_lot_id))
            ),'[]'::jsonb)
        )
    ELSE '{}'::jsonb END
    FROM grainledger.ledger_lots x
    WHERE x.id=l AND grainledger.ledger_can_read_lot(a,l)
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_trace_lot_metadata(a uuid, ids uuid[]) RETURNS TABLE(lot_id uuid, code text, owner_id uuid, stage text, lot_kind text, entered_pool boolean, is_current_pool boolean, pool_revision bigint, positions jsonb, quantity_kg numeric)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    WITH actor AS MATERIALIZED (
        SELECT u."Role" role,u."OrganizationId" owner_id FROM grainledger.identity_users u
        WHERE u."Id"=a AND u."IsActive" AND u."Role" IN ('Administrator','CustomerManager','ExportStaff')
    ), scoped AS MATERIALIZED (
        SELECT l.* FROM grainledger.ledger_lots l CROSS JOIN actor
        WHERE (actor.role='Administrator' OR l.owner_id=actor.owner_id) AND (ids IS NULL OR l.id=ANY(ids))
    ), origins AS MATERIALIZED (
        SELECT edge.output_lot_id, max(ce.revision) revision,
            max(CASE WHEN ce.action='INGRESS' THEN 'POOL_GENERATION'
                     WHEN edge.output_lot_id=ce.output_lot_id THEN 'ALIQUOT' ELSE 'REMAINDER' END) kind
        FROM grainledger.ledger_custody_edges edge JOIN grainledger.ledger_custody_events ce ON ce.event_id=edge.event_id
        JOIN scoped s ON s.id=edge.output_lot_id GROUP BY edge.output_lot_id
    ), balances AS MATERIALIZED (
        SELECT p.lot_id,sum(p.quantity_kg) quantity,
            jsonb_agg(jsonb_build_object('locationId',loc.id,'locationName',loc.name,'locationKind',loc.kind,
                'facilityId',facility.id,'facilityName',facility.name,'quantityKg',p.quantity_kg::text,
                'availableKg',CASE WHEN grainledger.ledger_blocked(p.lot_id) THEN '0.000000' ELSE GREATEST(0,p.quantity_kg-grainledger.ledger_blend_reserved_kg(p.lot_id,p.location_id))::numeric(20,6)::text END
            ) ORDER BY loc.id) positions
        FROM grainledger.ledger_positions p JOIN scoped s ON s.id=p.lot_id
        JOIN grainledger.ledger_locations loc ON loc.id=p.location_id JOIN grainledger.ledger_facilities facility ON facility.id=loc.facility_id
        GROUP BY p.lot_id
    )
    SELECT s.id,s.code,s.owner_id,s.stage,
        CASE WHEN pool.lot_id IS NOT NULL THEN 'CURRENT_POOL' WHEN s.receipt_id IS NOT NULL THEN 'SOURCE'
             WHEN origins.kind IS NOT NULL THEN origins.kind WHEN s.operation_id IS NOT NULL THEN 'PROCESSING_OUTPUT' ELSE 'LOT' END,
        EXISTS(SELECT 1 FROM grainledger.ledger_custody_edges e JOIN grainledger.ledger_custody_events ce ON ce.event_id=e.event_id
               WHERE e.input_lot_id=s.id AND ce.action='INGRESS'),
        pool.lot_id IS NOT NULL,COALESCE(pool.revision,origins.revision),COALESCE(balances.positions,'[]'::jsonb),COALESCE(balances.quantity,0)
    FROM scoped s LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.lot_id=s.id
    LEFT JOIN origins ON origins.output_lot_id=s.id LEFT JOIN balances ON balances.lot_id=s.id
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_procurement_stock(o uuid, l uuid) RETURNS TABLE(location_id uuid, on_hand_kg numeric, available_kg numeric)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    SELECT p.location_id,p.quantity_kg,CASE WHEN p.lot_id IS NULL THEN NULL
        WHEN grainledger.ledger_blocked(p.lot_id) THEN 0
        ELSE GREATEST(0,p.quantity_kg-grainledger.ledger_blend_reserved_kg(p.lot_id,p.location_id)) END
    FROM grainledger.capture_source_lots s LEFT JOIN grainledger.ledger_lots x ON x.id=s.lot_id AND x.owner_id=s.company_id
    LEFT JOIN grainledger.ledger_positions p ON p.lot_id=x.id WHERE s.lot_id=l AND s.company_id=o
$$;

CREATE OR REPLACE FUNCTION grainledger.ledger_procurement_stock_bulk(o uuid, source_ids uuid[]) RETURNS TABLE(source_lot_id uuid, current_stock_lot_id uuid, current_stock_lot_code text, location_id uuid, on_hand_kg numeric, available_kg numeric, pool_total_kg numeric, is_mixed_bulk boolean, pool_revision bigint)
    LANGUAGE sql STABLE SECURITY DEFINER
    SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
    AS $$
    WITH sources AS MATERIALIZED (
        SELECT s.lot_id AS root_lot_id
        FROM grainledger.capture_source_lots s
        JOIN grainledger.ledger_lots root ON root.id=s.lot_id AND root.owner_id=s.company_id
        WHERE s.company_id=o AND s.lot_id=ANY(source_ids)
    ), shares AS MATERIALIZED (
        SELECT c.lot_id,c.source_lot_id,sum(c.quantity_kg) AS creation_kg
        FROM grainledger.ledger_contributions c
        GROUP BY c.lot_id,c.source_lot_id
    ), creation_totals AS MATERIALIZED (
        SELECT lot_id,sum(creation_kg) AS quantity_kg
        FROM shares GROUP BY lot_id
    ), positions AS MATERIALIZED (
        SELECT p.lot_id,p.location_id,p.quantity_kg,lc.facility_id,lc.mixed_bulk,
            CASE WHEN lc.mixed_bulk THEN mbp.revision END AS pool_revision
        FROM grainledger.ledger_positions p
        JOIN grainledger.ledger_locations lc ON lc.id=p.location_id
        LEFT JOIN grainledger.ledger_mixed_bulk_pools mbp ON mbp.location_id=lc.id
        WHERE p.quantity_kg>0 AND (NOT lc.mixed_bulk OR mbp.lot_id=p.lot_id)
    ), raw_shares AS MATERIALIZED (
        SELECT p.*,sh.source_lot_id,
            sh.creation_kg*p.quantity_kg/NULLIF(t.quantity_kg,0) AS raw_kg
        FROM positions p
        JOIN shares sh ON sh.lot_id=p.lot_id
        JOIN creation_totals t ON t.lot_id=p.lot_id
        WHERE sh.creation_kg>0
    ), ranked_shares AS MATERIALIZED (
        SELECT r.*,
            trunc(r.raw_kg*1000000) AS whole_micro_kg,
            row_number() OVER (PARTITION BY r.lot_id,r.location_id
                ORDER BY r.raw_kg*1000000-trunc(r.raw_kg*1000000) DESC,r.source_lot_id) AS remainder_rank,
            round(r.quantity_kg*1000000)
                - sum(trunc(r.raw_kg*1000000)) OVER (PARTITION BY r.lot_id,r.location_id) AS remainder_micro_kg
        FROM raw_shares r
    ), allocated AS MATERIALIZED (
        SELECT r.*, (r.whole_micro_kg+
            CASE WHEN r.remainder_rank<=r.remainder_micro_kg THEN 1 ELSE 0 END)/1000000 AS allocated_kg
        FROM ranked_shares r
    ), free_shares AS MATERIALIZED (
        SELECT a.*, (GREATEST(0,a.quantity_kg-grainledger.ledger_blend_reserved_kg(a.lot_id,a.location_id))
            * 1000000) AS free_micro_kg
        FROM allocated a
    ), free_raw AS MATERIALIZED (
        SELECT f.*, f.allocated_kg * f.free_micro_kg / f.quantity_kg AS raw_free_micro_kg
        FROM free_shares f
    ), free_ranked AS MATERIALIZED (
        SELECT f.*,
            row_number() OVER (PARTITION BY f.lot_id,f.location_id
                ORDER BY f.raw_free_micro_kg-trunc(f.raw_free_micro_kg) DESC,f.source_lot_id) AS free_rank,
            f.free_micro_kg - sum(trunc(f.raw_free_micro_kg)) OVER
                (PARTITION BY f.lot_id,f.location_id) AS free_remainder
        FROM free_raw f
    )
    SELECT s.root_lot_id,l.id,l.code,p.location_id,a.allocated_kg,
        CASE WHEN grainledger.ledger_blocked(l.id) THEN 0::numeric ELSE (trunc(a.raw_free_micro_kg) + CASE WHEN a.free_rank<=a.free_remainder THEN 1 ELSE 0 END)/1000000 END,
        CASE WHEN p.mixed_bulk THEN p.quantity_kg ELSE 0::numeric END,
        p.mixed_bulk,p.pool_revision
    FROM sources s
    JOIN free_ranked a ON a.source_lot_id=s.root_lot_id
    JOIN positions p ON p.lot_id=a.lot_id AND p.location_id=a.location_id
    JOIN grainledger.ledger_lots l ON l.id=p.lot_id AND l.owner_id=o
    WHERE a.allocated_kg>0
    ORDER BY s.root_lot_id,p.location_id,l.id
$$;
