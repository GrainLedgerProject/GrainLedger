using System.Data;
using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GrainLedger.Api.Features.Exports;

internal sealed record BlendExecutionReservation(Guid LotId, Guid LocationId, decimal RemainingKg);
internal sealed record BlendExecutionAuthorization(Guid PlanId, Guid ProposalId, int DecisionVersion,
    Guid CompanyId, Guid OrderId, Guid ContainerId, int ContainerVersion, Guid FacilityId,
    string SelectionMode, Guid? CompatibilityPolicyId, IReadOnlyList<BlendExecutionReservation> Reservations);
internal sealed record BlendExecutionStartResult(BlendExecutionAuthorization? Authorization, IResult? Failure);

public static partial class ExportModule
{
    // F16 starts execution in its own transaction. It must commit the marker and
    // the first execution write together, or roll the transaction back.
    internal static async Task<BlendExecutionStartResult> BeginApprovedBlendExecutionAsync(Guid containerId,
        Guid proposalId, int expectedDecisionVersion, HttpContext http, ICurrentAccess access,
        AppDbContext db, LedgerService ledger, ResidueEvaluationService residue,
        ApplicableMrlRead applicableMrl, BlendAssessmentPolicyRead assessmentPolicies,
        TimeProvider clock, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Blend execution requires a caller-owned READ COMMITTED transaction.");
        var scope = await ScopeAsync(http, db, access, Permissions.ContainersWrite, ct);
        if (scope is null) return new(null, Results.Forbid());
        var (actor, company) = scope.Value;
        if (!await db.Users.AnyAsync(user => user.Id == actor && user.IsActive &&
                user.OrganizationId == company && user.Role == Roles.ExportStaff, ct))
            return new(null, Results.Forbid());

        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
            await LockAsync(db, company, ct);
            var container = await db.Containers.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == containerId && row.CompanyId == company, ct);
            if (container is null) return new(null, Results.NotFound());
            if (container.Status != "PLANNED")
                return new(null, Problem(409, "PLAN_EXECUTION_STARTED", "This container has already started execution."));

            var proposal = await db.BlendPlanProposals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == proposalId && row.CompanyId == company, ct);
            if (proposal is null) return new(null, Results.NotFound());
            var plan = await db.BlendPlanDrafts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == proposal.PlanId && row.CompanyId == company && row.ContainerId == containerId, ct);
            if (plan is null) return new(null, Results.NotFound());
            var rows = await ReadBlendDecisionRowsAsync(db, plan.Id, company, ct);
            if (rows.Count == 0 || rows[0].Status != "APPROVED" || !rows[0].ActiveApproval ||
                rows[0].DecisionVersion != expectedDecisionVersion)
                return new(null, Problem(409, "PLAN_APPROVAL_REQUIRED", "A current active blend approval is required."));

            var saved = ReadSavedBlendSelection(proposal.SnapshotJson);
            var preview = await db.BlendPlanPreviews.AsNoTracking().SingleAsync(row => row.Id == proposal.PreviewId, ct);
            var request = JsonSerializer.Deserialize<PreviewBlendPlanRequest>(preview.RequestJson);
            if (saved is null || request is null) return new(null, InvalidSavedBlendProposal());

            var remaining = rows.Where(row => row.LotId.HasValue && row.LocationId.HasValue)
                .ToDictionary(row => (row.LotId!.Value, row.LocationId!.Value),
                    row => row.ReservedKg - row.ReleasedKg - row.ConsumedKg);
            var selection = saved.Value.Selection;
            if (rows.Any(row => row.ConsumedKg != 0 || row.ReleasedKg != 0) ||
                remaining.Count != selection.Allocations.Count || selection.Allocations.Any(row =>
                    !remaining.TryGetValue((row.LotId, row.LocationId), out var kg) || kg != row.QuantityKg))
                return new(null, Problem(409, "PLAN_RESERVATION_CHANGED", "The approval no longer holds the saved allocations."));

            var reassessed = await ComputeBlendPlanPreviewAsync(plan.Id, request, http, access, db,
                ledger, residue, applicableMrl, assessmentPolicies, clock, false, Permissions.ContainersWrite, ct,
                selection, remaining);
            if (reassessed is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK } ||
                reassessed is not IValueHttpResult { Value: { } value }) return new(null, reassessed);
            var result = JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result.GetProperty("assessmentStatus").GetString() != "PASS" ||
                result.GetProperty("dependencyRevision").GetString() != saved.Value.DependencyRevision)
                return new(null, Problem(409, "PLAN_PROPOSAL_STALE", "Approval evidence or stock eligibility has changed."));

            var nextVersion = container.Version + 1;
            var updated = await db.Containers.Where(row => row.Id == containerId && row.CompanyId == company &&
                    row.Status == "PLANNED" && row.Version == container.Version)
                .ExecuteUpdateAsync(change => change.SetProperty(row => row.Status, "LOADING")
                    .SetProperty(row => row.Version, nextVersion), ct);
            if (updated != 1)
                return new(null, Problem(409, "PLAN_EXECUTION_STARTED", "The container changed before execution could start."));
            PlanEvent(db, plan, actor, "PLAN_EXECUTION_STARTED", clock.GetUtcNow().ToUniversalTime());
            await db.SaveChangesAsync(ct);

            return new(new BlendExecutionAuthorization(plan.Id, proposalId, expectedDecisionVersion, company,
                plan.OrderId, containerId, nextVersion, request.FacilityId, selection.Mode,
                selection.CompatibilityPolicyId, selection.Allocations.Select(row =>
                    new BlendExecutionReservation(row.LotId, row.LocationId, remaining[(row.LotId, row.LocationId)]))
                    .ToArray()), null);
        }
        catch (PostgresException ex) when (DecisionStorageUnavailable(ex))
        {
            return new(null, DecisionUnavailable());
        }
        catch (JsonException)
        {
            return new(null, InvalidSavedBlendProposal());
        }
    }
}
