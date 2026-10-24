-- F12 reads physical ancestry, never accounting root allocations.
CREATE TABLE ledger.residue_evaluations (
    id uuid PRIMARY KEY, lot_id uuid NOT NULL REFERENCES ledger.lots,
    actor_id uuid NOT NULL REFERENCES identity.users("Id"), request jsonb NOT NULL,
    snapshot jsonb NOT NULL, persisted_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX residue_evaluations_lot ON ledger.residue_evaluations(lot_id,persisted_at DESC);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.residue_evaluations
    FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
CREATE TRIGGER immutable_truncate BEFORE TRUNCATE ON ledger.residue_evaluations
    FOR EACH STATEMENT EXECUTE FUNCTION ledger.immutable();
REVOKE ALL ON ledger.residue_evaluations FROM PUBLIC;

CREATE FUNCTION ledger.residue_authorized(a uuid,l uuid) RETURNS boolean
LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
    SELECT COALESCE(ledger.role(a) IN ('CustomerManager','ExportStaff','MillManager','MillOperator')
        AND ledger.can_read_lot(a,l),false)
$$;
REVOKE ALL ON FUNCTION ledger.residue_authorized(uuid,uuid) FROM PUBLIC;

CREATE FUNCTION ledger.residue_graph(a uuid,target uuid) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE result jsonb;
BEGIN
    IF NOT ledger.residue_authorized(a,target) THEN
        RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501';
    END IF;
    WITH RECURSIVE closure(id) AS (
        SELECT target
        UNION
        SELECT parent.id FROM closure c JOIN ledger.lots l ON l.id=c.id
        CROSS JOIN LATERAL (
            SELECT edge.input_lot_id id FROM ledger.custody_edges edge WHERE edge.output_lot_id=l.id
            UNION
            SELECT (input->>'lotId')::uuid FROM ledger.events e
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(e.details->'resolvedInputs',e.details->'content'->'inputs','[]'::jsonb)) input
            WHERE e.operation_id=l.operation_id AND e.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')
        ) parent
    )
    SELECT jsonb_agg(jsonb_build_object(
        'lotId',l.id,'companyId',l.owner_id,'commodityId',COALESCE(mapping.catalog_commodity_id,l.commodity_id),'stage',l.stage,
        'productIdentity',l.product_identity,'at',COALESCE(o.occurred_at,custody.at,l.created_at),
        'kind',COALESCE('CUSTODY_'||custody.action,o.kind,'SOURCE'),
        'operationId',l.operation_id,'eventId',COALESCE(custody.event_id,physical.id),
        'conditions',physical.details->'content'->>'residueConditions',
        'inconsistent',COALESCE(l.owner_id<>(SELECT owner_id FROM ledger.lots WHERE id=target)
            OR l.operation_id IS NOT NULL AND (physical.id IS NULL OR physical.event_count<>1 OR o.status NOT IN ('COMMITTED','CORRECTED'))
            OR custody.event_count<>1 OR custody.event_id IS NOT NULL AND l.operation_id IS NOT NULL
            OR l.id<>target AND o.status='CORRECTED',false),
        'parents',COALESCE(custody.parents,physical.parents,'[]'::jsonb)
    ) ORDER BY l.id) INTO result
    FROM closure c JOIN ledger.lots l ON l.id=c.id
    LEFT JOIN ledger.operations o ON o.id=l.operation_id
    LEFT JOIN ledger.catalog_commodities mapping ON mapping.ledger_commodity_id=l.commodity_id
    LEFT JOIN LATERAL (
        SELECT e.id,e.details,count(*) OVER () event_count,
            jsonb_agg(jsonb_build_object('lotId',input->>'lotId','quantityKg',input->>'quantityKg')) parents
        FROM ledger.events e CROSS JOIN LATERAL jsonb_array_elements(COALESCE(e.details->'resolvedInputs',e.details->'content'->'inputs','[]'::jsonb)) input
        WHERE e.operation_id=l.operation_id AND e.kind IN ('DRYING','MILLING','MERGE','BLENDING','BAGGING')
        GROUP BY e.id,e.details ORDER BY e.id LIMIT 1
    ) physical ON true
    LEFT JOIN LATERAL (
        SELECT ce.event_id,ce.action,ce.created_at at,count(*) OVER () event_count,
            jsonb_agg(jsonb_build_object('lotId',edge.input_lot_id,'quantityKg',edge.quantity_kg::text)) parents
        FROM ledger.custody_edges edge JOIN ledger.custody_events ce ON ce.event_id=edge.event_id
        WHERE edge.output_lot_id=l.id GROUP BY ce.event_id,ce.action,ce.created_at
        ORDER BY ce.event_id LIMIT 1
    ) custody ON true;
    RETURN COALESCE(result,'[]'::jsonb);
END $$;
REVOKE ALL ON FUNCTION ledger.residue_graph(uuid,uuid) FROM PUBLIC;

CREATE FUNCTION ledger.residue_history(a uuid,l uuid,evaluation uuid DEFAULT NULL) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE result jsonb;
BEGIN
    IF NOT ledger.residue_authorized(a,l) THEN
        RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501';
    END IF;
    IF evaluation IS NOT NULL THEN
        SELECT snapshot INTO result FROM ledger.residue_evaluations WHERE id=evaluation AND lot_id=l;
        IF result IS NULL THEN RAISE EXCEPTION 'Residue evaluation not found' USING ERRCODE='P0002'; END IF;
        RETURN result;
    END IF;
    SELECT jsonb_build_object('items',COALESCE(jsonb_agg(snapshot ORDER BY persisted_at DESC,id),'[]'::jsonb)) INTO result
    FROM ledger.residue_evaluations WHERE lot_id=l;
    RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.residue_history(uuid,uuid,uuid) FROM PUBLIC;

CREATE FUNCTION ledger.residue_replay(a uuid,l uuid,evaluation uuid,p jsonb) RETURNS jsonb
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE old ledger.residue_evaluations;
BEGIN
    IF NOT ledger.residue_authorized(a,l) THEN RAISE EXCEPTION 'Residue lot access denied' USING ERRCODE='42501'; END IF;
    SELECT * INTO old FROM ledger.residue_evaluations WHERE id=evaluation;
    IF NOT FOUND THEN RETURN NULL; END IF;
    IF old.actor_id<>a OR old.lot_id<>l OR old.request<>p THEN
        RAISE EXCEPTION 'Residue evaluation ID reused with different actor or payload' USING ERRCODE='23505';
    END IF;
    RETURN old.snapshot;
END $$;
REVOKE ALL ON FUNCTION ledger.residue_replay(uuid,uuid,uuid,jsonb) FROM PUBLIC;

CREATE FUNCTION ledger.residue_append(a uuid,l uuid,evaluation uuid,p jsonb,s jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE old jsonb; persisted timestamptz := clock_timestamp();
BEGIN
    IF current_setting('transaction_isolation')<>'read committed' THEN
        RAISE EXCEPTION 'Residue persistence requires READ COMMITTED isolation' USING ERRCODE='23514';
    END IF;
    -- Authorization mutations use this same lock; recheck their committed state after waiting.
    PERFORM ledger.lock_writes();
    old:=ledger.residue_replay(a,l,evaluation,p);
    IF old IS NOT NULL THEN RETURN old; END IF;
    persisted:=clock_timestamp();
    IF s->>'id' IS DISTINCT FROM evaluation::text OR s->>'lotId' IS DISTINCT FROM l::text
       OR s->>'companyId' IS DISTINCT FROM (SELECT owner_id::text FROM ledger.lots WHERE id=l) THEN
        RAISE EXCEPTION 'Residue snapshot identity mismatch' USING ERRCODE='23514';
    END IF;
    s:=s||jsonb_build_object('persistedAt',persisted);
    INSERT INTO ledger.residue_evaluations(id,lot_id,actor_id,request,snapshot,persisted_at)
    VALUES(evaluation,l,a,p,s,persisted);
    RETURN s;
END $$;
REVOKE ALL ON FUNCTION ledger.residue_append(uuid,uuid,uuid,jsonb,jsonb) FROM PUBLIC;
