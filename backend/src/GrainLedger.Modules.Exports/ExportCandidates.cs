using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

internal sealed record PlanningCandidate(Guid LotId, string LotCode, Guid LocationId, string LocationName,
    string Stage, string ProductIdentity, decimal OnHandKg, decimal AvailableKg, string StockKind,
    bool IsCurrentPool, bool CustodyDerived, string? PoolRevision, string CandidateRevision,
    int SourceCount, bool LineageComplete, bool InventoryEligible, string[] Restrictions, string[] Reasons)
{
    public object ToResponse() => new
    {
        lotId = LotId, lotCode = LotCode, locationId = LocationId, locationName = LocationName,
        stage = Stage, productIdentity = ProductIdentity, onHandKg = ExportRules.Text(OnHandKg),
        availableKg = ExportRules.Text(AvailableKg), stockKind = StockKind, isCurrentPool = IsCurrentPool,
        custodyDerived = CustodyDerived, poolRevision = PoolRevision, candidateRevision = CandidateRevision,
        sourceCount = SourceCount, lineageComplete = LineageComplete, inventoryEligible = InventoryEligible,
        restrictions = Restrictions, reasons = Reasons, productCompatibility = "NOT_EVALUATED",
        complianceStatus = "NOT_ASSESSED"
    };
}

internal sealed record PlanningCandidateSnapshot(Guid ContainerId, Guid FacilityId, int OrderVersion,
    int ContainerVersion, string ContainerStatus, Guid CatalogCommodityId, decimal TargetMassKg, DateTimeOffset ObservedAt,
    string ScopeRevision, JsonElement Ledger, IReadOnlyList<PlanningCandidate> Candidates);

internal sealed class CandidateDemandChangedException : Exception;

public static partial class ExportModule
{
    private static string CandidateHash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static decimal LedgerMass(JsonElement value) =>
        decimal.Parse(value.GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

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
        Guid facilityId, ExportDbContext db, LedgerService ledger, CancellationToken ct)
    {
        var container = await db.Containers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == company, ct);
        if (container is null) throw new KeyNotFoundException();
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == container.OrderId, ct);
        var data = await ledger.ReadPlanningCandidatesAsync(company, facilityId, ct);
        var current = await db.Containers.AsNoTracking().Where(x => x.Id == id && x.CompanyId == company)
            .Select(x => new { x.Version, x.Status, x.TargetMassKg }).SingleAsync(ct);
        var currentOrder = await db.Orders.AsNoTracking().Where(x => x.Id == container.OrderId)
            .Select(x => new { x.Version, x.CommodityId }).SingleAsync(ct);
        if (current.Version != container.Version || current.Status != container.Status ||
            current.TargetMassKg != container.TargetMassKg || currentOrder.Version != order.Version ||
            currentOrder.CommodityId != order.CommodityId)
            throw new CandidateDemandChangedException();
        var positions = data.GetProperty("positions").EnumerateArray().ToArray();
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
            else if (p.GetProperty("catalogCommodityId").GetGuid() != order.CommodityId)
                reasons.Add("COMMODITY_MISMATCH");
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
                p.GetProperty("locationName").GetString()!, p.GetProperty("stage").GetString()!,
                p.GetProperty("productIdentity").GetString()!, onHand, available, kind, isCurrent,
                custodyDerived, isCurrent ? p.GetProperty("positionPoolRevision").GetString() : null,
                CandidateHash(revision + ":" + lotId + ":" + locationId), composition.Length,
                lineageComplete, reasons.Count == 0, restrictions, reasons.Distinct().ToArray()));
        }
        return new PlanningCandidateSnapshot(id, facilityId, order.Version, container.Version, container.Status,
            order.CommodityId, container.TargetMassKg, DateTimeOffset.UtcNow, revision, data, candidates);
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
        ICurrentAccess access, ExportDbContext db, LedgerService ledger, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!Guid.TryParse(http.Request.Query["facilityId"], out var facilityId) || facilityId == Guid.Empty ||
            !Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size) ||
            http.Request.Query.ContainsKey("includeExcluded") &&
            !bool.TryParse(http.Request.Query["includeExcluded"], out _))
            return Invalid(new() { ["query"] = ["Choose a facility and valid pagination/filter values."] });
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(id, scope.Value.Company, facilityId, db, ledger, ct); }
        catch (Exception ex) when (ex is LedgerException or KeyNotFoundException or CandidateDemandChangedException) { return CandidateFailure(ex); }
        var expected = http.Request.Query["scopeRevision"].ToString();
        if (expected.Length > 0 && expected != snapshot.ScopeRevision)
            return Problem(409, "CANDIDATE_SNAPSHOT_STALE", "Candidate inventory changed. Reload candidates.");
        var eligible = snapshot.Candidates.Where(x => x.InventoryEligible).ToArray();
        var filtered = bool.TryParse(http.Request.Query["includeExcluded"], out var include) && include
            ? snapshot.Candidates : eligible;
        var items = filtered.Skip((page - 1) * size).Take(size).Select(x => x.ToResponse()).ToArray();
        var eligibleMass = eligible.Sum(x => x.AvailableKg);
        return Results.Ok(new
        {
            containerId = id, facilityId, snapshot.OrderVersion, snapshot.ContainerVersion,
            snapshot.CatalogCommodityId, targetMassKg = ExportRules.Text(snapshot.TargetMassKg),
            snapshot.ObservedAt, snapshot.ScopeRevision, scopeComplete = true,
            summary = new { inventoryEligibleCount = eligible.Length,
                excludedCount = snapshot.Candidates.Count - eligible.Length,
                inventoryEligibleMassKg = ExportRules.Text(eligibleMass),
                inventoryShortfallKg = ExportRules.Text(Math.Max(0, snapshot.TargetMassKg - eligibleMass)) },
            items, page, pageSize = size, totalCount = filtered.Count,
            hasMore = (long)page * size < filtered.Count, canPropose = false
        });
    }

    private static async Task<IResult> CandidateLineageAsync(Guid id, Guid lotId, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, LedgerService ledger,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!Guid.TryParse(http.Request.Query["facilityId"], out var facilityId) || facilityId == Guid.Empty ||
            !Guid.TryParse(http.Request.Query["locationId"], out var locationId) || locationId == Guid.Empty)
            return Invalid(new() { ["query"] = ["Choose a facility and location."] });
        PlanningCandidateSnapshot snapshot;
        try { snapshot = await CandidateSnapshotAsync(id, scope.Value.Company, facilityId, db, ledger, ct); }
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

}
