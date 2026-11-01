using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

internal sealed record PlanningCandidate(Guid LotId, string LotCode, Guid LocationId, string LocationName,
    Guid LedgerCommodityId, Guid CatalogCommodityId, string Stage, string ProductIdentity, decimal OnHandKg, decimal AvailableKg, string StockKind,
    bool IsCurrentPool, bool CustodyDerived, string? PoolRevision, string CandidateRevision,
    int SourceCount, bool LineageComplete, bool InventoryEligible, string[] Restrictions, string[] Reasons,
    string? CatalogCommodityCode = null)
{
    public object ToResponse(string productCompatibility = "NOT_EVALUATED") => new
    {
        lotId = LotId, lotCode = LotCode, locationId = LocationId, locationName = LocationName,
        catalogCommodityId = CatalogCommodityId, stage = Stage, productIdentity = ProductIdentity, onHandKg = ExportRules.Text(OnHandKg),
        availableKg = ExportRules.Text(AvailableKg), stockKind = StockKind, isCurrentPool = IsCurrentPool,
        custodyDerived = CustodyDerived, poolRevision = PoolRevision, candidateRevision = CandidateRevision,
        sourceCount = SourceCount, lineageComplete = LineageComplete, inventoryEligible = InventoryEligible,
        restrictions = Restrictions, reasons = Reasons, productCompatibility,
        complianceStatus = "NOT_ASSESSED"
    };
}

internal sealed record PlanningCandidateSnapshot(Guid ContainerId, Guid FacilityId, int OrderVersion,
    int ContainerVersion, string ContainerStatus, Guid CatalogCommodityId, decimal TargetMassKg, DateTimeOffset ObservedAt,
    string ScopeRevision, JsonElement Ledger, JsonElement? Compatibilities,
    IReadOnlyList<PlanningCandidate> Candidates, string? OrderCommodityCode = null);

internal sealed class CandidateDemandChangedException : Exception;

internal sealed record PlanResiduePosition(Guid LotId, Guid LocationId, string QuantityKg,
    string CandidateRevision);
internal sealed record PlanResiduePreviewRequest(Guid FacilityId, string ScopeRevision,
    string Mode, Guid? CompatibilityPolicyId, PlanResiduePosition[] Selected, Guid[] SubstanceIds);

public static partial class ExportModule
{
    private static async Task<IResult> PlanResiduePreviewAsync(Guid id, PlanResiduePreviewRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db,
        CatalogDbContext catalog, LedgerService ledger, ResidueEvaluationService residue, ApplicableMrlRead applicableMrl,
        BlendAssessmentPolicyRead assessmentPolicies,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (request.FacilityId == Guid.Empty || request.Selected is null or { Length: < 1 or > 100 } ||
            request.SubstanceIds is null or { Length: < 1 or > 200 } ||
            request.SubstanceIds.Contains(Guid.Empty) ||
            request.SubstanceIds.Distinct().Count() != request.SubstanceIds.Length)
            return Invalid(new() { ["request"] = ["Choose a facility, 1–100 positions and 1–200 distinct substances."] });
        var selected = new List<CandidateGroupItem>(request.Selected.Length);
        foreach (var item in request.Selected)
        {
            if (item.LotId == Guid.Empty || item.LocationId == Guid.Empty ||
                !ExportRules.TryMass(item.QuantityKg, "KG", out var kg))
                return Invalid(new() { ["selected"] = ["Each position needs a lot, location and exact positive kg string."] });
            selected.Add(new CandidateGroupItem(item.LotId, item.LocationId, kg, item.CandidateRevision));
        }
        var plan = await db.BlendPlanDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (plan is null) return Results.NotFound();
        var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        if (plan.Status != "DRAFT" || !DemandIsCurrent(plan, order, container))
            return Problem(409, "PLAN_DEMAND_STALE", "Create a current draft before previewing candidates.");
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(plan.ContainerId, scope.Value.Company,
            request.FacilityId, db, catalog, ledger, ct, withCompatibility: true); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException)
        { return CandidateFailure(ex); }
        var target = TargetProductFromSnapshot(plan.DemandSnapshot);
        if (string.IsNullOrWhiteSpace(target))
            return Problem(409, "PLAN_TARGET_MISSING", "Create a draft with a target product.");
        var planRevision = PlanScopeRevision(snapshot, plan, target);
        var group = CandidateGroupValidator.Validate(snapshot with { ScopeRevision = planRevision }, selected, target,
            request.Mode, request.ScopeRevision, request.CompatibilityPolicyId);
        if (group.Findings.Contains("CANDIDATE_SNAPSHOT_STALE"))
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate inventory or blend policy changed. Reload candidates.");
        if (group.Status == "INVALID")
            return Results.Ok(new { status = group.Status, group.Findings, residue = Array.Empty<object>(),
                assessmentStatus = "NOT_ASSESSED" });
        Guid? outputLedgerCommodityId = null;
        if (request.Mode == "BLEND_INPUT_STOCK" && request.CompatibilityPolicyId is { } policyId &&
            snapshot.Compatibilities is { } policies)
        {
            var policy = policies.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == policyId);
            if (policy.ValueKind != JsonValueKind.Undefined &&
                policy.TryGetProperty("outputLedgerCommodityId", out var output) &&
                output.ValueKind == JsonValueKind.String && output.TryGetGuid(out var outputId))
                outputLedgerCommodityId = outputId;
        }
        var residueSelection = await residue.PreviewSelectionAsync(
            selected.GroupBy(x => x.LotId).Select(x => new HypotheticalBlendInput(
                x.Key, ExportRules.Text(x.Sum(y => y.QuantityKg)))).ToArray(),
            outputLedgerCommodityId, request.SubstanceIds, ct);
        PlanningYieldRule? publishedYield = null;
        if (request.Mode == "BLEND_INPUT_STOCK" && request.CompatibilityPolicyId is { } yieldCompatibility)
        {
            try { publishedYield = await ledger.ReadPlanningYieldRuleAsync(scope.Value.Company,
                request.FacilityId, yieldCompatibility, residueSelection.SnapshotAt, ct); }
            catch (LedgerException ex) when (ex.StatusCode == 503) { /* Unapplied optional policy schema. */ }
        }
        if (publishedYield is not null)
            group = CandidateGroupValidator.Validate(snapshot with { ScopeRevision = planRevision },
                selected, target, request.Mode, request.ScopeRevision,
                request.CompatibilityPolicyId, outputYieldPolicyAvailable: true);
        var previews = residueSelection.Inputs;
        var hypothetical = residueSelection.HypotheticalBlend;
        var mrl = await applicableMrl.ResolveAsync(order.MarketId, order.CommodityId,
            residueSelection.SnapshotAt, ct);
        PublishedBlendAssessmentPolicy? publishedPolicy;
        try { publishedPolicy = await assessmentPolicies.ResolveAsync(scope.Value.Company,
            order.MarketId, order.CommodityId, residueSelection.SnapshotAt, ct); }
        catch (BlendAssessmentPolicyUnavailableException) { publishedPolicy = null; }
        PlanningCandidateSnapshot refreshed;
        try { refreshed = await CandidateSnapshotAsync(plan.ContainerId, scope.Value.Company,
            request.FacilityId, db, catalog, ledger, ct, withCompatibility: true); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException)
        { return CandidateFailure(ex); }
        if (PlanScopeRevision(refreshed, plan, target) != planRevision)
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate inventory changed. Reload candidates.");
        var currentPlan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == plan.Id, ct);
        var currentContainer = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == plan.ContainerId, ct);
        var currentOrder = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == plan.OrderId, ct);
        if (currentPlan.Status != "DRAFT" || currentPlan.Version != plan.Version ||
            !DemandIsCurrent(currentPlan, currentOrder, currentContainer))
            return Problem(409, "PLAN_DEMAND_STALE", "Draft or demand changed during the preview.");
        var unresolved = previews.SelectMany(x => x.Results.Where(r => r.Status != "COMPLETE")
            .Select(r => new { x.LotId, r.SubstanceId, r.Reasons })).ToArray();
        var blendUnresolved = hypothetical?.Results.Where(x => x.Status != "COMPLETE")
            .Select(x => new { x.SubstanceId, x.Reasons }).ToArray();
        var requestedLimits = mrl.Limits.Where(x => request.SubstanceIds.Contains(x.SubstanceId))
            .Select(x => new { x.SubstanceId, limitValue = ExportRules.Text(x.LimitValue), x.Unit }).ToArray();
        var specification = await db.Limits.AsNoTracking().Where(x =>
            x.SpecificationId == plan.SpecificationId).ToArrayAsync(ct);
        var margin = publishedPolicy is null ? (decimal?)null :
            decimal.Parse(publishedPolicy.MrlMarginFraction, CultureInfo.InvariantCulture);
        var assessmentRule = publishedPolicy is null ? null : new BlendAssessmentRule(
            publishedPolicy.Id, publishedPolicy.VersionNo, publishedPolicy.MarketId,
            publishedPolicy.CommodityId, publishedPolicy.RequiredSubstanceIds,
            margin!.Value, publishedPolicy.ResidueBasis, publishedPolicy.EvidenceReference);
        var qualityRules = publishedPolicy?.QualityRules.Select(x => new BlendQualityRule(
            publishedPolicy.Id, publishedPolicy.VersionNo, x.AttributeId, x.Unit,
            x.Calculation, publishedPolicy.EvidenceReference)).ToArray() ?? [];
        var yieldRule = publishedYield is null ? null : new BlendYieldRule(publishedYield.Id,
            publishedYield.VersionNo, decimal.Parse(publishedYield.OutputFraction,
                CultureInfo.InvariantCulture), publishedYield.EvidenceId.ToString());
        // Confirmed per-lot quality observations remain unavailable. Even
        // published yield and coverage policies cannot authorize PASS alone.
        var assessment = BlendAssessment.Evaluate(request.Mode, plan.TargetMassKg, selected,
            yieldRule, assessmentRule, mrl, hypothetical?.Results, specification, qualityRules, []);
        return Results.Ok(new { status = group.Status, group.Findings,
            inputMassKg = ExportRules.Text(group.InputMassKg), scopeRevision = planRevision,
            residueSnapshotAt = residueSelection.SnapshotAt, residue = previews,
            evidenceStatus = unresolved.Length == 0 ? "INPUT_BOUNDS_AVAILABLE" : "INCOMPLETE",
            unresolved, hypotheticalBlend = hypothetical,
            hypotheticalBlendStatus = hypothetical is null ? "INCOMPLETE" :
                blendUnresolved!.Length == 0 ? "BOUNDS_AVAILABLE" : "INCOMPLETE",
            blendUnresolved,
            applicableMrl = new { mrl.Status, mrl.VersionId, mrl.VersionNo,
                mrl.PublishedAt, mrl.EffectiveFrom, mrl.EffectiveTo,
                mrl.SourceRef, mrl.VersionToken, decisionAt = residueSelection.SnapshotAt,
                publishedLimitCount = mrl.Limits.Count, requestedLimits },
            assessmentPolicy = publishedPolicy is null ? null : new {
                publishedPolicy.Id, publishedPolicy.VersionNo, publishedPolicy.EffectiveFrom,
                publishedPolicy.EffectiveTo, publishedPolicy.EvidenceReference },
            outputYieldPolicy = publishedYield is null ? null : new {
                publishedYield.Id, publishedYield.VersionNo, publishedYield.OutputFraction,
                publishedYield.EvidenceId, publishedYield.EffectiveFrom, publishedYield.EffectiveTo },
            assessmentStatus = assessment.Status, assessmentFindings = assessment.Findings,
            predictedOutputKg = assessment.PredictedOutputKg is { } outputKg ?
                ExportRules.Text(outputKg) : null });
    }

    private static string CandidateHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string PlanScopeRevision(PlanningCandidateSnapshot snapshot,
        BlendPlanDraft plan, string targetProductIdentity) => CandidateHash(
            snapshot.ScopeRevision + ":" + plan.Id + ":" + targetProductIdentity + ":" +
            snapshot.Compatibilities!.Value.GetRawText());

    private static decimal LedgerMass(JsonElement value) =>
        decimal.Parse(value.GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    // Ledger compatibility is an exact set rule for a physical BLENDING operation.
    // One candidate can only be a possible input; approval requires the full set.
    private static string ProductCompatibility(PlanningCandidate candidate, string target,
        Guid outputCatalogCommodityId, string? outputCommodityCode, JsonElement policies)
    {
        if (ExportProductMatch.IsDirect(candidate, target, outputCatalogCommodityId, outputCommodityCode))
            return "DIRECT_MATCH";
        if (target != "BLENDED_RICE" || candidate.Stage is not ("HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE"))
            return "PRODUCT_MISMATCH";
        var key = candidate.LedgerCommodityId.ToString() + ":" + candidate.ProductIdentity;
        return policies.EnumerateArray().Any(policy =>
            policy.GetProperty("outputCatalogCommodityId").ValueKind == JsonValueKind.String &&
            policy.GetProperty("outputCatalogCommodityId").GetGuid() == outputCatalogCommodityId &&
            policy.GetProperty("inputProducts").EnumerateArray().Any(input => input.GetString() == key))
            ? "POSSIBLE_BLEND_INPUT" : "BLEND_POLICY_MISSING";
    }

    private static (HashSet<Guid> Nodes, List<JsonElement> Edges, bool Complete) Ancestors(
        Guid lotId, Dictionary<Guid, List<JsonElement>> byOutput, HashSet<Guid> knownNodes)
    {
        var nodes = new HashSet<Guid>();
        var edges = new List<JsonElement>();
        var active = new HashSet<Guid>();
        var complete = true;
        void Visit(Guid id, int depth)
        {
            if (depth > 128 || !knownNodes.Contains(id) || !active.Add(id)) { complete = false; return; }
            if (!nodes.Add(id)) { active.Remove(id); return; }
            if (byOutput.TryGetValue(id, out var incoming))
            {
                foreach (var edge in incoming)
                {
                    edges.Add(edge);
                    if (edge.GetProperty("kind").GetString() == "PROCESSING_UNATTRIBUTED" ||
                        edge.GetProperty("eventId").ValueKind == JsonValueKind.Null) complete = false;
                    Visit(edge.GetProperty("inputLotId").GetGuid(), depth + 1);
                }
            }
            active.Remove(id);
        }
        Visit(lotId, 0);
        return (nodes, edges, complete);
    }

    private static async Task<PlanningCandidateSnapshot> CandidateSnapshotAsync(Guid id, Guid company,
        Guid facilityId, ExportDbContext db, CatalogDbContext catalog, LedgerService ledger, CancellationToken ct,
        bool withCompatibility = false)
    {
        var container = await db.Containers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == company, ct);
        if (container is null) throw new KeyNotFoundException();
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        var orderCommodityCode = await catalog.Commodities.AsNoTracking()
            .Where(x => x.Id == order.CommodityId).Select(x => x.Code).SingleAsync(ct);
        var (data, compatibilities) = withCompatibility
            ? await ledger.ReadPlanningCandidatesWithCompatibilityAsync(company, facilityId, ct)
            : (await ledger.ReadPlanningCandidatesAsync(company, facilityId, ct), (JsonElement?)null);
        var current = await db.Containers.AsNoTracking().Where(x => x.Id == id && x.CompanyId == company)
            .Select(x => new { x.Version, x.Status, x.TargetMassKg }).SingleAsync(ct);
        var currentOrder = await db.Orders.AsNoTracking().Where(x => x.Id == container.OrderId)
            .Select(x => new { x.Version, x.CommodityId }).SingleAsync(ct);
        if (current.Version != container.Version || current.Status != container.Status ||
            current.TargetMassKg != container.TargetMassKg || currentOrder.Version != order.Version ||
            currentOrder.CommodityId != order.CommodityId)
            throw new CandidateDemandChangedException();
        var positions = data.GetProperty("positions").EnumerateArray().ToArray();
        var commodityIds = positions.Select(x => x.GetProperty("catalogCommodityId"))
            .Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetGuid())
            .Distinct().ToArray();
        var commodityCodes = await catalog.Commodities.AsNoTracking()
            .Where(x => commodityIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var allNodes = data.GetProperty("nodes").EnumerateArray().Select(x => x.GetProperty("lotId").GetGuid()).ToHashSet();
        var allEdges = data.GetProperty("edges").EnumerateArray().ToArray();
        var byOutput = allEdges.GroupBy(x => x.GetProperty("outputLotId").GetGuid())
            .ToDictionary(x => x.Key, x => x.ToList());
        var compositions = data.GetProperty("composition").EnumerateArray()
            .GroupBy(x => x.GetProperty("lotId").GetGuid()).ToDictionary(x => x.Key, x => x.ToArray());
        var nodeOperations = data.GetProperty("nodes").EnumerateArray()
            .Where(x => x.GetProperty("operationId").ValueKind != JsonValueKind.Null)
            .ToDictionary(x => x.GetProperty("lotId").GetGuid(), x => x.GetProperty("operationId").GetGuid());
        var operationStatuses = data.GetProperty("operations").EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetGuid(), x => x.GetProperty("status").GetString()!);

        // Exclude the observation time and pagination from the canonical fingerprint.
        // The Ledger arrays are ordered by stable IDs; the position projection strips
        // the unordered display-only positions array from lot_json.
        var canonicalPositions = positions.Select(p => new
        {
            lotId = p.GetProperty("id").GetGuid(), locationId = p.GetProperty("locationId").GetGuid(),
            onHand = p.GetProperty("onHandKg").GetString(),
            available = p.GetProperty("positions").EnumerateArray()
                .FirstOrDefault(x => x.GetProperty("locationId").GetGuid() == p.GetProperty("locationId").GetGuid())
                .GetProperty("availableKg").GetString(),
            mapping = p.GetProperty("catalogCommodityId"), restrictions = p.GetProperty("restrictions"),
            poolRevision = p.GetProperty("positionPoolRevision"), currentPool = p.GetProperty("isCurrentPoolAtLocation"),
            cleared = p.GetProperty("locationCleared"), owner = p.GetProperty("locationOwnerId")
        }).OrderBy(x => x.lotId).ThenBy(x => x.locationId).ToArray();
        var state = JsonSerializer.Serialize(new { orderVersion = order.Version, containerVersion = container.Version, order.CommodityId,
            orderCommodityCode, commodityCodes = commodityCodes.OrderBy(x => x.Key).ToArray(),
            container.TargetMassKg, facilityId, positions = canonicalPositions,
            nodes = data.GetProperty("nodes"), edges = data.GetProperty("edges"),
            composition = data.GetProperty("composition"), operations = data.GetProperty("operations") });
        var revision = CandidateHash(state);
        var candidates = new List<PlanningCandidate>();
        foreach (var p in positions)
        {
            var lotId = p.GetProperty("id").GetGuid();
            var locationId = p.GetProperty("locationId").GetGuid();
            var position = p.GetProperty("positions").EnumerateArray()
                .FirstOrDefault(x => x.GetProperty("locationId").GetGuid() == locationId);
            if (position.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("Ledger candidate position is missing from lot inventory.");
            var onHand = LedgerMass(p.GetProperty("onHandKg"));
            var available = LedgerMass(position.GetProperty("availableKg"));
            var restrictions = p.GetProperty("restrictions").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var composition = compositions.GetValueOrDefault(lotId, []);
            var ancestry = Ancestors(lotId, byOutput, allNodes);
            var uncommittedAncestry = ancestry.Nodes.Any(x => nodeOperations.TryGetValue(x, out var operationId) &&
                (!operationStatuses.TryGetValue(operationId, out var status) || status != "COMMITTED"));
            var lineageComplete = ancestry.Complete && composition.Length > 0 &&
                !uncommittedAncestry &&
                composition.All(x => x.GetProperty("captureSourceLotId").ValueKind != JsonValueKind.Null &&
                    ancestry.Nodes.Contains(x.GetProperty("ledgerSourceLotId").GetGuid()));
            var reasons = new List<string>();
            if (available <= 0) reasons.Add("NO_AVAILABLE_STOCK");
            foreach (var restriction in restrictions)
                reasons.Add(restriction switch
                {
                    "HOLD" => "HOLD_ACTIVE", "CORRECTION" => "CORRECTION_BLOCKED",
                    "RECEIVING_DISCREPANCY" => "RECEIVING_RESTRICTED",
                    "SUPERSEDED" => "SUPERSEDED_LOT", _ => "LINEAGE_INCOMPLETE"
                });
            // Ledger `cleared` means a mixed-bulk location was cleaned before
            // ingress. It is false while an active pool holds stock.
            if (p.GetProperty("locationOwnerId").ValueKind != JsonValueKind.Null &&
                p.GetProperty("locationOwnerId").GetGuid() != company) reasons.Add("LOCATION_INACTIVE");
            if (p.GetProperty("locationMixedBulk").GetBoolean() &&
                !p.GetProperty("isCurrentPoolAtLocation").GetBoolean()) reasons.Add("POOL_GENERATION_CHANGED");
            if (p.GetProperty("catalogCommodityId").ValueKind == JsonValueKind.Null)
                reasons.Add("COMMODITY_MAPPING_MISSING");
            if (composition.Any(x => x.GetProperty("captureSourceLotId").ValueKind == JsonValueKind.Null))
                reasons.Add("SOURCE_MAPPING_MISSING");
            var positivePositions = p.GetProperty("positions").EnumerateArray()
                .Count(x => LedgerMass(x.GetProperty("quantityKg")) > 0);
            if (positivePositions > 1 && composition.Length > 1)
                reasons.Add("POSITION_ATTRIBUTION_UNRESOLVED");
            if (!lineageComplete) reasons.Add("LINEAGE_INCOMPLETE");
            var isCurrent = p.GetProperty("isCurrentPoolAtLocation").GetBoolean();
            var custodyDerived = p.GetProperty("custodyDerived").GetBoolean();
            var kind = isCurrent ? "CURRENT_POOL" : custodyDerived ? "CUSTODY_DERIVED" : "ORDINARY";
            candidates.Add(new PlanningCandidate(lotId, p.GetProperty("code").GetString()!, locationId,
                p.GetProperty("locationName").GetString()!, p.GetProperty("commodityId").GetGuid(),
                p.GetProperty("catalogCommodityId").ValueKind == JsonValueKind.Null
                    ? Guid.Empty : p.GetProperty("catalogCommodityId").GetGuid(),
                p.GetProperty("stage").GetString()!,
                p.GetProperty("productIdentity").GetString()!, onHand, available, kind, isCurrent,
                custodyDerived, isCurrent ? p.GetProperty("positionPoolRevision").GetString() : null,
                CandidateHash(revision + ":" + lotId + ":" + locationId), composition.Length,
                lineageComplete, reasons.Count == 0, restrictions, reasons.Distinct().ToArray(),
                commodityCodes.GetValueOrDefault(p.GetProperty("catalogCommodityId").ValueKind == JsonValueKind.Null
                    ? Guid.Empty : p.GetProperty("catalogCommodityId").GetGuid())));
        }
        // Use the database clock for point-in-time evidence reads. An API host
        // clock a few milliseconds behind PostgreSQL can otherwise miss a
        // quality confirmation committed immediately before this request.
        var observedAt = await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        return new PlanningCandidateSnapshot(id, facilityId, order.Version, container.Version, container.Status,
            order.CommodityId, container.TargetMassKg, observedAt, revision, data,
            compatibilities, candidates, orderCommodityCode);
    }

    private static IResult CandidateFailure(Exception ex) => ex switch
    {
        KeyNotFoundException => Results.NotFound(),
        CandidateDemandChangedException => Problem(409, "EXPORT_DEMAND_STALE", "Order or container changed during the inventory read. Reload the demand."),
        LedgerException ledger => Problem(ledger.StatusCode,
            ledger.StatusCode == 422 ? "CANDIDATE_SCOPE_LIMIT_EXCEEDED" :
            ledger.StatusCode == 503 ? "CANDIDATE_SCHEMA_NOT_READY" : "CANDIDATE_READ_FAILED", ledger.Message),
        _ => throw ex
    };

    private static async Task<IResult> CandidatesAsync(Guid id, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog, LedgerService ledger, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!Guid.TryParse(http.Request.Query["facilityId"], out var facilityId) || facilityId == Guid.Empty ||
            !Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size) ||
            http.Request.Query.ContainsKey("includeExcluded") &&
            !bool.TryParse(http.Request.Query["includeExcluded"], out _) ||
            http.Request.Query.ContainsKey("productMatchOnly") &&
            !bool.TryParse(http.Request.Query["productMatchOnly"], out _) ||
            http.Request.Query.ContainsKey("planId") &&
            (!Guid.TryParse(http.Request.Query["planId"], out var parsedPlanId) || parsedPlanId == Guid.Empty))
            return Invalid(new() { ["query"] = ["Choose a facility and valid pagination/filter values."] });
        Guid? planId = Guid.TryParse(http.Request.Query["planId"], out var requestedPlanId)
            ? requestedPlanId : null;
        string? targetProductIdentity = null;
        BlendPlanDraft? plan = null;
        if (planId is not null)
        {
            if (!await PlanningReadyAsync(db, ct))
                return Problem(503, "PLANNING_SCHEMA_NOT_READY", "Blend plan storage is not deployed yet.");
            plan = await db.BlendPlanDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == planId && x.ContainerId == id && x.CompanyId == scope.Value.Company, ct);
            if (plan is null) return Results.NotFound();
            var container = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
            if (plan.Status != "DRAFT" || !DemandIsCurrent(plan, order, container))
                return Problem(409, "PLAN_DEMAND_STALE", "Create a current draft before assessing product candidates.");
            targetProductIdentity = TargetProductFromSnapshot(plan.DemandSnapshot);
            if (targetProductIdentity is null)
                return Problem(409, "PLAN_PRODUCT_UNSPECIFIED", "Create a new draft with a target product identity.");
        }
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(id, scope.Value.Company, facilityId, db, catalog, ledger, ct,
            withCompatibility: plan is not null); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException) { return CandidateFailure(ex); }
        if (plan is not null)
        {
            var currentPlan = await db.BlendPlanDrafts.AsNoTracking().SingleAsync(x => x.Id == plan.Id, ct);
            var currentContainer = await db.Containers.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var currentOrder = await db.Orders.AsNoTracking()
                .SingleAsync(x => x.Id == currentContainer.OrderId, ct);
            if (currentPlan.Status != "DRAFT" || !DemandIsCurrent(currentPlan, currentOrder, currentContainer))
                return Problem(409, "PLAN_DEMAND_STALE", "Draft or demand changed during the inventory read.");
        }
        var revision = plan is null ? snapshot.ScopeRevision :
            PlanScopeRevision(snapshot, plan, targetProductIdentity!);
        var expected = http.Request.Query["scopeRevision"].ToString();
        if (expected.Length > 0 && expected != revision)
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate inventory changed. Reload candidates.");
        var eligible = snapshot.Candidates.Where(x => x.InventoryEligible).ToArray();
        var filtered = bool.TryParse(http.Request.Query["includeExcluded"], out var include) && include
            ? snapshot.Candidates : eligible;
        if (plan is not null && bool.TryParse(http.Request.Query["productMatchOnly"], out var matchOnly) && matchOnly)
            filtered = filtered.Where(x => ProductCompatibility(x, targetProductIdentity!,
                snapshot.CatalogCommodityId, snapshot.OrderCommodityCode,
                snapshot.Compatibilities!.Value) != "PRODUCT_MISMATCH").ToArray();
        var items = filtered.Skip((page - 1) * size).Take(size).Select(x => x.ToResponse(
            plan is null ? "NOT_EVALUATED" : ProductCompatibility(x, targetProductIdentity!,
                snapshot.CatalogCommodityId, snapshot.OrderCommodityCode, snapshot.Compatibilities!.Value))).ToArray();
        var eligibleMass = eligible.Sum(x => x.AvailableKg);
        var matchingMass = plan is null ? (decimal?)null : eligible
            .Where(x => ProductCompatibility(x, targetProductIdentity!, snapshot.CatalogCommodityId,
                snapshot.OrderCommodityCode,
                snapshot.Compatibilities!.Value) == "DIRECT_MATCH")
            .Sum(x => x.AvailableKg);
        return Results.Ok(new
        {
            containerId = id, facilityId, planId, targetProductIdentity,
            snapshot.OrderVersion, snapshot.ContainerVersion,
            snapshot.CatalogCommodityId, targetMassKg = ExportRules.Text(snapshot.TargetMassKg),
            snapshot.ObservedAt, scopeRevision = revision, scopeComplete = true,
            summary = new { inventoryEligibleCount = eligible.Length,
                excludedCount = snapshot.Candidates.Count - eligible.Length,
                inventoryEligibleMassKg = ExportRules.Text(eligibleMass),
                inventoryShortfallKg = ExportRules.Text(Math.Max(0, snapshot.TargetMassKg - eligibleMass)),
                directProductAvailableMassKg = matchingMass is null ? null : ExportRules.Text(matchingMass.Value) },
            items, page, pageSize = size, totalCount = filtered.Count,
            hasMore = (long)page * size < filtered.Count, canPropose = false
        });
    }

    private static async Task<IResult> CandidateLineageAsync(Guid id, Guid lotId, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog, LedgerService ledger,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!Guid.TryParse(http.Request.Query["facilityId"], out var facilityId) || facilityId == Guid.Empty ||
            !Guid.TryParse(http.Request.Query["locationId"], out var locationId) || locationId == Guid.Empty)
            return Invalid(new() { ["query"] = ["Choose a facility and location."] });
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(id, scope.Value.Company, facilityId, db, catalog, ledger, ct); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException) { return CandidateFailure(ex); }
        var candidate = snapshot.Candidates.SingleOrDefault(x => x.LotId == lotId && x.LocationId == locationId);
        if (candidate is null) return http.Request.Query.ContainsKey("candidateRevision")
            ? Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate left the facility. Reload candidates.") : Results.NotFound();
        var expected = http.Request.Query["candidateRevision"].ToString();
        if (expected.Length > 0 && expected != candidate.CandidateRevision)
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate inventory changed. Reload candidates.");
        var data = snapshot.Ledger;
        var allNodes = data.GetProperty("nodes").EnumerateArray().ToArray();
        var byOutput = data.GetProperty("edges").EnumerateArray()
            .GroupBy(x => x.GetProperty("outputLotId").GetGuid()).ToDictionary(x => x.Key, x => x.ToList());
        var ancestry = Ancestors(lotId, byOutput, allNodes.Select(x => x.GetProperty("lotId").GetGuid()).ToHashSet());
        var nodes = allNodes.Where(x => ancestry.Nodes.Contains(x.GetProperty("lotId").GetGuid())).ToArray();
        var operations = data.GetProperty("operations").EnumerateArray().Where(x =>
            nodes.Any(n => n.GetProperty("operationId").ValueKind != JsonValueKind.Null &&
                n.GetProperty("operationId").GetGuid() == x.GetProperty("id").GetGuid())).ToArray();
        var composition = data.GetProperty("composition").EnumerateArray()
            .Where(x => x.GetProperty("lotId").GetGuid() == lotId)
            .Select(x => new { ledgerSourceLotId = x.GetProperty("ledgerSourceLotId").GetGuid(),
                captureSourceLotId = x.GetProperty("captureSourceLotId").ValueKind == JsonValueKind.Null
                    ? (Guid?)null : x.GetProperty("captureSourceLotId").GetGuid(),
                lotAttributedKg = x.GetProperty("lotAttributedKg").GetString() }).ToArray();
        return Results.Ok(new { lotId, locationId, candidate.CandidateRevision, candidate.PoolRevision,
            candidate.StockKind, compositionBasis = "LOT_ATTRIBUTION",
            positionAttributionStatus = candidate.Reasons.Contains("POSITION_ATTRIBUTION_UNRESOLVED")
                ? "UNRESOLVED" : "PROVEN", composition,
            lineage = new { complete = candidate.LineageComplete, nodes,
                edges = ancestry.Edges, operations, reasons = candidate.Reasons },
            complianceStatus = "NOT_ASSESSED" });
    }

    private static async Task<IResult> CandidateResiduePreviewAsync(Guid id, Guid lotId, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog, LedgerService ledger,
        ResidueEvaluationService residue, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!Guid.TryParse(http.Request.Query["facilityId"], out var facilityId) || facilityId == Guid.Empty ||
            !Guid.TryParse(http.Request.Query["locationId"], out var locationId) || locationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(http.Request.Query["candidateRevision"]) ||
            string.IsNullOrWhiteSpace(http.Request.Query["substanceIds"]))
            return Invalid(new() { ["query"] = ["Choose a facility, location, candidate revision and substances."] });
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(id, scope.Value.Company, facilityId, db, catalog, ledger, ct); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException) { return CandidateFailure(ex); }
        var candidate = snapshot.Candidates.SingleOrDefault(x => x.LotId == lotId && x.LocationId == locationId);
        if (candidate is null || candidate.CandidateRevision != http.Request.Query["candidateRevision"])
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate inventory changed. Reload candidates.");
        if (!candidate.InventoryEligible || !candidate.LineageComplete)
            return Problem(409, "CANDIDATE_NOT_ELIGIBLE", "Candidate is not physically eligible. Reload candidates.");
        var preview = await residue.PreviewLotAsync(lotId, http.Request.Query["substanceIds"], ct);
        // The residue read owns its own repeatable-read snapshot; this is a diagnostic
        // view. A future proposal must recheck stock and evidence in its write path.
        return Results.Ok(new { id, lotId, locationId, candidate.CandidateRevision,
            assessmentStatus = "NOT_ASSESSED", residue = preview });
    }

}
