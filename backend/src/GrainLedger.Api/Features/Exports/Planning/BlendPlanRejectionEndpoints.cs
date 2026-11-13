using System.Data;
using GrainLedger.Api.Features.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Api.Features.Exports;

public sealed record RejectBlendPlanRequest(Guid OperationId, Guid ProposalId,
    int? ExpectedDecisionVersion, string? Reason);

public static partial class ExportModule
{
    private static async Task<IResult> RejectBlendPlanAsync(Guid id, RejectBlendPlanRequest request,
        HttpContext http, ICurrentAccess access, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, db, access, Permissions.BlendApprove, ct);
        if (scope is null) return Results.Forbid();
        var (actor, company) = scope.Value;
        if (!await db.Users.AnyAsync(user => user.Id == actor && user.IsActive
            && user.OrganizationId == company && user.Role == Roles.CustomerManager, ct))
            return Results.Forbid();

        var reason = request.Reason?.Trim();
        if (request.OperationId == Guid.Empty || request.ProposalId == Guid.Empty ||
            request.ExpectedDecisionVersion is null or < 0)
            return Invalid(new() { ["request"] = ["Provide an operation ID, proposal ID and nonnegative decision version."] });
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000 || reason.Contains('\0'))
            return Invalid(new() { ["reason"] = ["Provide a reason of 1 to 1000 characters."] });

        var hash = ExportRules.Hash("reject-blend-plan:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Use the same lock order as proposal and approval writers.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            await LockAsync(db, company, ct);
            var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == company && x.ActorId == actor && x.OperationId == request.OperationId, ct);
            if (prior is not null) return Replay(prior, hash, http);

            var rows = await ReadBlendDecisionRowsAsync(db, id, company, ct);
            if (rows.Count == 0 || rows[0].ProposalId != request.ProposalId) return Results.NotFound();
            var decision = rows[0];
            if (decision.DecisionVersion != request.ExpectedDecisionVersion || decision.Status != "PENDING")
                return Problem(409, "PLAN_DECISION_CONFLICT", "Only a pending proposal at the expected decision version can be rejected.");

            var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == id && x.CompanyId == company, ct);
            var now = clock.GetUtcNow().ToUniversalTime();
            var recorded = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO grainledger.exports_blend_plan_decisions
                    (proposal_id, version, company_id, container_id, plan_id, action, actor_id, recorded_at, reason)
                VALUES ({decision.ProposalId}, 1, {company}, {plan.ContainerId}, {id}, 'REJECTED', {actor}, {now}, {reason})
                ON CONFLICT (proposal_id, version) DO NOTHING
                """, ct);
            if (recorded != 1)
                return Problem(409, "PLAN_DECISION_CONFLICT", "This proposal already has a decision.");

            PlanEvent(db, plan, actor, "PLAN_REJECTED", now);
            decision.Status = "REJECTED";
            decision.DecisionVersion = 1;
            decision.ActorId = actor;
            decision.RecordedAt = now;
            decision.Reason = reason;
            var response = BlendDecisionResponse(id, rows);
            await StoreResultAsync(db, new ExportOperation
            {
                Id = Guid.NewGuid(), CompanyId = company, ActorId = actor,
                OperationId = request.OperationId, RequestHash = hash,
                OrderId = plan.OrderId, ContainerId = plan.ContainerId
            }, response, 200, decision.DecisionVersion, ct);
            await transaction.CommitAsync(ct);
            SetEtag(http, decision.DecisionVersion);
            return Results.Ok(response);
        }
        catch (PostgresException ex) when (DecisionStorageUnavailable(ex))
        {
            return DecisionUnavailable();
        }
    }
}
