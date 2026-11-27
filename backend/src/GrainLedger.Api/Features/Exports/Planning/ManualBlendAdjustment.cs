namespace GrainLedger.Api.Features.Exports;

public sealed record BlendPlanAdjustmentItem(Guid LotId, Guid LocationId, string QuantityKg);
public sealed record BlendPlanAdjustment(Guid PreviewReference, BlendPlanAdjustmentItem[] Allocations);

// Resolve quantities and identities from the current scoped Ledger snapshot,
// never from caller-supplied product, variety, policy or candidate revisions.
internal static class ManualBlendAdjustment
{
    internal static GreedyStockSelection.Result? Build(PlanningCandidateSnapshot snapshot,
        GreedyStockSelection.Result initial, BlendPlanAdjustmentItem[]? items,
        string target, Func<Guid, bool> hasYield, out string? error)
    {
        error = null;
        if (items is null || items.Length is < 1 or > 100 ||
            items.Any(x => x is null || x.LotId == Guid.Empty || x.LocationId == Guid.Empty) ||
            items.Select(x => (x.LotId, x.LocationId)).Distinct().Count() != items.Length)
        { error = "Choose 1–100 distinct lot/location positions."; return null; }
        var allocations = new List<GreedyStockSelection.Allocation>();
        foreach (var item in items)
        {
            var position = snapshot.Candidates.SingleOrDefault(x => x.LotId == item.LotId && x.LocationId == item.LocationId);
            if (position is null || !ExportRules.TryMass(item.QuantityKg, "KG", out var kg) || kg < 1 || kg % 1 != 0)
            { error = "Choose scoped positions with positive whole-kilogram quantities."; return null; }
            allocations.Add(new(position.LotId, position.LocationId, position.LotCode, kg, position.CandidateRevision));
        }
        var total = allocations.Sum(x => x.QuantityKg);
        if (total != initial.SelectedMassKg)
        { error = $"Keep the total input at {ExportRules.Text(initial.SelectedMassKg)} kg."; return null; }
        Guid? compatibility = initial.CompatibilityPolicyId;
        if (initial.Mode == "BLEND_INPUT_STOCK" && snapshot.Compatibilities is { } policies)
        {
            var products = allocations.Select(a => snapshot.Candidates.Single(c => c.LotId == a.LotId && c.LocationId == a.LocationId))
                .Select(c => $"{c.LedgerCommodityId}:{c.ProductIdentity}").ToHashSet(StringComparer.Ordinal);
            compatibility = null;
            foreach (var policy in policies.EnumerateArray().OrderByDescending(p => p.GetProperty("id").GetGuid() == initial.CompatibilityPolicyId))
            {
                var policyId = policy.GetProperty("id").GetGuid();
                if (hasYield(policyId) && ExportProductMatch.TryMatchBlendOutput(policy, snapshot.CatalogCommodityId,
                        snapshot.OrderCommodityCode, snapshot.CommodityCodes, out _) &&
                    policy.GetProperty("inputProducts").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal).SetEquals(products))
                { compatibility = policyId; break; }
            }
        }
        var selected = initial with { Allocations = allocations, SelectedMassKg = total, CompatibilityPolicyId = compatibility };
        // The shared validator reports excess stock, HOLDs, wrong varieties,
        // product incompatibility and stale inputs through the F14 assessment.
        return selected;
    }
}
