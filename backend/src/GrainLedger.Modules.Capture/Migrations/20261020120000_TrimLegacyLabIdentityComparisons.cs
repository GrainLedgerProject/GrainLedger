using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261020120000_TrimLegacyLabIdentityComparisons")]
public sealed class TrimLegacyLabIdentityComparisons : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION capture.guard_lab_association() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            DECLARE s capture.lab_result_sets%ROWTYPE; p capture.samples%ROWTYPE; lab text;
                ws CONSTANT text := chr(9) || chr(10) || chr(11) || chr(12) || chr(13) || ' ' || chr(133) || chr(160) || chr(5760)
                    || chr(8192) || chr(8193) || chr(8194) || chr(8195) || chr(8196) || chr(8197) || chr(8198) || chr(8199)
                    || chr(8200) || chr(8201) || chr(8202) || chr(8232) || chr(8233) || chr(8239) || chr(8287) || chr(12288);
            BEGIN
                SELECT rs.* INTO s FROM capture.lab_result_sets rs JOIN capture.lab_report_confirmations c ON c.company_id = rs.company_id AND c.report_id = rs.report_id
                    WHERE rs.company_id = NEW.company_id AND rs.id = NEW.result_set_id AND c.id = NEW.confirmation_id AND c.creation_transaction_id = pg_current_xact_id();
                IF NOT FOUND THEN RAISE EXCEPTION 'Invalid laboratory confirmation association.' USING ERRCODE = '23514'; END IF;
                SELECT * INTO p FROM capture.samples WHERE company_id = NEW.company_id AND id = NEW.sample_id;
                IF NOT FOUND OR (nullif(btrim(s.stated_sample_code, ws),'') IS NOT NULL AND btrim(s.stated_sample_code, ws) <> btrim(p.code, ws))
                    OR (nullif(btrim(s.stated_seal_code, ws),'') IS NOT NULL AND btrim(s.stated_seal_code, ws) <> btrim(p.seal_code, ws)) THEN
                    RAISE EXCEPTION 'Laboratory sample identity is unresolved.' USING ERRCODE = '23514';
                END IF;
                SELECT laboratory_name INTO lab FROM capture.lab_reports WHERE company_id = NEW.company_id AND id = s.report_id;
                IF (NEW.identity_basis = 'SAMPLE_CODE' AND nullif(btrim(s.stated_sample_code, ws),'') IS NOT NULL AND btrim(s.stated_sample_code, ws) = btrim(p.code, ws))
                    OR (NEW.identity_basis = 'SEAL_CODE' AND nullif(btrim(s.stated_seal_code, ws),'') IS NOT NULL AND btrim(s.stated_seal_code, ws) = btrim(p.seal_code, ws))
                    OR (NEW.identity_basis = 'LAB_REFERENCE' AND nullif(btrim(NEW.evidence_ref, ws),'') IS NOT NULL AND EXISTS (
                        SELECT 1 FROM capture.sample_handovers h WHERE h.company_id = NEW.company_id AND h.sample_id = NEW.sample_id
                        AND h.handover_kind IN ('LAB_DISPATCH','LAB_ACKNOWLEDGEMENT') AND h.laboratory_name = lab
                        AND btrim(h.laboratory_sample_reference, ws) = btrim(s.laboratory_sample_reference, ws)
                        AND btrim(h.evidence_ref, ws) = btrim(NEW.evidence_ref, ws))) THEN
                    RETURN NEW;
                END IF;
                RAISE EXCEPTION 'Laboratory sample identity is unresolved.' USING ERRCODE = '23514';
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION capture.guard_lab_association() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            DECLARE s capture.lab_result_sets%ROWTYPE; p capture.samples%ROWTYPE; lab text;
            BEGIN
                SELECT rs.* INTO s FROM capture.lab_result_sets rs JOIN capture.lab_report_confirmations c ON c.company_id = rs.company_id AND c.report_id = rs.report_id
                    WHERE rs.company_id = NEW.company_id AND rs.id = NEW.result_set_id AND c.id = NEW.confirmation_id AND c.creation_transaction_id = pg_current_xact_id();
                IF NOT FOUND THEN RAISE EXCEPTION 'Invalid laboratory confirmation association.' USING ERRCODE = '23514'; END IF;
                SELECT * INTO p FROM capture.samples WHERE company_id = NEW.company_id AND id = NEW.sample_id;
                IF NOT FOUND OR (nullif(btrim(s.stated_sample_code),'') IS NOT NULL AND s.stated_sample_code <> p.code)
                    OR (nullif(btrim(s.stated_seal_code),'') IS NOT NULL AND s.stated_seal_code <> p.seal_code) THEN
                    RAISE EXCEPTION 'Laboratory sample identity is unresolved.' USING ERRCODE = '23514';
                END IF;
                SELECT laboratory_name INTO lab FROM capture.lab_reports WHERE company_id = NEW.company_id AND id = s.report_id;
                IF (NEW.identity_basis = 'SAMPLE_CODE' AND s.stated_sample_code = p.code)
                    OR (NEW.identity_basis = 'SEAL_CODE' AND s.stated_seal_code = p.seal_code)
                    OR (NEW.identity_basis = 'LAB_REFERENCE' AND nullif(btrim(NEW.evidence_ref),'') IS NOT NULL AND EXISTS (
                        SELECT 1 FROM capture.sample_handovers h WHERE h.company_id = NEW.company_id AND h.sample_id = NEW.sample_id
                        AND h.handover_kind IN ('LAB_DISPATCH','LAB_ACKNOWLEDGEMENT') AND h.laboratory_name = lab
                        AND h.laboratory_sample_reference = s.laboratory_sample_reference AND h.evidence_ref = NEW.evidence_ref)) THEN
                    RETURN NEW;
                END IF;
                RAISE EXCEPTION 'Laboratory sample identity is unresolved.' USING ERRCODE = '23514';
            END $$;
            """);
    }
}
