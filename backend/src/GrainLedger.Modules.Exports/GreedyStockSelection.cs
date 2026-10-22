using System.Text.Json;

namespace GrainLedger.Modules.Exports;

// Physical-stock search only. Compliance and the quantity produced by an actual
// Ledger BLENDING operation must be assessed before a plan can be proposed.
internal static class GreedyStockSelection
{
    private const decimal MassUnitKg = 0.000001m;

    internal sealed record Allocation(Guid LotId, Guid LocationId, string LotCode,
        decimal QuantityKg, string CandidateRevision);

    internal sealed record Result(string Outcome, string Mode, Guid? CompatibilityPolicyId,
        decimal SelectedMassKg, decimal RemainingMassKg, IReadOnlyList<Allocation> Allocations,
        string[]? Reasons = null, string[]? MissingInputProducts = null)
    {
        public object ToResponse() => new
        {
            outcome = Outcome, mode = Mode, compatibilityPolicyId = CompatibilityPolicyId,
            selectedMassKg = ExportRules.Text(SelectedMassKg),
            remainingMassKg = ExportRules.Text(RemainingMassKg),
            allocations = Allocations.Select(x => new
            {
                x.LotId, x.LocationId, x.LotCode,
                quantityKg = ExportRules.Text(x.QuantityKg), x.CandidateRevision
            }).ToArray(),
            reasons = Reasons ?? [], missingInputProducts = MissingInputProducts ?? [],
            complianceStatus = "NOT_ASSESSED", canPropose = false
        };
    }

    private static IEnumerable<PlanningCandidate> Ordered(IEnumerable<PlanningCandidate> candidates) =>
        candidates.OrderByDescending(x => x.AvailableKg).ThenBy(x => x.LotId).ThenBy(x => x.LocationId);

    private static string ProductKey(PlanningCandidate candidate) =>
        candidate.LedgerCommodityId + ":" + candidate.ProductIdentity;

    private static Result Fill(decimal target, IEnumerable<PlanningCandidate> candidates,
        string mode, Guid? policyId, IReadOnlyList<Allocation>? initial = null)
    {
        var allocations = initial?.ToList() ?? [];
        var remaining = target - allocations.Sum(x => x.QuantityKg);
        foreach (var candidate in Ordered(candidates))
        {
            if (remaining == 0) break;
            var alreadySelected = allocations.Where(x => x.LotId == candidate.LotId &&
                x.LocationId == candidate.LocationId).Sum(x => x.QuantityKg);
            var quantity = Math.Min(remaining, candidate.AvailableKg - alreadySelected);
            if (quantity <= 0) continue;
            var existing = allocations.FindIndex(x => x.LotId == candidate.LotId &&
                x.LocationId == candidate.LocationId);
            var allocation = new Allocation(candidate.LotId, candidate.LocationId, candidate.LotCode,
                quantity + alreadySelected, candidate.CandidateRevision);
            if (existing < 0) allocations.Add(allocation);
            else allocations[existing] = allocation;
            remaining -= quantity;
        }
        return new Result(remaining == 0 ? "MASS_COVERED" : "SEARCH_EXHAUSTED", mode,
            policyId, target - remaining, remaining, allocations);
    }

    internal static Result Select(IReadOnlyList<PlanningCandidate> candidates, decimal targetKg,
        string targetProductIdentity, Guid catalogCommodityId, JsonElement policies)
    {
        if (targetKg <= 0) throw new ArgumentOutOfRangeException(nameof(targetKg));
        var eligible = candidates.Where(x => x.InventoryEligible && x.AvailableKg > 0).ToArray();
        var direct = Fill(targetKg, eligible.Where(x => x.ProductIdentity == targetProductIdentity),
            "DIRECT_STOCK", null);
        if (direct.Outcome == "MASS_COVERED") return direct;
        if (targetProductIdentity != "BLENDED_RICE")
            return direct with { Reasons = ["INSUFFICIENT_DIRECT_STOCK"] };

        var matchingPolicy = false;
        var targetBelowInputPrecision = false;
        var missingProducts = new SortedSet<string>(StringComparer.Ordinal);
        Result? closest = null;
        foreach (var policy in policies.EnumerateArray().OrderBy(x => x.GetProperty("id").GetGuid()))
        {
            var output = policy.GetProperty("outputCatalogCommodityId");
            if (output.ValueKind != JsonValueKind.String || output.GetGuid() != catalogCommodityId)
                continue;
            matchingPolicy = true;
            var keys = policy.GetProperty("inputProducts").EnumerateArray()
                .Select(x => x.GetString()!).Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (keys.Length == 0) continue;
            if (targetKg < keys.Length * MassUnitKg)
            {
                targetBelowInputPrecision = true;
                continue;
            }
            var inputs = eligible.Where(x => x.Stage is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE")
                .Where(x => keys.Contains(ProductKey(x), StringComparer.Ordinal)).ToArray();
            var missing = keys.Where(key => !inputs.Any(x => ProductKey(x) == key)).ToArray();
            if (missing.Length > 0)
            {
                foreach (var key in missing) missingProducts.Add(key);
                continue;
            }

            // Include every product in the exact Ledger set before filling the
            // remaining mass greedily. This unit is storage precision, not a
            // business blend ratio or a claim about output yield.
            var anchors = keys.Select(key => Ordered(inputs.Where(x => ProductKey(x) == key)).First())
                .Select(x => new Allocation(x.LotId, x.LocationId, x.LotCode,
                    MassUnitKg, x.CandidateRevision)).ToArray();
            var selection = Fill(targetKg, inputs, "BLEND_INPUT_STOCK",
                policy.GetProperty("id").GetGuid(), anchors);
            if (selection.Outcome == "MASS_COVERED") return selection;
            if (closest is null || selection.SelectedMassKg > closest.SelectedMassKg)
                closest = selection;
        }
        if (closest is not null)
            return closest with { Reasons = ["INSUFFICIENT_COMPATIBLE_STOCK"] };
        return direct with
        {
            Reasons = [!matchingPolicy ? "NO_COMPATIBLE_POLICY" :
                targetBelowInputPrecision ? "TARGET_BELOW_INPUT_PRECISION" : "COMPATIBILITY_INPUT_MISSING"],
            MissingInputProducts = missingProducts.ToArray()
        };
    }
}
