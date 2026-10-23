using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261023000000_LabReportDraftRevisions")]
public sealed class LabReportDraftRevisions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE capture.lab_report_retirements (
                report_id uuid PRIMARY KEY REFERENCES capture.lab_reports(id),
                company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
                replacement_report_id uuid UNIQUE REFERENCES capture.lab_reports(id),
                status varchar(32) NOT NULL,
                recorded_by uuid NOT NULL REFERENCES identity.users("Id"), recorded_at timestamptz NOT NULL,
                CONSTRAINT "IX_lab_report_retirements_company_id_report_id" UNIQUE(company_id,report_id),
                CONSTRAINT fk_lab_retirement_report FOREIGN KEY(company_id,report_id) REFERENCES capture.lab_reports(company_id,id),
                CONSTRAINT fk_lab_retirement_replacement FOREIGN KEY(company_id,replacement_report_id) REFERENCES capture.lab_reports(company_id,id),
                CONSTRAINT ck_lab_retirement_status CHECK (
                    (status = 'DELETED' AND replacement_report_id IS NULL) OR
                    (status = 'SUPERSEDED' AND replacement_report_id IS NOT NULL AND replacement_report_id <> report_id))
            );
            CREATE TRIGGER lab_retirement_immutable BEFORE UPDATE OR DELETE ON capture.lab_report_retirements
                FOR EACH ROW EXECUTE FUNCTION capture.reject_lab_evidence_mutation();

            CREATE FUNCTION capture.guard_lab_retirement() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(714928352);
                IF EXISTS (SELECT 1 FROM capture.lab_report_confirmations WHERE company_id=NEW.company_id AND report_id=NEW.report_id)
                    OR EXISTS (SELECT 1 FROM capture.lab_report_retirements WHERE company_id=NEW.company_id AND report_id=NEW.report_id) THEN
                    RAISE EXCEPTION 'Laboratory report is confirmed or retired.' USING ERRCODE = '55000';
                END IF;
                IF NEW.status = 'DELETED' AND EXISTS (
                    SELECT 1 FROM capture.lab_reports WHERE company_id=NEW.company_id AND related_report_id=NEW.report_id) THEN
                    RAISE EXCEPTION 'Laboratory report is referenced.' USING ERRCODE = '23514';
                END IF;
                IF NEW.replacement_report_id IS NOT NULL AND (
                    NOT EXISTS (SELECT 1 FROM capture.lab_reports WHERE company_id=NEW.company_id AND id=NEW.replacement_report_id
                        AND creation_transaction_id=pg_current_xact_id())
                    OR EXISTS (SELECT 1 FROM capture.lab_report_confirmations WHERE company_id=NEW.company_id AND report_id=NEW.replacement_report_id)
                    OR EXISTS (SELECT 1 FROM capture.lab_report_retirements WHERE company_id=NEW.company_id AND report_id=NEW.replacement_report_id)) THEN
                    RAISE EXCEPTION 'Replacement must be a new unconfirmed report in this transaction.' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER lab_retirement_insert_guard BEFORE INSERT ON capture.lab_report_retirements
                FOR EACH ROW EXECUTE FUNCTION capture.guard_lab_retirement();

            CREATE FUNCTION capture.guard_lab_active_report() RETURNS trigger LANGUAGE plpgsql SET search_path = pg_catalog AS $$
            DECLARE target_report uuid;
            BEGIN
                PERFORM pg_advisory_xact_lock(714928352);
                IF TG_TABLE_NAME = 'lab_report_confirmations' THEN target_report := NEW.report_id;
                ELSE target_report := NEW.related_report_id;
                END IF;
                IF EXISTS (SELECT 1 FROM capture.lab_report_retirements WHERE company_id=NEW.company_id AND report_id=target_report) THEN
                    RAISE EXCEPTION 'Laboratory report is retired.' USING ERRCODE = '55000';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER lab_confirmation_active_guard BEFORE INSERT ON capture.lab_report_confirmations
                FOR EACH ROW EXECUTE FUNCTION capture.guard_lab_active_report();
            CREATE TRIGGER lab_related_active_guard BEFORE INSERT ON capture.lab_reports
                FOR EACH ROW WHEN (NEW.related_report_id IS NOT NULL) EXECUTE FUNCTION capture.guard_lab_active_report();
            REVOKE ALL ON TABLE capture.lab_report_retirements FROM PUBLIC;
            REVOKE ALL ON FUNCTION capture.guard_lab_retirement(),capture.guard_lab_active_report() FROM PUBLIC;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER lab_confirmation_active_guard ON capture.lab_report_confirmations;
            DROP TRIGGER lab_related_active_guard ON capture.lab_reports;
            DROP TABLE capture.lab_report_retirements;
            DROP FUNCTION capture.guard_lab_active_report(),capture.guard_lab_retirement();
            """);
    }
}
