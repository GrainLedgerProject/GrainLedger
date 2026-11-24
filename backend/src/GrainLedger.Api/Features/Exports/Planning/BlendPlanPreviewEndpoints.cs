using System.Globalization;
using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrainLedger.Api.Features.Exports;

public sealed record BlendRatioAdjustment(Guid PreviewReference, string FirstQuantityKg);
public sealed record PreviewBlendPlanRequest(Guid FacilityId, string ScopeRevision,
    int MaxIterations = 32, string? Objective = null, BlendRatioAdjustment? Adjustment = null, Guid? MoistureAttributeId = null);

public static partial class ExportModule
{
    private static Task<IResult> PreviewBlendPlanAsync(Guid id,
        PreviewBlendPlanRequest request, HttpContext http,
        ICurrentAccess access, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies, TimeProvider clock,
        CancellationToken ct) => ComputeBlendPlanPreviewAsync(id, request, http, access, db, ledger, residue, applicableMrl, assessmentPolicies,
            clock, true, Permissions.BlendPropose, ct);

    private static async Task<IResult> ComputeBlendPlanPreviewAsync(Guid id,
        PreviewBlendPlanRequest request, HttpContext http,
        ICurrentAccess access, AppDbContext db, LedgerService ledger,
        ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies, TimeProvider clock,
        bool issueReference, string requiredPermission, CancellationToken ct, GreedyStockSelection.Result? savedSelection = null,
        IReadOnlyDictionary<(Guid LotId, Guid LocationId), decimal>? ownReservations = null)
    {
        var scope = await ScopeAsync(http, db, access, requiredPermission, ct);
        if (scope is null) return Results.Forbid();
        if (!await access.HasPermissionAsync(Permissions.ResidueEvaluate, ct)) return Results.Forbid();
        if (request.FacilityId == Guid.Empty || string.IsNullOrWhiteSpace(request.ScopeRevision) ||
            request.MaxIterations is < 1 or > 256 || request.Objective is not
                (null or "MIN_LOCATIONS" or "RETAIN_HEAD_RICE" or "MIN_LOTS" or "MIN_MOISTURE" or "MAX_RESIDUE_HEADROOM"))
            return Invalid(new() { ["request"] = ["Choose a facility, current scope revision, 1–256 iterations and a supported objective."] });
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
            request.FacilityId, db, ledger, ct, withCompatibility: true, ownReservations: ownReservations); }
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
        if (request.Objective == "MIN_MOISTURE" && !limits.Any(limit =>
            limit.AttributeId == request.MoistureAttributeId && limit.Unit == "%"))
            return Invalid(new() { ["moistureAttributeId"] = ["Select the moisture percentage attribute from the order specification."] });
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
            return AssessedMilpSearch.MinimumBlendInput(plan.TargetMassKg, fraction);
        }
        string? passingDependencyRevision = null;
        object? passingEvidenceSnapshot = null;
        IReadOnlyList<BlendQualityObservation> passingQuality = [];
        async Task<BlendAssessmentResult> AssessAsync(GreedyStockSelection.Result selection, CancellationToken token)
        {
            var selected = selection.Allocations.Select(x => new CandidateGroupItem(
                x.LotId, x.LocationId, x.QuantityKg, x.CandidateRevision)).ToArray();
            var yield = selection.CompatibilityPolicyId is { } compatibilityId &&
                yieldByCompatibility.TryGetValue(compatibilityId, out var found) ? found : null;
            var group = CandidateGroupValidator.Validate(snapshot with { ScopeRevision = revision, TargetMassKg = plan.TargetMassKg },
                selected, target, selection.Mode, revision,
                selection.CompatibilityPolicyId, yield is not null);
            if (group.Status == "INVALID") return new BlendAssessmentResult("FAIL", null,
                group.Findings.Select(x => new BlendAssessmentFinding(x, "FAIL", null,
                    null, null, null)).ToArray());
            IReadOnlyList<ResidueResult>? bounds = null;
            IReadOnlyList<ResidueEvaluationPreview> inputEvidence = [];
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
                    outputCommodity, policy.RequiredSubstanceIds, token, requireSourceLab: true);
                evidenceAt = evidence.SnapshotAt;
                inputEvidence = evidence.Inputs;
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
                qualityRules, observations,
                ExportProductMatch.AssessmentResidueScope(snapshot, target, selected, selection.CompatibilityPolicyId));
            if (selection.Mode == "BLEND_INPUT_STOCK")
                assessment = BlendInputResidue.Apply(assessment, BlendInputResidue.Screen(snapshot,
                    selected.Select(x => x.LotId).Distinct().ToArray(), inputEvidence, assessmentRule, mrl));
            if (assessment.Status == "PASS")
            {
                passingDependencyRevision = ExportRules.Hash("blend-assessment-dependencies",
                    new { mrl, policy, yield, bounds, observations });
                passingQuality = observations;
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
        }

        AssessedGreedySearch.Result result;
        AssessedMilpSearch.Result? solve = null;
        BlendInputResidue.Exclusion[] inputResidueExclusions = [];
        var method = "RECHECK";
        var optimal = false;
        var priority = request.Objective switch
        {
            "MIN_LOCATIONS" => BlendPlanningObjective.FewestLocations,
            "RETAIN_HEAD_RICE" => BlendPlanningObjective.RetainHeadRice,
            "MIN_LOTS" => BlendPlanningObjective.FewestLots,
            "MIN_MOISTURE" => BlendPlanningObjective.LowestMoisture,
            "MAX_RESIDUE_HEADROOM" => BlendPlanningObjective.ResidueHeadroom,
            _ => BlendPlanningObjective.None
        };
        AssessedMilpSearch.RatioRange? ratioRange = null;
        if (savedSelection is null && request.Adjustment is { } adjustment)
        {
            var basis = await db.BlendPlanPreviews.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == adjustment.PreviewReference && x.CompanyId == scope.Value.Company &&
                x.ActorId == scope.Value.Actor && x.PlanId == id && x.ExpiresAt > clock.GetUtcNow(), ct);
            if (basis is null)
                return Problem(409, "PLAN_PREVIEW_EXPIRED", "Calculate a new plan before adjusting the ratio.");
            var basisRequest = JsonSerializer.Deserialize<PreviewBlendPlanRequest>(basis.RequestJson);
            var stored = ReadSavedBlendSelection(basis.SnapshotJson);
            using var basisDocument = JsonDocument.Parse(basis.SnapshotJson);
            if (basisRequest?.ScopeRevision != revision || basisRequest.FacilityId != request.FacilityId ||
                basisRequest.Objective != request.Objective || basisRequest.MoistureAttributeId != request.MoistureAttributeId ||
                stored is not { Selection.Allocations.Count: 2 } ||
                !basisDocument.RootElement.TryGetProperty("ratioRange", out var rangeElement) ||
                rangeElement.ValueKind != JsonValueKind.Object)
                return Problem(409, "PLAN_RATIO_STALE", "Calculate a new adjustable plan.");

            ratioRange = JsonSerializer.Deserialize<AssessedMilpSearch.RatioRange>(rangeElement,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (ratioRange is null ||
                !ExportRules.TryMass(adjustment.FirstQuantityKg, "KG", out var firstKg) ||
                !ExportRules.TryMass(ratioRange.MinimumKg, "KG", out var minimum) ||
                !ExportRules.TryMass(ratioRange.MaximumKg, "KG", out var maximum) ||
                firstKg < minimum || firstKg > maximum)
                return Invalid(new() { ["adjustment"] = ["Choose a quantity within the verified ratio range."] });

            var initial = stored.Value.Selection;
            if (firstKg % 1 != 0 || initial.SelectedMassKg % 1 != 0)
                return Invalid(new() { ["adjustment"] = ["Use whole kilograms for both lots when adjusting the ratio."] });
            var adjusted = initial with
            {
                Allocations = [initial.Allocations[0] with { QuantityKg = firstKg },
                    initial.Allocations[1] with { QuantityKg = initial.SelectedMassKg - firstKg }]
            };
            var assessment = await AssessAsync(adjusted, ct);
            method = "RATIO_RECHECK";
            optimal = assessment.Status == "PASS" &&
                (priority is BlendPlanningObjective.FewestLocations or BlendPlanningObjective.FewestLots) &&
                AssessedMilpSearch.ProvesFewestPositions(snapshot, target, InputTarget, adjusted,
                    priority == BlendPlanningObjective.FewestLots);
            result = new(assessment.Status, 1, adjusted, assessment, []);
        }
        else if (savedSelection is null)
        {
            var searchSnapshot = snapshot;
            if (assessmentRule is { RequiredSubstances.Count: > 0 })
            {
                var ids = snapshot.Candidates.Where(c => c.InventoryEligible && c.AvailableKg > 0)
                    .Select(c => c.LotId).Distinct().ToArray();
                if (ids.Length is > 0 and <= 100)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    try
                    {
                        if (target == "BLENDED_RICE")
                        {
                            var compatible = snapshot.Compatibilities.Value.EnumerateArray()
                                .Where(item => ExportProductMatch.TryMatchBlendOutput(item, order.CommodityId,
                                    snapshot.OrderCommodityCode, snapshot.CommodityCodes, out _))
                                .SelectMany(item => item.GetProperty("inputProducts").EnumerateArray().Select(key => key.GetString()))
                                .ToHashSet(StringComparer.Ordinal);
                            var blendIds = snapshot.Candidates.Where(c => c.InventoryEligible && c.AvailableKg > 0 &&
                                (c.Stage is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE") &&
                                compatible.Contains($"{c.LedgerCommodityId}:{c.ProductIdentity}"))
                                .Select(c => c.LotId).Distinct().ToArray();
                            if (blendIds.Length > 0)
                            {
                                var inputs = await residue.PreviewLotsAsync(blendIds, assessmentRule.RequiredSubstances, deadline.Token, requireSourceLab: true);
                                var inputMrl = await applicableMrl.ResolveAsync(order.MarketId, order.CommodityId,
                                    inputs[0].SnapshotAt, deadline.Token);
                                inputResidueExclusions = BlendInputResidue.Screen(snapshot, blendIds, inputs, assessmentRule, inputMrl);
                                var blocked = inputResidueExclusions.Select(x => x.LotId).ToHashSet();
                                searchSnapshot = snapshot with { Candidates = snapshot.Candidates.Where(c => !blocked.Contains(c.LotId)).ToArray() };
                            }
                        }
                        var quality = await ledger.ReadPlanningQualityObservationsAsync(
                            scope.Value.Company, request.FacilityId, ids, snapshot.ObservedAt, deadline.Token);
                        var observations = quality.Select(q => new BlendQualityObservation(q.LotId, q.AttributeId,
                            decimal.Parse(q.MeasuredValue, CultureInfo.InvariantCulture), q.Unit, q.Id, q.VersionNo))
                            .ToArray();
                        var mrl = await applicableMrl.ResolveAsync(order.MarketId, order.CommodityId,
                            snapshot.ObservedAt, deadline.Token);
                        async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResidueResult>>> ReadBounds(
                            Guid[] lotIds, Guid? output, CancellationToken token)
                        {
                            if (output is { } commodityId)
                                return (await residue.PreviewPlanningBlendBoundsAsync(lotIds, commodityId,
                                    assessmentRule.RequiredSubstances, token, requireSourceLab: true)).PlanningBlendBounds;
                            return (await residue.PreviewLotsAsync(lotIds, assessmentRule.RequiredSubstances, token, requireSourceLab: true))
                                .ToDictionary(x => x.LotId, x => x.Results);
                        }

                        solve = await AssessedMilpSearch.SearchAsync(searchSnapshot, target, limits, qualityRules,
                            observations, assessmentRule, mrl, InputTarget, priority, request.MaxIterations,
                            TimeSpan.FromSeconds(3), ReadBounds, AssessAsync, deadline.Token, request.MoistureAttributeId);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                    catch (LedgerException ex) when (ex.StatusCode is 409 or 422 or 503) { }
                }
            }

            GreedyStockSelection.Result? selection = solve?.Selection;
            BlendAssessmentResult? verified = null;
            if (selection is not null)
                verified = await AssessAsync(selection, ct);
            if (selection is not null && verified?.Status == "PASS")
            {
                method = "MILP";
                optimal = solve!.Optimal;
                ratioRange = solve.Range;
                result = new("PASS", solve.Attempts.Count, selection, verified, []);
            }
            else
            {
                method = "GREEDY_FALLBACK";
                result = await AssessedGreedySearch.SearchAsync(searchSnapshot.Candidates, plan.TargetMassKg,
                    target, snapshot.CatalogCommodityId, snapshot.Compatibilities.Value, AssessAsync,
                    request.MaxIterations, ct, InputTarget, snapshot.OrderCommodityCode, snapshot.CommodityCodes);
            }
        }
        else
        {
            var assessment = await AssessAsync(savedSelection, ct);
            result = new(assessment.Status, 1, savedSelection, assessment, []);
        }

        PlanningCandidateSnapshot refreshed;
        try { refreshed = await CandidateSnapshotAsync(plan.ContainerId, scope.Value.Company,
            request.FacilityId, db, ledger, ct, withCompatibility: true, ownReservations: ownReservations); }
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
            ratioRange,
            dependencyRevision = passingDependencyRevision,
            evidence = passingEvidenceSnapshot };
        Guid? reference = null;
        if (issueReference && result.Outcome == "PASS" &&
            result.Assessment?.Status == "PASS" && passingDependencyRevision is not null)
        {
            var ready = await db.Database.SqlQueryRaw<bool>("""
                SELECT to_regclass('grainledger.exports_blend_plan_previews') IS NOT NULL AS "Value"
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
        var verifiedSelection = result.Outcome == "PASS" ? result.Selection : null;
        var positionsUsed = verifiedSelection?.Allocations.Select(x => x.LocationId).Distinct().Count();
        string? headRiceUsedKg = null;
        if (priority == BlendPlanningObjective.RetainHeadRice && verifiedSelection is { } selected)
        {
            var headRiceLots = snapshot.Candidates.Where(c => c.ProductIdentity == "HEAD_RICE")
                .Select(c => c.LotId).ToHashSet();
            headRiceUsedKg = ExportRules.Text(selected.Allocations.Where(a => headRiceLots.Contains(a.LotId))
                .Sum(a => a.QuantityKg));
        }
        var lotsUsed = verifiedSelection?.Allocations.Select(a => a.LotId).Distinct().Count();
        string? weightedMoisture = null;
        if (verifiedSelection is not null && request.MoistureAttributeId is { } attributeId)
        {
            var moistureByLot = passingQuality.Where(q => q.AttributeId == attributeId && q.Unit == "%")
                .ToDictionary(q => q.LotId, q => q.Value);
            if (verifiedSelection.Allocations.All(a => moistureByLot.ContainsKey(a.LotId)))
                weightedMoisture = ExportRules.Text(verifiedSelection.Allocations.Sum(a => a.QuantityKg * moistureByLot[a.LotId]) /
                    verifiedSelection.SelectedMassKg);
        }
        var worstResidue = AssessedMilpSearch.ResidueHeadroom(snapshotResponse.assessmentFindings);
        var residueHeadroom = worstResidue is { } worst ? new { substanceId = worst.Substance, ratio = worst.Ratio } : null;
        object? ratioQuality = null;
        if (ratioRange is not null && verifiedSelection is { Mode: "BLEND_INPUT_STOCK", Allocations.Count: 2 })
        {
            var first = verifiedSelection.Allocations[0].LotId;
            var second = verifiedSelection.Allocations[1].LotId;
            var values = passingQuality.ToDictionary(q => (q.LotId, q.AttributeId));
            ratioQuality = limits.Where(limit => limit.Unit == "%" && values.ContainsKey((first, limit.AttributeId)) &&
                    values.ContainsKey((second, limit.AttributeId)))
                .Select(limit => new { limit.AttributeId, first = ExportRules.Text(values[(first, limit.AttributeId)].Value),
                    second = ExportRules.Text(values[(second, limit.AttributeId)].Value), limit.Unit }).ToArray();
        }
        var substanceIds = assessmentRule?.RequiredSubstances ?? [];
        var substanceNames = issueReference && substanceIds.Count > 0
            ? await db.ActiveSubstances.AsNoTracking().Where(x => substanceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Code + " · " + x.Name, ct)
            : null;
        // Development-only diagnostics stay outside the proposal snapshot and fingerprint.
        var showSteps = issueReference &&
            http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() &&
            http.RequestServices.GetRequiredService<IConfiguration>()
                .GetValue("BlendPlanning:ShowCalculationSteps", true);
        return Results.Ok(new { snapshotResponse.planId, snapshotResponse.scopeRevision,
            snapshotResponse.outcome, snapshotResponse.Iterations,
            snapshotResponse.Reasons, snapshotResponse.selection,
            snapshotResponse.assessmentStatus, snapshotResponse.assessmentFindings,
            snapshotResponse.predictedOutputKg, snapshotResponse.dependencyRevision,
            snapshotResponse.calculationVersion, snapshotResponse.evidence,
            snapshotFingerprint = ExportRules.Hash("blend-plan-preview", snapshotResponse),
            previewReference = reference, canPropose = reference is not null,
            ratioRange = snapshotResponse.ratioRange,
            substanceNames, ratioQuality, residueHeadroom, inputResidueExclusions,
            optimization = issueReference ? new { method, objective = request.Objective, positionsUsed, lotsUsed,
                headRiceUsedKg, weightedMoisture, optimal } : null,
            calculationSteps = showSteps ? new { method, attempts = solve?.Attempts ?? [] } : null });
    }
}
