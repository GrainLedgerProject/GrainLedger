using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112000100_BlendApprovalRevocation")]
public sealed class BlendApprovalRevocation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION grainledger.ledger_release_revoked_blend(a uuid, proposal uuid) RETURNS integer
        LANGUAGE plpgsql SECURITY DEFINER SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
        AS $$
        DECLARE revoked record; released_count integer;
        BEGIN
            PERFORM grainledger.ledger_lock_writes();
            SELECT d.company_id, d.recorded_at, c.status
            INTO revoked
            FROM grainledger.exports_blend_plan_decisions d
            JOIN grainledger.exports_active_blend_approvals active
                ON active.proposal_id = d.proposal_id AND active.company_id = d.company_id
                   AND active.container_id = d.container_id
            JOIN grainledger.exports_containers c
                ON c.id = d.container_id AND c.company_id = d.company_id
            WHERE d.proposal_id = proposal AND d.version = 2 AND d.action = 'REVOKED' AND d.actor_id = a;
            IF NOT FOUND OR grainledger.ledger_role(a) IS DISTINCT FROM 'CustomerManager'
               OR NOT COALESCE(grainledger.ledger_own(a,revoked.company_id),false) THEN
                RAISE EXCEPTION 'Blend reservation release access denied' USING ERRCODE='42501';
            END IF;
            IF revoked.status <> 'PLANNED' OR EXISTS (
                SELECT 1 FROM grainledger.ledger_blend_reservation_events
                WHERE proposal_id = proposal AND action = 'CONSUME'
            ) THEN
                RAISE EXCEPTION 'Blend execution already started' USING ERRCODE='23514';
            END IF;

            INSERT INTO grainledger.ledger_blend_reservation_events
                (id, proposal_id, lot_id, location_id, action, quantity_kg, actor_id, recorded_at)
            SELECT gen_random_uuid(), r.proposal_id, r.lot_id, r.location_id,
                   'RELEASE', remainder.quantity_kg, a, revoked.recorded_at
            FROM grainledger.ledger_blend_reservations r
            CROSS JOIN LATERAL (
                SELECT r.quantity_kg - COALESCE(SUM(e.quantity_kg),0) AS quantity_kg
                FROM grainledger.ledger_blend_reservation_events e
                WHERE (e.proposal_id,e.lot_id,e.location_id) = (r.proposal_id,r.lot_id,r.location_id)
                    AND e.action = 'RELEASE'
            ) remainder
            WHERE r.proposal_id = proposal AND remainder.quantity_kg > 0;
            GET DIAGNOSTICS released_count = ROW_COUNT;
            IF released_count = 0 THEN
                RAISE EXCEPTION 'No outstanding blend reservations to release' USING ERRCODE='23514';
            END IF;
            RETURN released_count;
        END;
        $$;

        REVOKE ALL ON FUNCTION grainledger.ledger_release_revoked_blend(uuid,uuid) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Blend reservation release history requires a forward migration.");
}
