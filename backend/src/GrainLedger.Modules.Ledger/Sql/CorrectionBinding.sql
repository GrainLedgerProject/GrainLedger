ALTER TABLE ledger.corrections ADD COLUMN replacement_version uuid;
ALTER TABLE ledger.corrections ADD COLUMN replacement_content jsonb;
-- The finite command function owns all correction validation; manager review binds replacement below.
CREATE FUNCTION ledger.review_replacement(
    a uuid,corr uuid,replacement uuid,reviewed uuid,physical_positions jsonb
) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog,ledger AS $$
DECLARE
    original ledger.operations;
    replacement_draft ledger.operations;
    output jsonb;
    output_facility uuid;
BEGIN
    SELECT op.* INTO STRICT original
    FROM ledger.corrections c JOIN ledger.operations op ON op.id=c.operation_id
    WHERE c.id=corr;
    SELECT * INTO STRICT replacement_draft FROM ledger.operations
    WHERE id=replacement FOR UPDATE;
    PERFORM ledger.require_staff(a,original.facility_id,ARRAY['MillManager']);

    -- Physical evidence reviews exact replacement content, never an inferred latest version.
    IF replacement_draft.status<>'DRAFT'
       OR replacement_draft.owner_id<>original.owner_id
       OR replacement_draft.facility_id<>original.facility_id
       OR replacement_draft.version IS DISTINCT FROM reviewed THEN
        RAISE EXCEPTION 'Replacement review stale or outside original owner/facility'
            USING ERRCODE='23514';
    END IF;
    FOR output IN SELECT value FROM jsonb_array_elements(replacement_draft.content->'outputs') LOOP
        IF NOT EXISTS (
            SELECT 1 FROM jsonb_array_elements(physical_positions) pos
            WHERE pos->>'locationId'=output->>'locationId'
        ) THEN
            RAISE EXCEPTION 'Replacement must retain reconciled current physical locations'
                USING ERRCODE='23514';
        END IF;
        SELECT facility_id INTO STRICT output_facility FROM ledger.locations
        WHERE id=(output->>'locationId')::uuid;
        PERFORM ledger.require_staff(a,output_facility,ARRAY['MillManager']);
    END LOOP;
    UPDATE ledger.corrections
    SET replacement_operation_id=replacement_draft.id,
        replacement_version=replacement_draft.version,
        replacement_content=replacement_draft.content
    WHERE id=corr;
END $$;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM PUBLIC;
