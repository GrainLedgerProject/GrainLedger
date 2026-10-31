using System.Globalization;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

public sealed record PreviewBlendPlanRequest(Guid FacilityId, string ScopeRevision,
    int MaxIterations = 32);

public static partial class ExportModule
{
    private static Task<IResult> PreviewBlendPlanAsync(Guid id,
        PreviewBlendPlanRequest request, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies, TimeProvider clock,
        CancellationToken ct) => ComputeBlendPlanPreviewAsync(id, request, http,
            identity, access, db, catalog, ledger, residue, applicableMrl, assessmentPolicies,
            clock, true, Permissions.BlendPropose, ct);

    private static async Task<IResult> ComputeBlendPlanPreviewAsync(Guid id,
        PreviewBlendPlanRequest request, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies, TimeProvider clock,
        bool issueReference, string requiredPermission, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, requiredPermission, ct);
        if (scope is null) return Results.Forbid();
        if (!await access.HasPermissionAsync(Permissions.ResidueEvaluate, ct)) return Results.Forbid();
        if (request.FacilityId == Guid.Empty || string.IsNullOrWhiteSpace(request.ScopeRevision) ||
            request.MaxIterations is < 1 or > 256)
            return Invalid(new() { ["request"] = ["Choose a facility, current scope revision and 1–256 iterations."] });
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (plan is null) return Results.NotFound();
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        if (plan.Status != "DRAFT" || !DemandIsCurrent(plan, order, container))
            return Problem(409, "PLAN_DEMAND_STALE", "Create a current draft before previewing a blend.");
        var target = TargetProductFromSnapshot(plan.DemandSnapshot);
        if (string.IsNullOrWhiteSpace(target))
            return Problem(409, "PLAN_TARGET_MISSING", "Create a draft with a target product.");
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(plan.ContainerId, scope.Value.Company,
            request.FacilityId, db, catalog, ledger, ct, withCompatibility: true); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException)
        { return CandidateFailure(ex); }
        var revision = PlanScopeRevision(snapshot, plan, target);
        if (revision != request.ScopeRevision)
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Reload the candidates before previewing.");
        if (snapshot.Compatibilities is null)
            return Problem(503, "COMPATIBILITY_READ_NOT_READY", "Planning compatibility data is unavailable.");

        PublishedBlendAssessmentPolicy? policy;
        try { policy = await assessmentPolicies.ResolveAsync(scope.Value.Company,
            order.MarketId, order.CommodityId, snapshot.ObservedAt, ct); }
        catch (BlendAssessmentPolicyUnavailableException) { policy = null; }
        var qualityRules = policy?.QualityRules.Select(x => new BlendQualityRule(policy.Id,
            policy.VersionNo, x.AttributeId, x.Unit, x.Calculation,
            policy.EvidenceReference)).ToArray() ?? [];
        var assessmentRule = policy is null ? null : new BlendAssessmentRule(policy.Id,
            policy.VersionNo, policy.MarketId, policy.CommodityId,
            policy.RequiredSubstanceIds, decimal.Parse(policy.MrlMarginFraction,
                CultureInfo.InvariantCulture), policy.ResidueBasis, policy.EvidenceReference);
        var limits = await db.Limits.AsNoTracking().Where(x =>
            x.SpecificationId == plan.SpecificationId).ToArrayAsync(ct);
        var yieldByCompatibility = new Dictionary<Guid, PlanningYieldRule?>();
        foreach (var compatibility in snapshot.Compatibilities.Value.EnumerateArray())
        {
            var compatibilityId = compatibility.GetProperty("id").GetGuid();
            try { yieldByCompatibility[compatibilityId] = await ledger.ReadPlanningYieldRuleAsync(
                scope.Value.Company, request.FacilityId, compatibilityId,
                snapshot.ObservedAt, ct); }
            catch (LedgerException ex) when (ex.StatusCode == 503)
            { yieldByCompatibility[compatibilityId] = null; }
        }
        decimal? InputTarget(Guid compatibilityId)
        {
            if (!yieldByCompatibility.TryGetValue(compatibilityId, out var rule) || rule is null ||
                !decimal.TryParse(rule.OutputFraction, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var fraction) || fraction is <= 0 or > 1)
                return null;
            try { return decimal.Ceiling(plan.TargetMassKg / fraction * 1000000m) / 1000000m; }
            catch (OverflowException) { return null; }
        }
        string? passingDependencyRevision = null;
        object? passingEvidenceSnapshot = null;
        var result = await AssessedGreedySearch.SearchAsync(snapshot.Candidates,
            plan.TargetMassKg, target, snapshot.CatalogCommodityId,
            snapshot.Compatibilities.Value, async (selection, token) =>
            {
                var selected = selection.Allocations.Select(x => new CandidateGroupItem(
                    x.LotId, x.LocationId, x.QuantityKg, x.CandidateRevision)).ToArray();
                var yield = selection.CompatibilityPolicyId is { } compatibilityId &&
                    yieldByCompatibility.TryGetValue(compatibilityId, out var found) ? found : null;
                var group = CandidateGroupValidator.Validate(snapshot with { ScopeRevision = revision },
                    selected, target, selection.Mode, revision,
                    selection.CompatibilityPolicyId, yield is not null);
                if (group.Status == "INVALID") return new BlendAssessmentResult("FAIL", null,
                    group.Findings.Select(x => new BlendAssessmentFinding(x, "FAIL", null,
                        null, null, null)).ToArray());
                IReadOnlyList<ResidueResult>? bounds = null;
                DateTimeOffset evidenceAt = snapshot.ObservedAt;
                if (policy is { RequiredSubstanceIds.Length: > 0 })
                {
                    Guid? outputCommodity = null;
                    if (selection.CompatibilityPolicyId is { } selectedPolicy)
                    {
                        var matching = snapshot.Compatibilities.Value.EnumerateArray()
                            .Single(x => x.GetProperty("id").GetGuid() == selectedPolicy);
                        if (matching.TryGetProperty("outputLedgerCommodityId", out var value) &&
                            value.ValueKind == System.Text.Json.JsonValueKind.String)
                            outputCommodity = value.GetGuid();
                    }
                    var evidence = await residue.PreviewSelectionAsync(selected.GroupBy(x => x.LotId)
                        .Select(x => new HypotheticalBlendInput(x.Key,
                            ExportRules.Text(x.Sum(y => y.QuantityKg)))).ToArray(),
                        outputCommodity, policy.RequiredSubstanceIds, token);
                    evidenceAt = evidence.SnapshotAt;
                    bounds = selection.Mode == "DIRECT_STOCK"
                        ? DirectStockResidueAssessment.ConservativeBounds(evidence.Inputs,
                            policy.RequiredSubstanceIds)
                        : evidence.HypotheticalBlend?.Results;
                }
                var mrl = await applicableMrl.ResolveAsync(order.MarketId,
                    order.CommodityId, evidenceAt, token);
                var yieldRule = yield is null ? null : new BlendYieldRule(yield.Id,
                    yield.VersionNo, decimal.Parse(yield.OutputFraction,
                        CultureInfo.InvariantCulture), yield.EvidenceId.ToString());
                IReadOnlyList<BlendQualityObservation> observations;
                try
                {
                    var quality = await ledger.ReadPlanningQualityObservationsAsync(
                        scope.Value.Company, request.FacilityId,
                        selected.Select(x => x.LotId).Distinct().ToArray(), evidenceAt, token);
                    observations = quality.Select(x => new BlendQualityObservation(x.LotId,
                        x.AttributeId, decimal.Parse(x.MeasuredValue,
                            CultureInfo.InvariantCulture), x.Unit, x.Id, x.VersionNo)).ToArray();
                }
                catch (LedgerException ex) when (ex.StatusCode == 503)
                {
                    observations = [];
                }
                var assessment = BlendAssessment.Evaluate(selection.Mode, plan.TargetMassKg,
                    selected, yieldRule, assessmentRule, mrl, bounds, limits,
                    qualityRules, observations);
                if (assessment.Status == "PASS")
                {
                    passingDependencyRevision = ExportRules.Hash("blend-assessment-dependencies",
                        new { mrl, policy, yield, bounds, observations });
                    passingEvidenceSnapshot = new
                    {
                        applicableMrl = mrl,
                        assessmentPolicy = policy,
                        outputYieldRule = yield,
                        residueBounds = bounds,
                        qualityObservations = observations
                    };
                }
                return assessment;
            }, request.MaxIterations, ct, InputTarget, snapshot.OrderCommodityCode);

        PlanningCandidateSnapshot refreshed;
        try { refreshed = await CandidateSnapshotAsync(plan.ContainerId, scope.Value.Company,
            request.FacilityId, db, catalog, ledger, ct, withCompatibility: true); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException)
        { return CandidateFailure(ex); }
        var currentPlan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var currentContainer = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        var currentOrder = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        if (PlanScopeRevision(refreshed, plan, target) != revision ||
            currentPlan.Version != plan.Version || currentPlan.Status != "DRAFT" ||
            !DemandIsCurrent(currentPlan, currentOrder, currentContainer))
            return Problem(409, "PLANNING_INPUT_STALE", "Planning inputs changed. Reload the draft and candidates.");
        var snapshotResponse = new { planId = id, scopeRevision = revision,
            outcome = result.Outcome, result.Iterations, result.Reasons,
            selection = result.Selection?.ToResponse(),
            assessmentStatus = result.Assessment?.Status ?? "INCOMPLETE",
            assessmentFindings = result.Assessment?.Findings ?? [],
            predictedOutputKg = result.Assessment?.PredictedOutputKg is { } kg ?
                ExportRules.Text(kg) : null,
            calculationVersion = "F14_ASSESSMENT_V1",
            dependencyRevision = passingDependencyRevision,
            evidence = passingEvidenceSnapshot };
        Guid? reference = null;
        if (issueReference && result.Outcome == "PASS" &&
            result.Assessment?.Status == "PASS" && passingDependencyRevision is not null)
        {
            var ready = await db.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('exports.blend_plan_previews') IS NOT NULL AS "Value"
                """).SingleAsync(ct);
            if (!ready) return Problem(503, "PROPOSAL_SCHEMA_NOT_READY",
                "Apply reviewed Export proposal migration and runtime grants.");
            var now = clock.GetUtcNow();
            var preview = new BlendPlanPreview
            {
                Id = Guid.NewGuid(), CompanyId = scope.Value.Company, PlanId = id,
                ActorId = scope.Value.Actor,
                RequestJson = JsonSerializer.Serialize(request),
                SnapshotJson = JsonSerializer.Serialize(snapshotResponse,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Fingerprint = ExportRules.Hash("blend-plan-preview", snapshotResponse),
                CreatedAt = now, ExpiresAt = now.AddMinutes(10)
            };
            db.BlendPlanPreviews.Add(preview);
            await db.SaveChangesAsync(ct);
            reference = preview.Id;
        }
        return Results.Ok(new { snapshotResponse.planId, snapshotResponse.scopeRevision,
            snapshotResponse.outcome, snapshotResponse.Iterations,
            snapshotResponse.Reasons, snapshotResponse.selection,
            snapshotResponse.assessmentStatus, snapshotResponse.assessmentFindings,
            snapshotResponse.predictedOutputKg, snapshotResponse.dependencyRevision,
            snapshotResponse.calculationVersion, snapshotResponse.evidence,
            snapshotFingerprint = ExportRules.Hash("blend-plan-preview", snapshotResponse),
            previewReference = reference, canPropose = reference is not null });
    }
}
