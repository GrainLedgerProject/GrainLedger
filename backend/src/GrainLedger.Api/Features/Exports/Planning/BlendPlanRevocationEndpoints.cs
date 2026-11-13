using System.Data;
using GrainLedger.Api.Features.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Api.Features.Exports;

public sealed record RevokeBlendPlanRequest(Guid OperationId, Guid ProposalId,
    int? ExpectedDecisionVersion, string? Reason);

public static partial class ExportModule
{
    private static async Task<IResult> RevokeBlendPlanAsync(Guid id, RevokeBlendPlanRequest request,
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

        var hash = ExportRules.Hash("revoke-blend-plan:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            await LockAsync(db, company, ct);
            var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.CompanyId == company && x.ActorId == actor && x.OperationId == request.OperationId, ct);
            if (prior is not null) return Replay(prior, hash, http);

            var rows = await ReadBlendDecisionRowsAsync(db, id, company, ct);
            if (rows.Count == 0 || rows[0].ProposalId != request.ProposalId) return Results.NotFound();
            var decision = rows[0];
            if (decision.Status != "APPROVED" || !decision.ActiveApproval ||
                decision.DecisionVersion != request.ExpectedDecisionVersion)
                return Problem(409, "PLAN_DECISION_CONFLICT", "Only an active approval at the expected decision version can be revoked.");

            var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(p => p.Id == id && p.CompanyId == company, ct);
            var container = await db.Containers.AsNoTracking().SingleAsync(c => c.Id == plan.ContainerId, ct);
            if (container.Status != "PLANNED" || rows.Any(row => row.ConsumedKg > 0))
                return Problem(409, "PLAN_EXECUTION_STARTED", "An approval cannot be revoked once execution starts.");

            var now = clock.GetUtcNow().ToUniversalTime();
            var recorded = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO grainledger.exports_blend_plan_decisions
                    (proposal_id, version, company_id, container_id, plan_id, action, actor_id, recorded_at, reason)
                VALUES ({decision.ProposalId}, 2, {company}, {plan.ContainerId}, {id}, 'REVOKED', {actor}, {now}, {reason})
                ON CONFLICT (proposal_id, version) DO NOTHING
                """, ct);
            if (recorded != 1)
                return Problem(409, "PLAN_DECISION_CONFLICT", "This approval already has a revocation.");

            await db.Database.SqlQuery<int>($"""
                SELECT grainledger.ledger_release_revoked_blend({actor},{decision.ProposalId}) AS "Value"
                """).SingleAsync(ct);
            var removed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM grainledger.exports_active_blend_approvals
                WHERE company_id = {company} AND container_id = {plan.ContainerId} AND proposal_id = {decision.ProposalId}
                """, ct);
            if (removed != 1)
                return Problem(409, "PLAN_DECISION_CONFLICT", "This proposal no longer has an active approval.");

            PlanEvent(db, plan, actor, "PLAN_REVOKED", now);
            var response = BlendDecisionResponse(id, await ReadBlendDecisionRowsAsync(db, id, company, ct));
            await StoreResultAsync(db, new ExportOperation
            {
                Id = Guid.NewGuid(), CompanyId = company, ActorId = actor,
                OperationId = request.OperationId, RequestHash = hash,
                OrderId = plan.OrderId, ContainerId = plan.ContainerId
            }, response, 200, 2, ct);
            await transaction.CommitAsync(ct);
            SetEtag(http, 2);
            return Results.Ok(response);
        }
        catch (PostgresException ex) when (DecisionStorageUnavailable(ex) || ex.SqlState == PostgresErrorCodes.UndefinedFunction)
        {
            return DecisionUnavailable();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return Problem(409, "PLAN_RELEASE_CONFLICT", "The reserved stock could not be released.");
        }
    }
}
