using System.Text.Json;

namespace GrainLedger.Modules.Exports;

internal sealed record CandidateGroupItem(Guid LotId, Guid LocationId, decimal QuantityKg,
    string CandidateRevision);

internal sealed record CandidateGroupResult(string Status, decimal InputMassKg,
    Guid? CompatibilityPolicyId, IReadOnlyList<string> Findings);

// This checks one physical selection against a trusted, owner/facility-scoped Ledger
// snapshot. It does not assess quality, residues or processing output yield.
internal static class CandidateGroupValidator
{
    private const decimal MaxKg = 99999999999999.999999m;

    internal static CandidateGroupResult Validate(PlanningCandidateSnapshot snapshot,
        IReadOnlyList<CandidateGroupItem> selected, string targetProductIdentity,
        string mode, string expectedScopeRevision, Guid? compatibilityPolicyId = null,
        bool outputYieldPolicyAvailable = false)
    {
        var findings = new List<string>();
        if (expectedScopeRevision != snapshot.ScopeRevision)
            findings.Add("CANDIDATE_SNAPSHOT_STALE");
        if (selected.Count == 0) findings.Add("NO_POSITIONS_SELECTED");
        if (mode is not ("DIRECT_STOCK" or "BLEND_INPUT_STOCK"))
            findings.Add("SELECTION_MODE_INVALID");
        if (selected.Count != selected.Select(x => (x.LotId, x.LocationId)).Distinct().Count())
            findings.Add("DUPLICATE_POSITION");

        var positions = snapshot.Candidates.ToDictionary(x => (x.LotId, x.LocationId));
        var products = new HashSet<string>(StringComparer.Ordinal);
        decimal total = 0;
        foreach (var item in selected)
        {
            if (!positions.TryGetValue((item.LotId, item.LocationId), out var position))
            {
                findings.Add("POSITION_NOT_IN_SCOPE");
                continue;
            }
            if (item.CandidateRevision != position.CandidateRevision)
                findings.Add("CANDIDATE_SNAPSHOT_STALE");
            if (!position.InventoryEligible || !position.LineageComplete)
                findings.AddRange(position.Reasons.Length == 0 ? ["POSITION_NOT_ELIGIBLE"] : position.Reasons);
            if (item.QuantityKg <= 0 || item.QuantityKg > MaxKg ||
                decimal.Round(item.QuantityKg, 6) != item.QuantityKg)
                findings.Add("QUANTITY_INVALID");
            else if (item.QuantityKg > position.AvailableKg)
                findings.Add("QUANTITY_EXCEEDS_AVAILABLE");
            try { total = checked(total + item.QuantityKg); }
            catch (OverflowException) { findings.Add("MASS_OVERFLOW"); }
            if (total > MaxKg) findings.Add("MASS_OVERFLOW");
            products.Add(position.LedgerCommodityId + ":" + position.ProductIdentity);
            if (mode == "DIRECT_STOCK" &&
                (position.ProductIdentity != targetProductIdentity ||
                    position.CatalogCommodityId != snapshot.CatalogCommodityId))
                findings.Add("DIRECT_PRODUCT_MISMATCH");
            if (mode == "BLEND_INPUT_STOCK" &&
                position.Stage is not ("HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE"))
                findings.Add("BLEND_INPUT_STAGE_INVALID");
        }

        if (mode == "DIRECT_STOCK" && total != snapshot.TargetMassKg)
            findings.Add("TARGET_MASS_MISMATCH");
        if (mode == "BLEND_INPUT_STOCK")
        {
            if (targetProductIdentity != "BLENDED_RICE") findings.Add("BLEND_OUTPUT_MISMATCH");
            // Ledger records actual output and declared loss at commit. Its balance
            // tolerance is not an approved prospective yield. Conservation still
            // proves that output cannot exceed the selected input mass.
            if (total < snapshot.TargetMassKg)
                findings.Add("TARGET_MASS_EXCEEDS_INPUT");
            if (compatibilityPolicyId is null || snapshot.Compatibilities is null)
                findings.Add("COMPATIBILITY_POLICY_MISSING");
            else
            {
                var policy = snapshot.Compatibilities.Value.EnumerateArray().SingleOrDefault(x =>
                    x.GetProperty("id").GetGuid() == compatibilityPolicyId.Value);
                if (policy.ValueKind == JsonValueKind.Undefined ||
                    policy.GetProperty("outputCatalogCommodityId").ValueKind != JsonValueKind.String ||
                    policy.GetProperty("outputCatalogCommodityId").GetGuid() != snapshot.CatalogCommodityId ||
                    !policy.GetProperty("inputProducts").EnumerateArray()
                        .Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal).SetEquals(products))
                    findings.Add("COMPATIBILITY_SET_MISMATCH");
            }
            // Enough input is necessary but not sufficient: an approved yield/loss
            // rule is needed before promising this container's output quantity.
            if (!outputYieldPolicyAvailable) findings.Add("OUTPUT_YIELD_POLICY_MISSING");
        }
        var distinct = findings.Distinct(StringComparer.Ordinal).ToArray();
        var status = distinct.Length == 0 ? "VALID" : distinct.All(x => x is
            "OUTPUT_YIELD_POLICY_MISSING" or "COMPATIBILITY_POLICY_MISSING") ? "INCOMPLETE" : "INVALID";
        return new CandidateGroupResult(status, total, compatibilityPolicyId, distinct);
    }
}
