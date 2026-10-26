using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Exports.Migrations;

[DbContext(typeof(ExportDbContext))]
[Migration("20261026000000_BlendPlanProposals")]
public sealed class BlendPlanProposalsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE exports.blend_plan_previews (
            id uuid PRIMARY KEY,
            company_id uuid NOT NULL,
            plan_id uuid NOT NULL,
            actor_id uuid NOT NULL REFERENCES identity.users("Id"),
            request_json jsonb NOT NULL,
            snapshot_json jsonb NOT NULL,
            fingerprint text NOT NULL CHECK (length(fingerprint)=64),
            created_at timestamptz NOT NULL,
            expires_at timestamptz NOT NULL CHECK (expires_at>created_at),
            CONSTRAINT fk_blend_preview_plan FOREIGN KEY(company_id,plan_id)
                REFERENCES exports.blend_plan_drafts(company_id,id),
            CONSTRAINT uq_blend_preview_company_id UNIQUE(company_id,id)
        );
        CREATE INDEX ix_blend_preview_scope ON exports.blend_plan_previews
            (company_id,plan_id,expires_at);
        CREATE TABLE exports.blend_plan_proposals (
            id uuid PRIMARY KEY,
            company_id uuid NOT NULL,
            plan_id uuid NOT NULL UNIQUE,
            preview_id uuid NOT NULL,
            actor_id uuid NOT NULL REFERENCES identity.users("Id"),
            snapshot_json jsonb NOT NULL,
            proposed_at timestamptz NOT NULL,
            CONSTRAINT fk_blend_proposal_plan FOREIGN KEY(company_id,plan_id)
                REFERENCES exports.blend_plan_drafts(company_id,id),
            CONSTRAINT fk_blend_proposal_preview FOREIGN KEY(company_id,preview_id)
                REFERENCES exports.blend_plan_previews(company_id,id),
            CONSTRAINT uq_blend_proposal_company_id UNIQUE(company_id,id)
        );
        CREATE TRIGGER immutable_blend_preview BEFORE UPDATE OR DELETE
            ON exports.blend_plan_previews FOR EACH ROW
            EXECUTE FUNCTION exports.reject_historical_change();
        CREATE TRIGGER immutable_blend_proposal BEFORE UPDATE OR DELETE
            ON exports.blend_plan_proposals FOR EACH ROW
            EXECUTE FUNCTION exports.reject_historical_change();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Planning preview and proposal history cannot be rolled back safely.");
}
