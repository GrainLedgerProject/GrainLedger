using System.Text.Json;
using GrainLedger.Api.Features.Ledger;

namespace GrainLedger.Api.Features.Exports;

internal static class ExportProductMatch
{
    internal static bool IsDirect(PlanningCandidate candidate, string targetProductIdentity,
        Guid orderCommodityId, string? orderCommodityCode)
    {
        if (candidate.ProductIdentity != targetProductIdentity) return false;
        if (candidate.CatalogCommodityId == orderCommodityId) return true;

        // Ledger preserves the source Catalog commodity through drying and
        // milling. A committed PADDY-derived finished fraction is therefore
        // physically rice even while its stored commodity ID remains PADDY.
        // The candidate snapshot has already checked committed lineage,
        // current pool generation, ownership and stock restrictions.
        return candidate.LineageComplete && candidate.Stage == targetProductIdentity &&
            IsPaddyDerivedRice(candidate.CatalogCommodityCode, orderCommodityCode, targetProductIdentity);
    }

    internal static bool TryMatchBlendOutput(JsonElement policy, Guid orderCommodityId,
        string? orderCommodityCode, IReadOnlyDictionary<Guid, string>? commodityCodes, out Guid outputCommodityId)
    {
        outputCommodityId = Guid.Empty;
        return policy.ValueKind == JsonValueKind.Object &&
            policy.GetProperty("outputCatalogCommodityId").ValueKind == JsonValueKind.String &&
            policy.GetProperty("outputCatalogCommodityId").TryGetGuid(out outputCommodityId) &&
            (outputCommodityId == orderCommodityId || IsPaddyDerivedRice(
                commodityCodes?.GetValueOrDefault(outputCommodityId), orderCommodityCode, "BLENDED_RICE"));
    }

    internal static bool IsResidueForProduct(ResidueResult bound, Guid physicalCommodityId,
        PlanningCandidateSnapshot snapshot, string target)
    {
        if (bound.CommodityId != physicalCommodityId) return false;
        return physicalCommodityId == snapshot.CatalogCommodityId ||
            (bound.ProductStage == target && bound.ProductIdentity == target &&
            IsPaddyDerivedRice(snapshot.CommodityCodes?.GetValueOrDefault(physicalCommodityId),
                snapshot.OrderCommodityCode, target));
    }

    internal static Func<ResidueResult, bool> AssessmentResidueScope(PlanningCandidateSnapshot snapshot,
        string target, IReadOnlyList<CandidateGroupItem> positions, Guid? compatibilityId)
    {
        if (compatibilityId is { } id && snapshot.Compatibilities is { } policies)
        {
            var selected = policies.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == id);
            if (TryMatchBlendOutput(selected, snapshot.CatalogCommodityId, snapshot.OrderCommodityCode,
                snapshot.CommodityCodes, out var physicalId))
                return bound => IsResidueForProduct(bound, physicalId, snapshot, target);
            return _ => false;
        }

        var selectedLots = positions.Select(p => p.LotId).ToHashSet();
        var physicalIds = snapshot.Candidates.Where(c => selectedLots.Contains(c.LotId) &&
                IsDirect(c, target, snapshot.CatalogCommodityId, snapshot.OrderCommodityCode))
            .Select(c => c.CatalogCommodityId).ToHashSet();
        return bound => bound.CommodityId is { } commodity && physicalIds.Contains(commodity) &&
            IsResidueForProduct(bound, commodity, snapshot, target);
    }

    private static bool IsPaddyDerivedRice(string? physicalCode, string? orderCode, string target) =>
        physicalCode == "PADDY" && (orderCode is "RICE" or "RICE_POLISHED" or "POLISHED-RICE") &&
        (target is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE");
}
