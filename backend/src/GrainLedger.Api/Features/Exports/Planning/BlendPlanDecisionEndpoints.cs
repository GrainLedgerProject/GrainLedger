using GrainLedger.Api.Features.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Api.Features.Exports;

public static partial class ExportModule
{
    private sealed class BlendDecisionRow
    {
        public Guid ProposalId { get; set; }
        public string Status { get; set; } = "";
        public int DecisionVersion { get; set; }
        public Guid? ActorId { get; set; }
        public DateTimeOffset? RecordedAt { get; set; }
        public string? Reason { get; set; }
        public bool ActiveApproval { get; set; }
        public Guid? LotId { get; set; }
        public Guid? LocationId { get; set; }
        public decimal ReservedKg { get; set; }
        public decimal ReleasedKg { get; set; }
        public decimal ConsumedKg { get; set; }
    }

    private static async Task<IResult> GetBlendPlanDecisionAsync(Guid id, HttpContext http,
        ICurrentAccess access, AppDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, db, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();

        try
        {
            var rows = await ReadBlendDecisionRowsAsync(db, id, scope.Value.Company, ct);
            return rows.Count == 0 ? Results.NotFound() : Results.Ok(BlendDecisionResponse(id, rows));
        }
        catch (PostgresException ex) when (DecisionStorageUnavailable(ex))
        {
            return DecisionUnavailable();
        }
    }

    private static bool DecisionStorageUnavailable(PostgresException ex) =>
        ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InsufficientPrivilege;

    private static IResult DecisionUnavailable() =>
        Problem(503, "DECISION_SCHEMA_NOT_READY", "Blend decision storage or runtime grants are not ready.");

    // One statement keeps the decision and reservations on the same database snapshot.
    private static Task<List<BlendDecisionRow>> ReadBlendDecisionRowsAsync(
        AppDbContext db, Guid id, Guid companyId, CancellationToken ct) =>
        db.Database.SqlQuery<BlendDecisionRow>($"""
        SELECT p.id AS "ProposalId", COALESCE(d.action, 'PENDING') AS "Status",
               COALESCE(d.version, 0) AS "DecisionVersion", d.actor_id AS "ActorId",
               d.recorded_at AS "RecordedAt", d.reason AS "Reason",
               a.proposal_id IS NOT NULL AS "ActiveApproval",
               r.lot_id AS "LotId", r.location_id AS "LocationId",
               COALESCE(r.quantity_kg, 0) AS "ReservedKg",
               COALESCE(e.released_kg, 0) AS "ReleasedKg",
               COALESCE(e.consumed_kg, 0) AS "ConsumedKg"
        FROM grainledger.exports_blend_plan_proposals p
        LEFT JOIN LATERAL (
            SELECT action, version, actor_id, recorded_at, reason
            FROM grainledger.exports_blend_plan_decisions
            WHERE proposal_id = p.id AND company_id = p.company_id
            ORDER BY version DESC LIMIT 1
        ) d ON true
        LEFT JOIN grainledger.exports_active_blend_approvals a
            ON a.proposal_id = p.id AND a.company_id = p.company_id
        LEFT JOIN grainledger.ledger_blend_reservations r ON r.proposal_id = p.id
        LEFT JOIN LATERAL (
            SELECT SUM(quantity_kg) FILTER (WHERE action = 'RELEASE') AS released_kg,
                   SUM(quantity_kg) FILTER (WHERE action = 'CONSUME') AS consumed_kg
            FROM grainledger.ledger_blend_reservation_events
            WHERE proposal_id = r.proposal_id AND lot_id = r.lot_id AND location_id = r.location_id
        ) e ON true
        WHERE p.plan_id = {id} AND p.company_id = {companyId}
        ORDER BY r.lot_id, r.location_id
        """).ToListAsync(ct);

    private static object BlendDecisionResponse(Guid id, List<BlendDecisionRow> rows)
    {
        var decision = rows[0];
        var reserved = rows.Sum(row => row.ReservedKg);
        var released = rows.Sum(row => row.ReleasedKg);
        var consumed = rows.Sum(row => row.ConsumedKg);
        var remaining = reserved - released - consumed;
        var reservationStatus = reserved == 0 ? "NOT_RESERVED"
            : remaining > 0 ? "HELD"
            : consumed == reserved ? "CONSUMED"
            : released == reserved ? "RELEASED" : "SETTLED";

        return new
        {
            planId = id,
            decision.ProposalId,
            decision.Status,
            decision.DecisionVersion,
            decision.ActorId,
            decision.RecordedAt,
            decision.Reason,
            decision.ActiveApproval,
            reservation = new
            {
                status = reservationStatus,
                reservedKg = ExportRules.Text(reserved),
                releasedKg = ExportRules.Text(released),
                consumedKg = ExportRules.Text(consumed),
                remainingKg = ExportRules.Text(remaining),
                lines = rows.Where(row => row.LotId.HasValue).Select(row => new
                {
                    row.LotId,
                    row.LocationId,
                    reservedKg = ExportRules.Text(row.ReservedKg),
                    releasedKg = ExportRules.Text(row.ReleasedKg),
                    consumedKg = ExportRules.Text(row.ConsumedKg),
                    remainingKg = ExportRules.Text(row.ReservedKg - row.ReleasedKg - row.ConsumedKg)
                }).ToArray()
            }
        };
    }
}
