-- Existing drafts keep their manually assigned codes. New drafts opt into reservations.
ALTER TABLE ledger.operations ADD COLUMN lot_code_scheme text NOT NULL DEFAULT 'LEGACY'
    CHECK (lot_code_scheme IN ('LEGACY','GENERATED'));

ALTER FUNCTION ledger.command(uuid,text,jsonb) RENAME TO command_legacy;
REVOKE ALL ON FUNCTION ledger.command_legacy(uuid,text,jsonb) FROM PUBLIC;
DO $$
DECLARE granted record;
BEGIN
    FOR granted IN
        SELECT DISTINCT role.rolname FROM pg_proc p
        CROSS JOIN LATERAL aclexplode(p.proacl) acl
        JOIN pg_roles role ON role.oid=acl.grantee
        WHERE p.oid='ledger.command_legacy(uuid,text,jsonb)'::regprocedure
          AND acl.privilege_type='EXECUTE' AND acl.grantee<>p.proowner
    LOOP
        EXECUTE format('REVOKE EXECUTE ON FUNCTION ledger.command_legacy(uuid,text,jsonb) FROM %I',granted.rolname);
    END LOOP;
END $$;

CREATE FUNCTION ledger.command(a uuid,action text,p jsonb) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity AS $$
DECLARE prior ledger.operations; saved ledger.operations; result jsonb; item jsonb;
    outputs jsonb:='[]'::jsonb; assigned_code text; reservation ledger.lot_code_reservations;
    prior_outputs jsonb; requested_outputs jsonb; original_lot ledger.lots;
BEGIN
    IF action<>'draft' THEN RETURN ledger.command_legacy(a,action,p); END IF;
    PERFORM ledger.lock_writes();
    SELECT * INTO prior FROM ledger.operations WHERE id=(p->>'id')::uuid;
    IF FOUND AND prior.lot_code_scheme='LEGACY' THEN
        RETURN ledger.command_legacy(a,action,p);
    END IF;
    IF FOUND AND prior.lot_code_scheme='GENERATED' AND NOT p ? 'version' THEN
        SELECT jsonb_agg(value-'code' ORDER BY ordinal) INTO prior_outputs
        FROM jsonb_array_elements(prior.content->'outputs') WITH ORDINALITY AS x(value,ordinal);
        SELECT jsonb_agg(value-'code' ORDER BY ordinal) INTO requested_outputs
        FROM jsonb_array_elements(p->'outputs') WITH ORDINALITY AS x(value,ordinal);
        IF prior.status='DRAFT' AND prior.created_by=a
           AND jsonb_set(prior.content,'{outputs}',prior_outputs)
               =jsonb_set(p,'{outputs}',requested_outputs) THEN
            PERFORM ledger.require_staff(a,prior.facility_id,ARRAY['MillOperator']);
            RETURN ledger.operation_json(prior.id)||jsonb_build_object('createdBy',a,'canEdit',true,
                'canDelete',NOT ledger.record_in_use('operations',prior.id));
        END IF;
    END IF;
    -- The legacy command performs persisted role, owner, input, facility and draft-version checks.
    result:=ledger.command_legacy(a,action,p);
    SELECT * INTO STRICT saved FROM ledger.operations WHERE id=(p->>'id')::uuid FOR UPDATE;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(saved.content->'outputs') x
        GROUP BY (x->>'id')::uuid HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Duplicate output lot identifier' USING ERRCODE='23514';
    END IF;
    IF EXISTS(SELECT 1 FROM jsonb_array_elements(saved.content->'outputs') x
        WHERE x ? 'originalLotId' AND x->>'originalLotId' IS NOT NULL
        GROUP BY (x->>'originalLotId')::uuid HAVING count(*)>1) THEN
        RAISE EXCEPTION 'Original output cannot be replaced twice in one draft' USING ERRCODE='23514';
    END IF;
    FOR item IN SELECT value FROM jsonb_array_elements(saved.content->'outputs') LOOP
        IF nullif(item->>'originalLotId','') IS NOT NULL THEN
            SELECT * INTO STRICT original_lot FROM ledger.lots WHERE id=(item->>'originalLotId')::uuid;
            IF original_lot.owner_id<>saved.owner_id OR original_lot.stage IS DISTINCT FROM item->>'stage'
               OR original_lot.product_identity IS DISTINCT FROM item->>'productIdentity'
               OR original_lot.operation_id IS NULL OR NOT EXISTS(
                   SELECT 1 FROM ledger.operations old
                   JOIN ledger.corrections c ON c.operation_id=old.id
                   WHERE old.id=original_lot.operation_id AND old.owner_id=saved.owner_id
                     AND old.facility_id=saved.facility_id AND old.kind=saved.kind
                     AND c.status IN ('REQUESTED','RECONCILED')
               ) THEN
                RAISE EXCEPTION 'Original output mapping is outside a matching correction' USING ERRCODE='23514';
            END IF;
            IF nullif(item->>'code','') IS NOT NULL AND item->>'code'<>original_lot.code THEN
                RAISE EXCEPTION 'Replacement code must match mapped original output' USING ERRCODE='23514';
            END IF;
            outputs:=outputs||jsonb_build_array(item||jsonb_build_object('code',original_lot.code));
            CONTINUE;
        END IF;
        SELECT * INTO reservation FROM ledger.lot_code_reservations WHERE lot_id=(item->>'id')::uuid;
        IF FOUND AND item->>'code' IS NOT NULL AND item->>'code'<>reservation.code THEN
            RAISE EXCEPTION 'Output code does not match the reserved code' USING ERRCODE='23514';
        END IF;
        IF NOT FOUND AND nullif(item->>'code','') IS NOT NULL THEN
            RAISE EXCEPTION 'New output code is assigned by the server' USING ERRCODE='23514';
        END IF;
        assigned_code:=ledger.reserve_lot_code_internal((item->>'id')::uuid,saved.owner_id,
            'OPERATION',saved.id,item->>'stage',item->>'productIdentity');
        outputs:=outputs||jsonb_build_array(item||jsonb_build_object('code',assigned_code));
    END LOOP;
    UPDATE ledger.operations SET content=jsonb_set(content,'{outputs}',outputs),lot_code_scheme='GENERATED'
    WHERE id=saved.id;
    RETURN result||jsonb_build_object('outputs',outputs);
END $$;
REVOKE ALL ON FUNCTION ledger.command(uuid,text,jsonb) FROM PUBLIC;
