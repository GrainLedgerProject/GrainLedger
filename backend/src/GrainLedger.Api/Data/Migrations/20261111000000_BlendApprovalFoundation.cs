using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261111000000_BlendApprovalFoundation")]
public sealed class BlendApprovalFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE grainledger.exports_blend_plan_proposals
            ADD UNIQUE (company_id, plan_id, id);
        ALTER TABLE grainledger.exports_blend_plan_drafts
            ADD UNIQUE (company_id, container_id, id);

        CREATE TABLE grainledger.exports_blend_plan_decisions (
            proposal_id uuid NOT NULL,
            version integer NOT NULL,
            company_id uuid NOT NULL,
            container_id uuid NOT NULL,
            plan_id uuid NOT NULL,
            action text NOT NULL,
            actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
            recorded_at timestamptz NOT NULL,
            reason text,
            previous_version integer GENERATED ALWAYS AS
                (CASE WHEN version = 2 THEN 1 END) STORED,
            previous_action text GENERATED ALWAYS AS
                (CASE WHEN version = 2 THEN 'APPROVED'::text END) STORED,
            PRIMARY KEY (proposal_id, version),
            UNIQUE (company_id, container_id, proposal_id, version, action),
            FOREIGN KEY (company_id, plan_id, proposal_id)
                REFERENCES grainledger.exports_blend_plan_proposals (company_id, plan_id, id),
            FOREIGN KEY (company_id, container_id, plan_id)
                REFERENCES grainledger.exports_blend_plan_drafts (company_id, container_id, id),
            FOREIGN KEY (company_id, container_id, proposal_id, previous_version, previous_action)
                REFERENCES grainledger.exports_blend_plan_decisions
                    (company_id, container_id, proposal_id, version, action),
            CHECK ((version = 1 AND action IN ('APPROVED', 'REJECTED'))
                OR (version = 2 AND action = 'REVOKED')),
            CHECK ((action = 'APPROVED' AND reason IS NULL)
                OR (action IN ('REJECTED', 'REVOKED') AND reason IS NOT NULL
                    AND length(btrim(reason)) BETWEEN 1 AND 1000))
        );

        CREATE TABLE grainledger.exports_active_blend_approvals (
            company_id uuid NOT NULL,
            container_id uuid NOT NULL,
            proposal_id uuid NOT NULL UNIQUE,
            decision_version integer NOT NULL DEFAULT 1 CHECK (decision_version = 1),
            action text NOT NULL DEFAULT 'APPROVED' CHECK (action = 'APPROVED'),
            PRIMARY KEY (company_id, container_id),
            FOREIGN KEY (company_id, container_id, proposal_id, decision_version, action)
                REFERENCES grainledger.exports_blend_plan_decisions
                    (company_id, container_id, proposal_id, version, action)
        );

        CREATE TABLE grainledger.ledger_blend_reservations (
            proposal_id uuid NOT NULL REFERENCES grainledger.exports_blend_plan_proposals (id),
            lot_id uuid NOT NULL REFERENCES grainledger.ledger_lots (id),
            location_id uuid NOT NULL REFERENCES grainledger.ledger_locations (id),
            quantity_kg numeric(20,6) NOT NULL CHECK (quantity_kg > 0),
            PRIMARY KEY (proposal_id, lot_id, location_id)
        );

        CREATE INDEX ledger_blend_reservations_position_idx
            ON grainledger.ledger_blend_reservations (lot_id, location_id);

        CREATE TABLE grainledger.ledger_blend_reservation_events (
            id uuid PRIMARY KEY,
            proposal_id uuid NOT NULL,
            lot_id uuid NOT NULL,
            location_id uuid NOT NULL,
            action text NOT NULL CHECK (action IN ('RELEASE', 'CONSUME')),
            quantity_kg numeric(20,6) NOT NULL CHECK (quantity_kg > 0),
            actor_id uuid NOT NULL REFERENCES grainledger.identity_users("Id"),
            recorded_at timestamptz NOT NULL,
            FOREIGN KEY (proposal_id, lot_id, location_id)
                REFERENCES grainledger.ledger_blend_reservations (proposal_id, lot_id, location_id)
        );

        CREATE INDEX ledger_blend_reservation_events_line_idx
            ON grainledger.ledger_blend_reservation_events (proposal_id, lot_id, location_id);

        CREATE TRIGGER exports_immutable_blend_plan_decision
            BEFORE UPDATE OR DELETE ON grainledger.exports_blend_plan_decisions
            FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();
        CREATE TRIGGER ledger_immutable_blend_reservation
            BEFORE UPDATE OR DELETE ON grainledger.ledger_blend_reservations
            FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();
        CREATE TRIGGER ledger_immutable_blend_reservation_event
            BEFORE UPDATE OR DELETE ON grainledger.ledger_blend_reservation_events
            FOR EACH ROW EXECUTE FUNCTION grainledger.exports_reject_historical_change();

        REVOKE ALL ON grainledger.exports_blend_plan_decisions, grainledger.exports_active_blend_approvals,
            grainledger.ledger_blend_reservations, grainledger.ledger_blend_reservation_events FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Blend approval history requires a forward migration.");
}
