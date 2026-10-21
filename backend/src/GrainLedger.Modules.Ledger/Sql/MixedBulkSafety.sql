-- Mixed-bulk restrictions follow only positive immutable custody and processing allocations.
CREATE OR REPLACE FUNCTION ledger.mixed_bulk_custody_derived(l uuid) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
    WITH RECURSIVE lineage_allocations(input_lot_id,output_lot_id,source_lot_id,quantity_kg) AS (
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM ledger.custody_allocations WHERE quantity_kg>0
        UNION
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM ledger.custody_processing_allocations WHERE quantity_kg>0
    ), source_paths(lot_id,source_lot_id) AS (
        SELECT l,source_lot_id FROM ledger.contributions
        WHERE lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM ledger.custody_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM ledger.custody_processing_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT allocation.input_lot_id,path.source_lot_id
        FROM source_paths path
        JOIN lineage_allocations allocation
          ON allocation.output_lot_id=path.lot_id
         AND allocation.source_lot_id=path.source_lot_id
         AND allocation.quantity_kg>0
    )
    SELECT EXISTS (
        SELECT 1 FROM ledger.mixed_bulk_pools WHERE lot_id=l
    ) OR EXISTS (
        SELECT 1 FROM source_paths path
        JOIN ledger.custody_edges edge ON edge.output_lot_id=path.lot_id AND edge.quantity_kg>0
    )
$$;
REVOKE ALL ON FUNCTION ledger.mixed_bulk_custody_derived(uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.effective_hold_ids(l uuid) RETURNS SETOF uuid
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    WITH RECURSIVE lineage_allocations(input_lot_id,output_lot_id,source_lot_id,quantity_kg) AS (
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM ledger.custody_allocations WHERE quantity_kg>0
        UNION
        SELECT input_lot_id,output_lot_id,source_lot_id,quantity_kg
        FROM ledger.custody_processing_allocations WHERE quantity_kg>0
    ), source_paths(lot_id,source_lot_id) AS (
        SELECT l,source_lot_id FROM ledger.contributions
        WHERE lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM ledger.custody_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT l,source_lot_id FROM ledger.custody_processing_allocations
        WHERE output_lot_id=l AND quantity_kg>0
        UNION
        SELECT allocation.input_lot_id,path.source_lot_id
        FROM source_paths path
        JOIN lineage_allocations allocation
          ON allocation.output_lot_id=path.lot_id
         AND allocation.source_lot_id=path.source_lot_id
         AND allocation.quantity_kg>0
    ), derived(value) AS (
        SELECT ledger.mixed_bulk_custody_derived(l)
    ), effective_lots(lot_id) AS (
        SELECT l FROM derived
        UNION
        SELECT path.lot_id FROM source_paths path CROSS JOIN derived WHERE derived.value
        UNION
        SELECT path.source_lot_id FROM source_paths path CROSS JOIN derived WHERE derived.value
    )
    SELECT DISTINCT h.id
    FROM effective_lots related
    JOIN ledger.holds h ON h.lot_id=related.lot_id
    WHERE h.lot_id=l
       OR EXISTS (
            SELECT 1 FROM source_paths path
            WHERE path.lot_id=h.lot_id
       )
$$;
REVOKE ALL ON FUNCTION ledger.effective_hold_ids(uuid) FROM PUBLIC;

-- Non-HOLD restrictions remain independently testable for authorized HOLD isolation.
CREATE OR REPLACE FUNCTION ledger.mixed_bulk_independent_blocked(l uuid,ignore_correction uuid DEFAULT NULL)
RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT ledger.receiving_restricted(l)
        OR EXISTS (
            SELECT 1 FROM ledger.corrections c JOIN ledger.lots x ON x.operation_id=c.operation_id
            WHERE x.id=l AND c.status IN ('REQUESTED','RECONCILED')
              AND c.id IS DISTINCT FROM ignore_correction
        )
        OR EXISTS (
            SELECT 1 FROM ledger.receipt_correction_blocks r
            JOIN ledger.lots x ON x.receipt_id=r.receipt_id WHERE x.id=l
        )
        OR EXISTS (
            SELECT 1 FROM ledger.receipt_reversals r
            JOIN ledger.lots x ON x.receipt_id=r.receipt_id WHERE x.id=l
        )
$$;
REVOKE ALL ON FUNCTION ledger.mixed_bulk_independent_blocked(uuid,uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.blocked(l uuid,ignore_correction uuid DEFAULT NULL) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT EXISTS(SELECT 1 FROM ledger.effective_hold_ids(l))
        OR ledger.mixed_bulk_independent_blocked(l,ignore_correction)
$$;
REVOKE ALL ON FUNCTION ledger.blocked(uuid,uuid) FROM PUBLIC;

CREATE OR REPLACE FUNCTION ledger.require_mixed_correction_safe(action text,p jsonb) RETURNS void
LANGUAGE plpgsql STABLE SET search_path=pg_catalog,ledger AS $$
DECLARE
    target_operation uuid;
    correction_id uuid;
    replacement_operation uuid;
BEGIN
    IF action='correction_request' THEN
        target_operation:=(p->>'operationId')::uuid;
    ELSIF p ? '_correctionId' THEN
        correction_id:=(p->>'_correctionId')::uuid;
        SELECT operation_id INTO target_operation FROM ledger.corrections WHERE id=correction_id;
    ELSE
        RETURN;
    END IF;
    IF action IN ('correction_reconcile','correction_commit') THEN
        replacement_operation:=NULLIF(p->>'replacementOperationId','')::uuid;
        IF replacement_operation IS NULL AND correction_id IS NOT NULL THEN
            SELECT replacement_operation_id INTO replacement_operation
            FROM ledger.corrections WHERE id=correction_id;
        END IF;
    END IF;

    IF target_operation IS NULL THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM ledger.operations op
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') output(value)
        WHERE op.id=target_operation AND (
            EXISTS (SELECT 1 FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=(output.value->>'id')::uuid)
            OR EXISTS (SELECT 1 FROM ledger.custody_edges edge
                       WHERE edge.input_lot_id=(output.value->>'id')::uuid
                          OR edge.output_lot_id=(output.value->>'id')::uuid)
            OR ledger.mixed_bulk_custody_derived((output.value->>'id')::uuid)
            OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(op.content->'outputs') item(value)
                JOIN ledger.locations loc ON loc.id=(item.value->>'locationId')::uuid
                WHERE loc.mixed_bulk
            )
        )
    ) OR EXISTS (
        SELECT 1
        FROM ledger.operations op
        CROSS JOIN LATERAL jsonb_array_elements(op.content->'inputs') input(value)
        WHERE op.id=target_operation AND (
            EXISTS (SELECT 1 FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=(input.value->>'lotId')::uuid)
            OR EXISTS (SELECT 1 FROM ledger.custody_edges edge
                       WHERE edge.input_lot_id=(input.value->>'lotId')::uuid
                          OR edge.output_lot_id=(input.value->>'lotId')::uuid)
            OR ledger.mixed_bulk_custody_derived((input.value->>'lotId')::uuid)
            OR EXISTS (
                SELECT 1 FROM jsonb_array_elements(op.content->'inputs') item(value)
                JOIN ledger.locations loc ON loc.id=(item.value->>'locationId')::uuid
                WHERE loc.mixed_bulk
            )
        )
    ) OR replacement_operation IS NOT NULL AND (
        EXISTS (
            SELECT 1 FROM ledger.operations op
            CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') output(value)
            WHERE op.id=replacement_operation AND (
                EXISTS (SELECT 1 FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=(output.value->>'id')::uuid)
                OR EXISTS (SELECT 1 FROM ledger.custody_edges edge
                           WHERE edge.input_lot_id=(output.value->>'id')::uuid
                              OR edge.output_lot_id=(output.value->>'id')::uuid)
                OR ledger.mixed_bulk_custody_derived((output.value->>'id')::uuid)
                OR EXISTS (SELECT 1 FROM ledger.locations loc
                           WHERE loc.id=(output.value->>'locationId')::uuid AND loc.mixed_bulk)
            )
        ) OR EXISTS (
            SELECT 1 FROM ledger.operations op
            CROSS JOIN LATERAL jsonb_array_elements(op.content->'inputs') input(value)
            WHERE op.id=replacement_operation AND (
                EXISTS (SELECT 1 FROM ledger.mixed_bulk_pools pool WHERE pool.lot_id=(input.value->>'lotId')::uuid)
                OR EXISTS (SELECT 1 FROM ledger.custody_edges edge
                           WHERE edge.input_lot_id=(input.value->>'lotId')::uuid
                              OR edge.output_lot_id=(input.value->>'lotId')::uuid)
                OR ledger.mixed_bulk_custody_derived((input.value->>'lotId')::uuid)
                OR EXISTS (SELECT 1 FROM ledger.locations loc
                           WHERE loc.id=(input.value->>'locationId')::uuid AND loc.mixed_bulk)
            )
        )
    ) THEN
        RAISE EXCEPTION 'Mixed-bulk custody correction unsupported; downstream custody or changed generation prevents compensation'
            USING ERRCODE='23514';
    END IF;
END $$;
REVOKE ALL ON FUNCTION ledger.require_mixed_correction_safe(text,jsonb) FROM PUBLIC;
