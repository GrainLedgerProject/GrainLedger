using System.Data;
using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Api.Features.Exports;

public sealed record ApproveBlendPlanRequest(Guid OperationId, Guid ProposalId, int? ExpectedDecisionVersion);

public static partial class ExportModule
{
    private static async Task<IResult> ApproveBlendPlanAsync(Guid id, ApproveBlendPlanRequest request,
        HttpContext http, ICurrentAccess access, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, db, access, Permissions.BlendApprove, ct);
        if (scope is null) return Results.Forbid();
        var (actor, company) = scope.Value;
        if (!await db.Users.AnyAsync(user => user.Id == actor && user.IsActive
            && user.OrganizationId == company && user.Role == Roles.CustomerManager, ct))
            return Results.Forbid();
        if (request.OperationId == Guid.Empty || request.ProposalId == Guid.Empty ||
            request.ExpectedDecisionVersion is null or < 0)
            return Invalid(new() { ["request"] = ["Provide an operation ID, proposal ID and nonnegative decision version."] });

        var hash = ExportRules.Hash("approve-blend-plan:" + id, request);
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
            if (rows[0].Status != "PENDING" || rows[0].DecisionVersion != request.ExpectedDecisionVersion)
                return Problem(409, "PLAN_DECISION_CONFLICT", "Only a pending proposal at the expected decision version can be approved.");
            var proposal = await db.BlendPlanProposals.AsNoTracking().SingleAsync(p => p.Id == request.ProposalId, ct);
            var plan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(p => p.Id == id && p.CompanyId == company, ct);
            var container = await db.Containers.AsNoTracking().SingleAsync(c => c.Id == plan.ContainerId, ct);
            if (container.Status != "PLANNED")
                return Problem(409, "CONTAINER_IN_USE", "Only a planned container can receive a new approval.");
            if (await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM grainledger.exports_active_blend_approvals
                    WHERE company_id = {company} AND container_id = {plan.ContainerId}) AS "Value"
                """).SingleAsync(ct))
                return ActiveBlendApprovalConflict();

            var preview = await db.BlendPlanPreviews.AsNoTracking().SingleAsync(p => p.Id == proposal.PreviewId, ct);
            var previewRequest = JsonSerializer.Deserialize<PreviewBlendPlanRequest>(preview.RequestJson);
            var saved = ReadSavedBlendSelection(proposal.SnapshotJson);
            if (previewRequest is null || saved is null)
                return InvalidSavedBlendProposal();

            var reassessed = await ComputeBlendPlanPreviewAsync(id, previewRequest, http, access, db,
                ledger, residue, applicableMrl, assessmentPolicies, clock, false, Permissions.BlendApprove, ct,
                saved.Value.Selection);
            if (reassessed is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK } ||
                reassessed is not IValueHttpResult { Value: { } value }) return reassessed;
            var result = JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result.GetProperty("assessmentStatus").GetString() != "PASS" ||
                result.GetProperty("dependencyRevision").GetString() != saved.Value.DependencyRevision)
                return Problem(409, "PLAN_PROPOSAL_STALE", "The saved allocations or assessment evidence no longer pass the original proposal checks.");

            var now = clock.GetUtcNow().ToUniversalTime();
            var recorded = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO grainledger.exports_blend_plan_decisions
                    (proposal_id, version, company_id, container_id, plan_id, action, actor_id, recorded_at)
                VALUES ({proposal.Id}, 1, {company}, {plan.ContainerId}, {id}, 'APPROVED', {actor}, {now})
                ON CONFLICT (proposal_id, version) DO NOTHING
                """, ct);
            if (recorded != 1) return Problem(409, "PLAN_DECISION_CONFLICT", "This proposal already has a decision.");
            var activated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO grainledger.exports_active_blend_approvals(company_id,container_id,proposal_id)
                VALUES ({company},{plan.ContainerId},{proposal.Id})
                ON CONFLICT DO NOTHING
                """, ct);
            if (activated != 1) return ActiveBlendApprovalConflict();

            await db.Database.SqlQuery<int>($"""
                SELECT grainledger.ledger_reserve_approved_blend({actor},{proposal.Id}) AS "Value"
                """).SingleAsync(ct);
            PlanEvent(db, plan, actor, "PLAN_APPROVED", now);
            var response = BlendDecisionResponse(id, await ReadBlendDecisionRowsAsync(db, id, company, ct));
            await StoreResultAsync(db, new ExportOperation
            {
                Id = Guid.NewGuid(), CompanyId = company, ActorId = actor,
                OperationId = request.OperationId, RequestHash = hash,
                OrderId = plan.OrderId, ContainerId = plan.ContainerId
            }, response, 200, 1, ct);
            await transaction.CommitAsync(ct);
            SetEtag(http, 1);
            return Results.Ok(response);
        }
        catch (PostgresException ex) when (DecisionStorageUnavailable(ex) || ex.SqlState == PostgresErrorCodes.UndefinedFunction)
        {
            return DecisionUnavailable();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return Problem(409, "PLAN_RESERVATION_CONFLICT", "The approved stock could not be reserved.");
        }
        catch (JsonException)
        {
            return InvalidSavedBlendProposal();
        }
    }

    private static IResult InvalidSavedBlendProposal() =>
        Problem(409, "PLAN_PROPOSAL_INVALID", "The saved proposal has no valid passing allocation.");

    private static IResult ActiveBlendApprovalConflict() =>
        Problem(409, "ACTIVE_BLEND_APPROVAL_EXISTS", "Revoke the active blend approval before changing its demand or approving another plan.");

    private static (GreedyStockSelection.Result Selection, string DependencyRevision)? ReadSavedBlendSelection(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("outcome").GetString() != "PASS" ||
                root.GetProperty("assessmentStatus").GetString() != "PASS" ||
                root.GetProperty("dependencyRevision").GetString() is not { Length: > 0 } dependency)
                return null;
            var selected = root.GetProperty("selection");
            var mode = selected.GetProperty("mode").GetString();
            if (mode is not ("DIRECT_STOCK" or "BLEND_INPUT_STOCK")) return null;
            var compatibility = selected.GetProperty("compatibilityPolicyId");
            var allocations = new List<GreedyStockSelection.Allocation>();
            foreach (var row in selected.GetProperty("allocations").EnumerateArray())
            {
                if (!ExportRules.TryMass(row.GetProperty("quantityKg").GetString(), "KG", out var quantity))
                    return null;
                allocations.Add(new(row.GetProperty("lotId").GetGuid(), row.GetProperty("locationId").GetGuid(),
                    row.GetProperty("lotCode").GetString()!, quantity, row.GetProperty("candidateRevision").GetString()!));
            }
            if (allocations.Count is < 1 or > 100) return null;
            return (new("MASS_COVERED", mode, compatibility.ValueKind == JsonValueKind.Null ? null : compatibility.GetGuid(),
                allocations.Sum(row => row.QuantityKg), 0, allocations), dependency);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static async Task<bool> HasActiveBlendApprovalAsync(AppDbContext db, Guid company, Guid order,
        CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('grainledger.exports_active_blend_approvals') IS NOT NULL AS "Value"
            """).SingleAsync(ct)) return false;
        return await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(
                SELECT 1 FROM grainledger.exports_active_blend_approvals a
                JOIN grainledger.exports_containers c ON c.id = a.container_id AND c.company_id = a.company_id
                WHERE a.company_id = {company} AND c.order_id = {order}
            ) AS "Value"
            """).SingleAsync(ct);
    }
}
