-- Human codes may recur only along one explicit immutable correction lineage.
CREATE TABLE ledger.lot_codes (
    code text PRIMARY KEY,
    origin_lot_id uuid NOT NULL REFERENCES ledger.lots(id) DEFERRABLE INITIALLY DEFERRED,
    UNIQUE(code,origin_lot_id)
);
ALTER TABLE ledger.lots ADD CONSTRAINT lot_code_lineage
    FOREIGN KEY(code,code_origin_id) REFERENCES ledger.lot_codes(code,origin_lot_id);
CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON ledger.lot_codes
FOR EACH ROW EXECUTE FUNCTION ledger.immutable();

CREATE FUNCTION ledger.register_code(
    v_code text,new_lot uuid,original_receipt uuid DEFAULT NULL,original_operation uuid DEFAULT NULL
) RETURNS uuid LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE
    origin uuid;
BEGIN
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
    ) THEN
        RETURN origin;
    END IF;
    RAISE EXCEPTION 'Lot code belongs to unrelated provenance; only correction lineage may reuse it'
        USING ERRCODE='23505';
END $$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
