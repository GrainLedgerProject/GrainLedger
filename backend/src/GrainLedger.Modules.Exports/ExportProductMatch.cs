namespace GrainLedger.Modules.Exports;

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
        return candidate.LineageComplete &&
            candidate.Stage == targetProductIdentity &&
            candidate.CatalogCommodityCode == "PADDY" &&
            orderCommodityCode == "RICE" &&
            targetProductIdentity is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE";
    }
}
