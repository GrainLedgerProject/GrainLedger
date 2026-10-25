-- Retain equipment IDs and content; is_deleted means unavailable for new work.
-- Pending correction originals must remain active until their existing workflow resolves.
CREATE FUNCTION ledger.equipment_has_pending_work(equipment_id uuid) RETURNS boolean
LANGUAGE sql STABLE SET search_path=pg_catalog,ledger AS $$
    SELECT EXISTS(SELECT 1 FROM ledger.operations op
        WHERE op.equipment_id=$1 AND op.status='DRAFT' AND NOT op.is_deleted)
        OR EXISTS(SELECT 1 FROM ledger.corrections c
            WHERE c.status IN ('REQUESTED','RECONCILED') AND (
                EXISTS(SELECT 1 FROM ledger.operations op
                    WHERE op.id IN (c.operation_id,c.replacement_operation_id) AND op.equipment_id=$1)
                OR translate(lower(c.replacement_content->>'equipmentId'),'-{}','')=replace($1::text,'-','')))
$$;
REVOKE ALL ON FUNCTION ledger.equipment_has_pending_work(uuid) FROM PUBLIC;

-- Follow existing protected command/read extension boundaries; old routines stay private.
ALTER FUNCTION ledger.command(uuid,text,jsonb) RENAME TO command_before_equipment_retirement;
ALTER FUNCTION ledger.read(uuid,text,jsonb) RENAME TO read_before_equipment_retirement;
REVOKE ALL ON FUNCTION ledger.command_before_equipment_retirement(uuid,text,jsonb),
    ledger.read_before_equipment_retirement(uuid,text,jsonb) FROM PUBLIC;
DO $$
DECLARE granted record;
BEGIN
    FOR granted IN
        SELECT DISTINCT role.rolname,p.oid::regprocedure routine FROM pg_proc p
        CROSS JOIN LATERAL aclexplode(p.proacl) acl
        JOIN pg_roles role ON role.oid=acl.grantee
        WHERE p.oid IN ('ledger.command_before_equipment_retirement(uuid,text,jsonb)'::regprocedure,
            'ledger.read_before_equipment_retirement(uuid,text,jsonb)'::regprocedure)
            AND acl.privilege_type='EXECUTE' AND acl.grantee<>p.proowner
    LOOP
        EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM %I',granted.routine,granted.rolname);
    END LOOP;
END $$;

CREATE FUNCTION ledger.command(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE selected_equipment ledger.equipment; result jsonb;
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS NULL THEN RAISE EXCEPTION 'Active actor required' USING ERRCODE='42501'; END IF;
    IF p ?| ARRAY['createdBy','created_by'] THEN
        RAISE EXCEPTION 'Creator derives from authenticated actor' USING ERRCODE='22023';
    END IF;
    IF action='equipment_retire' THEN
        IF ledger.role(a)<>'Administrator' THEN
            RAISE EXCEPTION 'Setup authority denied' USING ERRCODE='42501';
        END IF;
        SELECT * INTO STRICT selected_equipment FROM ledger.equipment WHERE id=(p->>'id')::uuid FOR UPDATE;
        IF selected_equipment.created_by IS DISTINCT FROM a THEN
            RAISE EXCEPTION 'Only creator may manage setup record' USING ERRCODE='42501';
        END IF;
        IF selected_equipment.is_deleted THEN
            RAISE EXCEPTION 'Removed setup record cannot be changed or restored' USING ERRCODE='23514';
        END IF;
        IF jsonb_typeof(p->'version') IS DISTINCT FROM 'string' OR NULLIF(p->>'version','') IS NULL THEN
            RAISE EXCEPTION 'Row version string required' USING ERRCODE='22023';
        END IF;
        IF selected_equipment.version IS DISTINCT FROM (p->>'version')::uuid THEN
            RAISE EXCEPTION 'Setup record version stale' USING ERRCODE='23514';
        END IF;
        IF ledger.equipment_has_pending_work(selected_equipment.id) THEN
            RAISE EXCEPTION 'Equipment has active drafts or pending corrections; resolve them before retirement' USING ERRCODE='23514';
        END IF;
        UPDATE ledger.equipment SET is_deleted=true,version=gen_random_uuid() WHERE id=selected_equipment.id;
        RETURN jsonb_build_object('id',selected_equipment.id);
    END IF;
    -- Check before idempotent draft caches too: replay cannot revive retired equipment.
    IF action='draft' AND NOT EXISTS(SELECT 1 FROM ledger.equipment e
        WHERE e.id=(p->>'equipmentId')::uuid AND e.facility_id=(p->>'facilityId')::uuid AND NOT e.is_deleted) THEN
        RAISE EXCEPTION 'Draft equipment outside assigned facility' USING ERRCODE='42501';
    END IF;
    result:=ledger.command_before_equipment_retirement(a,action,p);
    IF action IN ('equipment','equipment_update') THEN
        SELECT * INTO STRICT selected_equipment FROM ledger.equipment WHERE id=(result->>'id')::uuid;
        result:=result||jsonb_build_object('canRetire',coalesce(selected_equipment.created_by=a
            AND ledger.role(a)='Administrator' AND NOT selected_equipment.is_deleted
            AND ledger.record_in_use('equipment',selected_equipment.id)
            AND NOT ledger.equipment_has_pending_work(selected_equipment.id),false));
    END IF;
    RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.command(uuid,text,jsonb) FROM PUBLIC;

CREATE FUNCTION ledger.read(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE result jsonb;
BEGIN
    result:=ledger.read_before_equipment_retirement(a,action,p);
    IF action='setup' THEN
        SELECT jsonb_set(result,'{equipment}',coalesce(jsonb_agg(entry.value||jsonb_build_object(
            'canDelete',coalesce(e.created_by=a AND ledger.role(a)='Administrator'
                AND NOT ledger.record_in_use('equipment',e.id),false),
            'canRetire',coalesce(e.created_by=a AND ledger.role(a)='Administrator'
                AND ledger.record_in_use('equipment',e.id) AND NOT ledger.equipment_has_pending_work(e.id),false))
            ORDER BY entry.ordinal),'[]'::jsonb)) INTO result
        FROM jsonb_array_elements(result->'equipment') WITH ORDINALITY entry(value,ordinal)
        JOIN ledger.equipment e ON e.id=(entry.value->>'id')::uuid;
    ELSIF action='operation' THEN
        SELECT result||jsonb_build_object('equipmentName',e.name||CASE WHEN e.is_deleted THEN ' (retired)' ELSE '' END)
        INTO result FROM ledger.equipment e WHERE e.id=(result->>'equipmentId')::uuid;
    ELSIF action IN ('operations','history') THEN
        -- Decorate only rows already authorized by the scoped reader; never alter stored content or event details.
        SELECT jsonb_set(result,'{items}',coalesce(jsonb_agg(entry.value||CASE WHEN e.id IS NOT NULL THEN
            jsonb_build_object('equipmentName',e.name||CASE WHEN e.is_deleted THEN ' (retired)' ELSE '' END)
            ELSE '{}'::jsonb END ORDER BY entry.ordinal),'[]'::jsonb)) INTO result
        FROM jsonb_array_elements(result->'items') WITH ORDINALITY entry(value,ordinal)
        LEFT JOIN ledger.operations op ON op.id=(CASE WHEN action='operations' THEN entry.value->>'id' ELSE entry.value->>'operationId' END)::uuid
        LEFT JOIN ledger.events ev ON action='history' AND ev.id=(entry.value->>'id')::uuid
        LEFT JOIN ledger.equipment e ON e.id=CASE WHEN ev.kind='VARIANCE_CONFIRMATION' AND op.id IS NOT NULL
            THEN coalesce((ev.details->'reviewedContent'->>'equipmentId')::uuid,op.equipment_id)
            ELSE op.equipment_id END;
    END IF;
    RETURN result;
END $$;
REVOKE ALL ON FUNCTION ledger.read(uuid,text,jsonb) FROM PUBLIC;
