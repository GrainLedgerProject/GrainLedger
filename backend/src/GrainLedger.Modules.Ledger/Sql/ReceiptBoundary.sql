CREATE OR REPLACE FUNCTION ledger.complete_receipt_package() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
BEGIN
    IF TG_TABLE_NAME='lots' THEN
        IF NEW.receipt_id IS NOT NULL THEN
            IF to_regnamespace('capture_mock') IS NULL THEN
                RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
                    USING ERRCODE='23514';
            END IF;
            IF capture_mock.receipt_stock_complete(NEW.receipt_id) IS DISTINCT FROM true THEN
                RAISE EXCEPTION 'Receipt, source lot, event and credit must complete atomically'
                    USING ERRCODE='23514';
            END IF;
        END IF;
    ELSE
        IF to_regnamespace('capture_mock') IS NULL THEN
            RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
                USING ERRCODE='23514';
        END IF;
        IF capture_mock.receipt_correction_complete(NEW.correction_id) IS DISTINCT FROM true THEN
            RAISE EXCEPTION 'Receipt compensation and replacement must complete atomically'
                USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS receipt_package ON ledger.lots;
CREATE CONSTRAINT TRIGGER receipt_package
AFTER INSERT ON ledger.lots DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION ledger.complete_receipt_package();
DROP TRIGGER IF EXISTS receipt_correction_package ON ledger.receipt_reversals;
CREATE CONSTRAINT TRIGGER receipt_correction_package
AFTER INSERT ON ledger.receipt_reversals DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION ledger.complete_receipt_package();

CREATE OR REPLACE FUNCTION ledger.complete_receiving_resolution() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,ledger AS $$
DECLARE
    final_state ledger.receiving_blocks;
    package_complete boolean;
BEGIN
    IF to_regnamespace('capture_mock') IS NULL THEN
        RAISE EXCEPTION 'Synthetic receipt and receiving operations require the capture_mock schema'
            USING ERRCODE='23514';
    END IF;
    -- Validate final row, not an earlier transition queued in the same transaction.
    SELECT * INTO STRICT final_state FROM ledger.receiving_blocks
    WHERE discrepancy_id=NEW.discrepancy_id;
    IF final_state.blocked THEN
        package_complete:=capture_mock.receiving_block_complete(
            final_state.discrepancy_id,final_state.lot_id,final_state.evidence_id,final_state.reason
        );
    ELSE
        package_complete:=capture_mock.receiving_clear_complete(
            final_state.discrepancy_id,final_state.lot_id,final_state.evidence_id,final_state.reason
        );
    END IF;
    IF package_complete IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'Receiving restriction requires completed matching observation/correction package'
            USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS receiving_resolution_package ON ledger.receiving_blocks;
CREATE CONSTRAINT TRIGGER receiving_resolution_package
AFTER INSERT OR UPDATE ON ledger.receiving_blocks
DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
EXECUTE FUNCTION ledger.complete_receiving_resolution();
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
