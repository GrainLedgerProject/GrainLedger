-- Forward-only code reservations. A source or draft may have a code before stock exists.
CREATE SEQUENCE ledger.lot_code_serial AS bigint;
REVOKE ALL ON SEQUENCE ledger.lot_code_serial FROM PUBLIC;

CREATE TABLE ledger.lot_code_reservations (
    lot_id uuid PRIMARY KEY,
    code text NOT NULL UNIQUE,
    owner_id uuid NOT NULL REFERENCES identity.organizations("Id"),
    context_kind text NOT NULL CHECK (context_kind IN ('SOURCE','OPERATION')),
    context_id uuid NOT NULL,
    stage text NOT NULL,
    product_identity text NOT NULL,
    issued_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX lot_code_reservations_context ON ledger.lot_code_reservations(context_kind,context_id);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.lot_code_reservations
FOR EACH ROW EXECUTE FUNCTION ledger.immutable();
REVOKE ALL ON TABLE ledger.lot_code_reservations FROM PUBLIC;

CREATE FUNCTION ledger.lot_code_token(v_stage text,v_product text) RETURNS text
LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,ledger AS $$
    SELECT CASE
        WHEN v_stage='RAW_PADDY' AND v_product='RAW_PADDY' THEN 'RAW'
        WHEN v_stage='DRIED_PADDY' AND v_product='DRIED_PADDY' THEN 'DRY'
        WHEN v_stage='HEAD_RICE' AND v_product='HEAD_RICE' THEN 'HDR'
        WHEN v_stage='BROKEN_RICE' AND v_product='BROKEN_RICE' THEN 'BRK'
        WHEN v_stage='BRAN' AND v_product='BRAN' THEN 'BRN'
        WHEN v_stage='HUSK' AND v_product='HUSK' THEN 'HSK'
        WHEN v_stage='BLENDED_RICE' AND v_product='BLENDED_RICE' THEN 'BLD'
        WHEN v_stage='BAGGED_RICE' AND v_product='HEAD_RICE' THEN 'BGH'
        WHEN v_stage='BAGGED_RICE' AND v_product='BROKEN_RICE' THEN 'BGB'
        WHEN v_stage='BAGGED_RICE' AND v_product='BLENDED_RICE' THEN 'BGL'
    END
$$;

CREATE FUNCTION ledger.reserve_lot_code_internal(v_lot uuid,v_owner uuid,v_kind text,v_context uuid,
    v_stage text,v_product text) RETURNS text
LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE old ledger.lot_code_reservations; token text; candidate text; serial bigint;
BEGIN
    PERFORM ledger.lock_writes();
    IF v_lot IS NULL OR v_lot='00000000-0000-0000-0000-000000000000' OR v_owner IS NULL
       OR v_context IS NULL OR v_kind NOT IN ('SOURCE','OPERATION') THEN
        RAISE EXCEPTION 'Lot code context is incomplete' USING ERRCODE='22023';
    END IF;
    token:=ledger.lot_code_token(v_stage,v_product);
    IF token IS NULL THEN RAISE EXCEPTION 'Unsupported lot stage and product identity' USING ERRCODE='23514'; END IF;
    SELECT * INTO old FROM ledger.lot_code_reservations WHERE lot_id=v_lot;
    IF FOUND THEN
        IF old.owner_id<>v_owner OR old.context_kind<>v_kind OR old.context_id<>v_context
           OR old.stage<>v_stage OR old.product_identity<>v_product THEN
            RAISE EXCEPTION 'Lot identifier already reserved for different provenance or product' USING ERRCODE='23514';
        END IF;
        RETURN old.code;
    END IF;
    IF EXISTS(SELECT 1 FROM ledger.lots WHERE id=v_lot) THEN
        RAISE EXCEPTION 'Lot identifier already has stock history' USING ERRCODE='23514';
    END IF;
    LOOP
        serial:=nextval('ledger.lot_code_serial');
        candidate:=token||'-'||to_char(clock_timestamp() AT TIME ZONE 'Asia/Ho_Chi_Minh','YYYYMMDD')||'-'
            ||lpad(serial::text,greatest(6,length(serial::text)),'0');
        EXIT WHEN NOT EXISTS(SELECT 1 FROM ledger.lot_codes WHERE code=candidate)
              AND NOT EXISTS(SELECT 1 FROM ledger.lot_code_reservations WHERE code=candidate);
    END LOOP;
    INSERT INTO ledger.lot_code_reservations(lot_id,code,owner_id,context_kind,context_id,stage,product_identity)
    VALUES(v_lot,candidate,v_owner,v_kind,v_context,v_stage,v_product);
    RETURN candidate;
END $$;

CREATE FUNCTION ledger.reserve_source_lot_code(a uuid,o uuid,r uuid,v_lot uuid) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger,identity,capture AS $$
BEGIN
    PERFORM ledger.lock_writes();
    IF ledger.role(a) IS DISTINCT FROM 'PurchasingStaff' OR NOT ledger.own(a,o)
       OR NOT EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=o AND "Kind"='Customer')
       OR EXISTS(SELECT 1 FROM capture.procurements WHERE id=r)
       OR EXISTS(SELECT 1 FROM capture.source_lots WHERE lot_id=v_lot)
    THEN RAISE EXCEPTION 'Source lot code authority or identifier unavailable' USING ERRCODE='42501'; END IF;
    RETURN ledger.reserve_lot_code_internal(v_lot,o,'SOURCE',r,'RAW_PADDY','RAW_PADDY');
END $$;

CREATE FUNCTION ledger.source_lot_code(r uuid,v_lot uuid) RETURNS text
LANGUAGE plpgsql STABLE SET search_path=pg_catalog,ledger,capture AS $$
DECLARE reserved text; version integer;
BEGIN
    SELECT code INTO reserved FROM ledger.lot_code_reservations
    WHERE lot_id=v_lot AND context_kind='SOURCE' AND context_id=r
      AND stage='RAW_PADDY' AND product_identity='RAW_PADDY';
    IF reserved IS NOT NULL THEN RETURN reserved; END IF;
    SELECT COALESCE((ev.payload->>'schemaVersion')::integer,1) INTO version
    FROM capture.procurements p JOIN capture.business_events ev ON ev.id=p.event_id WHERE p.id=r;
    IF version>=2 THEN RAISE EXCEPTION 'New source lot is missing its reserved code' USING ERRCODE='23514'; END IF;
    RETURN upper('LL-'||replace(v_lot::text,'-',''));
END $$;

CREATE OR REPLACE FUNCTION ledger.register_code(
    v_code text,new_lot uuid,original_receipt uuid DEFAULT NULL,original_operation uuid DEFAULT NULL
) RETURNS uuid LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE origin uuid; reserved ledger.lot_code_reservations;
BEGIN
    SELECT * INTO reserved FROM ledger.lot_code_reservations WHERE code=v_code;
    IF FOUND AND reserved.lot_id<>new_lot THEN
        IF reserved.context_kind<>'OPERATION' OR original_operation IS NULL OR NOT EXISTS(
            SELECT 1 FROM ledger.lots old
            JOIN ledger.corrections c ON c.operation_id=old.operation_id
            JOIN ledger.operations replacement ON replacement.id=c.replacement_operation_id,
                jsonb_array_elements(replacement.content->'outputs') output
            WHERE old.id=reserved.lot_id AND old.code=v_code AND old.operation_id=original_operation
              AND c.status='RECONCILED' AND (output->>'id')::uuid=new_lot
              AND (output->>'originalLotId')::uuid=old.id
              AND output->>'stage'=reserved.stage AND output->>'productIdentity'=reserved.product_identity
        ) THEN
            RAISE EXCEPTION 'Lot code is reserved for another lineage or product' USING ERRCODE='23505';
        END IF;
    END IF;
    SELECT origin_lot_id INTO origin FROM ledger.lot_codes WHERE code=v_code;
    IF origin IS NULL THEN
        INSERT INTO ledger.lot_codes VALUES(v_code,new_lot);
        RETURN new_lot;
    END IF;
    IF EXISTS (
        SELECT 1 FROM ledger.lots old
        WHERE old.code=v_code AND old.code_origin_id=origin AND (
            original_receipt IS NOT NULL AND old.receipt_id=original_receipt
            OR original_operation IS NOT NULL AND old.operation_id=original_operation
        )
    ) THEN RETURN origin; END IF;
    RAISE EXCEPTION 'Lot code belongs to unrelated provenance; only correction lineage may reuse it'
        USING ERRCODE='23505';
END $$;

REVOKE ALL ON FUNCTION ledger.lot_code_token(text,text),
    ledger.reserve_lot_code_internal(uuid,uuid,text,uuid,text,text),
    ledger.reserve_source_lot_code(uuid,uuid,uuid,uuid),ledger.source_lot_code(uuid,uuid) FROM PUBLIC;
