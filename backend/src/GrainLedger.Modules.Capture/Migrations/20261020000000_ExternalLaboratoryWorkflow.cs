using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261020000000_ExternalLaboratoryWorkflow")]
public sealed class ExternalLaboratoryWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE capture.samples ADD COLUMN material_description varchar(200);
            ALTER TABLE capture.sample_handovers
                ADD COLUMN handover_kind varchar(32) NOT NULL DEFAULT 'HANDOVER',
                ADD COLUMN laboratory_name varchar(200),
                ADD COLUMN laboratory_reference varchar(200),
                ADD COLUMN laboratory_sample_reference varchar(200),
                ADD CONSTRAINT ck_sample_handovers_kind CHECK (handover_kind IN ('HANDOVER','LAB_DISPATCH','LAB_ACKNOWLEDGEMENT')),
                ADD CONSTRAINT ck_sample_handovers_lab CHECK (handover_kind = 'HANDOVER' OR (laboratory_name IS NOT NULL AND length(btrim(laboratory_name)) > 0));

            CREATE TABLE capture.lab_reports (
                id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
                laboratory_name varchar(200) NOT NULL, report_number varchar(200) NOT NULL,
                issued_on date NOT NULL, received_at timestamptz NOT NULL, report_kind varchar(32) NOT NULL,
                related_report_id uuid, relationship_reason text, laboratory_conclusion text, stated_standard text,
                file_name varchar(255) NOT NULL, content_type varchar(200) NOT NULL, file_size integer NOT NULL,
                sha256 varchar(64) NOT NULL, content bytea NOT NULL, company_only_confirmed boolean NOT NULL,
                creation_transaction_id xid8 NOT NULL DEFAULT pg_current_xact_id(),
                recorded_by uuid NOT NULL REFERENCES identity.users("Id"), captured_at timestamptz NOT NULL, recorded_at timestamptz NOT NULL,
                CONSTRAINT ux_lab_reports_company_id UNIQUE(company_id,id),
                CONSTRAINT ck_lab_reports_kind CHECK (report_kind IN ('PARTIAL','FINAL','SUPPLEMENTARY','AMENDMENT','RETEST')),
                CONSTRAINT ck_lab_reports_company_only CHECK (company_only_confirmed),
                CONSTRAINT ck_lab_reports_file CHECK (file_size > 0 AND file_size <= 10485760 AND octet_length(content) = file_size),
                CONSTRAINT ck_lab_reports_related CHECK ((related_report_id IS NULL AND report_kind NOT IN ('SUPPLEMENTARY','AMENDMENT','RETEST')) OR
                    (related_report_id IS NOT NULL AND related_report_id <> id AND relationship_reason IS NOT NULL AND length(btrim(relationship_reason)) > 0)),
                CONSTRAINT fk_lab_reports_related FOREIGN KEY(company_id,related_report_id) REFERENCES capture.lab_reports(company_id,id),
                CONSTRAINT ck_lab_reports_times CHECK (received_at <= captured_at),
                CONSTRAINT ck_lab_reports_labels CHECK (length(btrim(laboratory_name)) > 0 AND length(btrim(report_number)) > 0)
            );
            CREATE INDEX "IX_lab_reports_company_id_recorded_at_id" ON capture.lab_reports(company_id,recorded_at,id);
            CREATE INDEX "IX_lab_reports_related_report_id" ON capture.lab_reports(related_report_id);
            CREATE TABLE capture.lab_result_sets (
                id uuid PRIMARY KEY, company_id uuid NOT NULL, report_id uuid NOT NULL REFERENCES capture.lab_reports(id),
                position integer NOT NULL CHECK (position >= 0), sample_id uuid, laboratory_sample_reference text,
                stated_sample_code text, stated_seal_code text, section_reference text,
                CONSTRAINT ux_lab_result_sets_company_id UNIQUE(company_id,id),
                CONSTRAINT fk_lab_result_sets_report FOREIGN KEY(company_id,report_id) REFERENCES capture.lab_reports(company_id,id),
                CONSTRAINT fk_lab_result_sets_sample FOREIGN KEY(company_id,sample_id) REFERENCES capture.samples(company_id,id)
            );
            CREATE UNIQUE INDEX "IX_lab_result_sets_report_id_position" ON capture.lab_result_sets(report_id,position);
            CREATE INDEX "IX_lab_result_sets_sample_id" ON capture.lab_result_sets(sample_id);
            CREATE TABLE capture.lab_results (
                id uuid PRIMARY KEY, company_id uuid NOT NULL, result_set_id uuid NOT NULL REFERENCES capture.lab_result_sets(id),
                position integer NOT NULL CHECK (position >= 0), test_label text NOT NULL, substance_id uuid REFERENCES catalog.active_substances(id),
                original_result_text text NOT NULL, result_kind varchar(32) NOT NULL,
                numeric_value varchar(25), unit text, lod varchar(25), loq varchar(25), method text, uncertainty text, reference text,
                CONSTRAINT fk_lab_results_set FOREIGN KEY(company_id,result_set_id) REFERENCES capture.lab_result_sets(company_id,id),
                CONSTRAINT ck_lab_results_kind CHECK (result_kind IN ('NUMERIC','BELOW_LIMIT','NOT_DETECTED','QUALITATIVE','OTHER')),
                CONSTRAINT ck_lab_results_numeric_required CHECK (result_kind NOT IN ('NUMERIC','BELOW_LIMIT') OR numeric_value IS NOT NULL),
                CONSTRAINT ck_lab_results_numeric_value CHECK (numeric_value IS NULL OR numeric_value ~ '^[0-9]{1,12}(\.[0-9]{1,12})?$'),
                CONSTRAINT ck_lab_results_lod CHECK (lod IS NULL OR lod ~ '^[0-9]{1,12}(\.[0-9]{1,12})?$'),
                CONSTRAINT ck_lab_results_loq CHECK (loq IS NULL OR loq ~ '^[0-9]{1,12}(\.[0-9]{1,12})?$'),
                CONSTRAINT ck_lab_results_labels CHECK (length(btrim(test_label)) > 0 AND length(btrim(original_result_text)) > 0)
            );
            CREATE UNIQUE INDEX "IX_lab_results_result_set_id_position" ON capture.lab_results(result_set_id,position);
            CREATE INDEX "IX_lab_results_substance_id" ON capture.lab_results(substance_id);
            CREATE TABLE capture.lab_report_confirmations (
                id uuid PRIMARY KEY, company_id uuid NOT NULL, report_id uuid NOT NULL UNIQUE,
                confirmed_by uuid NOT NULL REFERENCES identity.users("Id"), confirmed_at timestamptz NOT NULL,
                identity_checked boolean NOT NULL, transcription_checked boolean NOT NULL,
                creation_transaction_id xid8 NOT NULL DEFAULT pg_current_xact_id(),
                CONSTRAINT ux_lab_confirmations_company_id UNIQUE(company_id,id),
                CONSTRAINT fk_lab_confirmations_report FOREIGN KEY(company_id,report_id) REFERENCES capture.lab_reports(company_id,id),
                CONSTRAINT ck_lab_confirmation_checked CHECK (identity_checked AND transcription_checked)
            );
            CREATE TABLE capture.lab_confirmed_associations (
                id uuid PRIMARY KEY, company_id uuid NOT NULL, confirmation_id uuid NOT NULL REFERENCES capture.lab_report_confirmations(id),
                result_set_id uuid NOT NULL UNIQUE, sample_id uuid NOT NULL, identity_basis varchar(32) NOT NULL, evidence_ref text,
                CONSTRAINT fk_lab_associations_confirmation FOREIGN KEY(company_id,confirmation_id) REFERENCES capture.lab_report_confirmations(company_id,id),
                CONSTRAINT fk_lab_associations_set FOREIGN KEY(company_id,result_set_id) REFERENCES capture.lab_result_sets(company_id,id),
                CONSTRAINT fk_lab_associations_sample FOREIGN KEY(company_id,sample_id) REFERENCES capture.samples(company_id,id),
                CONSTRAINT ck_lab_association_basis CHECK (identity_basis IN ('SAMPLE_CODE','SEAL_CODE','LAB_REFERENCE'))
            );
            CREATE INDEX "IX_lab_confirmed_associations_confirmation_id" ON capture.lab_confirmed_associations(confirmation_id);
            CREATE INDEX "IX_lab_confirmed_associations_sample_id" ON capture.lab_confirmed_associations(sample_id);

            -- Explicit top-level transaction stamps remain stable across EF SaveChanges savepoints.
            CREATE FUNCTION capture.stamp_lab_evidence_transaction() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            BEGIN
                NEW.creation_transaction_id := pg_current_xact_id();
                RETURN NEW;
            END $$;
            CREATE TRIGGER lab_report_transaction_stamp BEFORE INSERT ON capture.lab_reports FOR EACH ROW EXECUTE FUNCTION capture.stamp_lab_evidence_transaction();
            CREATE TRIGGER lab_confirmation_transaction_stamp BEFORE INSERT ON capture.lab_report_confirmations FOR EACH ROW EXECUTE FUNCTION capture.stamp_lab_evidence_transaction();

            CREATE FUNCTION capture.reject_lab_evidence_mutation() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            BEGIN
                RAISE EXCEPTION 'Laboratory evidence is immutable.' USING ERRCODE = '55000';
            END $$;
            CREATE TRIGGER lab_reports_immutable BEFORE UPDATE OR DELETE ON capture.lab_reports FOR EACH ROW EXECUTE FUNCTION capture.reject_lab_evidence_mutation();
            CREATE TRIGGER lab_result_sets_immutable BEFORE UPDATE OR DELETE ON capture.lab_result_sets FOR EACH ROW EXECUTE FUNCTION capture.reject_lab_evidence_mutation();
            CREATE TRIGGER lab_results_immutable BEFORE UPDATE OR DELETE ON capture.lab_results FOR EACH ROW EXECUTE FUNCTION capture.reject_lab_evidence_mutation();
            CREATE TRIGGER lab_confirmations_immutable BEFORE UPDATE OR DELETE ON capture.lab_report_confirmations FOR EACH ROW EXECUTE FUNCTION capture.reject_lab_evidence_mutation();
            CREATE TRIGGER lab_associations_immutable BEFORE UPDATE OR DELETE ON capture.lab_confirmed_associations FOR EACH ROW EXECUTE FUNCTION capture.reject_lab_evidence_mutation();

            CREATE FUNCTION capture.guard_lab_original_insert() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            DECLARE owner_report uuid;
            BEGIN
                IF TG_TABLE_NAME = 'lab_result_sets' THEN owner_report := NEW.report_id;
                ELSE SELECT s.report_id INTO owner_report FROM capture.lab_result_sets s WHERE s.company_id = NEW.company_id AND s.id = NEW.result_set_id;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM capture.lab_reports r WHERE r.id = owner_report AND r.company_id = NEW.company_id AND r.creation_transaction_id = pg_current_xact_id())
                   OR EXISTS (SELECT 1 FROM capture.lab_report_confirmations c WHERE c.report_id = owner_report AND c.company_id = NEW.company_id) THEN
                    RAISE EXCEPTION 'Laboratory evidence is immutable.' USING ERRCODE = '55000';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER lab_set_insert_guard BEFORE INSERT ON capture.lab_result_sets FOR EACH ROW EXECUTE FUNCTION capture.guard_lab_original_insert();
            CREATE TRIGGER lab_result_insert_guard BEFORE INSERT ON capture.lab_results FOR EACH ROW EXECUTE FUNCTION capture.guard_lab_original_insert();

            CREATE FUNCTION capture.guard_lab_association() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
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
            CREATE TRIGGER lab_association_insert_guard BEFORE INSERT ON capture.lab_confirmed_associations FOR EACH ROW EXECUTE FUNCTION capture.guard_lab_association();

            CREATE FUNCTION capture.check_lab_confirmation_complete() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM capture.lab_result_sets s WHERE s.company_id = NEW.company_id AND s.report_id = NEW.report_id)
                    OR EXISTS (SELECT 1 FROM capture.lab_result_sets s WHERE s.company_id = NEW.company_id AND s.report_id = NEW.report_id
                    AND (NOT EXISTS (SELECT 1 FROM capture.lab_confirmed_associations a WHERE a.company_id = NEW.company_id AND a.result_set_id = s.id AND a.confirmation_id = NEW.id)
                    OR NOT EXISTS (SELECT 1 FROM capture.lab_results r WHERE r.company_id = NEW.company_id AND r.result_set_id = s.id))) THEN
                    RAISE EXCEPTION 'Laboratory confirmation must cover every nonempty result set.' USING ERRCODE = '23514';
                END IF;
                RETURN NULL;
            END $$;
            CREATE CONSTRAINT TRIGGER lab_confirmation_complete AFTER INSERT ON capture.lab_report_confirmations DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION capture.check_lab_confirmation_complete();
            REVOKE ALL ON FUNCTION capture.stamp_lab_evidence_transaction(),capture.reject_lab_evidence_mutation(),capture.guard_lab_original_insert(),capture.guard_lab_association(),capture.check_lab_confirmation_complete() FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE capture.lab_confirmed_associations;
            DROP TABLE capture.lab_report_confirmations;
            DROP TABLE capture.lab_results;
            DROP TABLE capture.lab_result_sets;
            DROP TABLE capture.lab_reports;
            DROP FUNCTION capture.check_lab_confirmation_complete(),capture.guard_lab_association(),capture.guard_lab_original_insert(),capture.reject_lab_evidence_mutation(),capture.stamp_lab_evidence_transaction();
            ALTER TABLE capture.sample_handovers DROP CONSTRAINT ck_sample_handovers_kind, DROP CONSTRAINT ck_sample_handovers_lab,
                DROP COLUMN handover_kind, DROP COLUMN laboratory_name, DROP COLUMN laboratory_reference, DROP COLUMN laboratory_sample_reference;
            ALTER TABLE capture.samples DROP COLUMN material_description;
            """);
    }
}
