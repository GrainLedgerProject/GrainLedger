using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Catalogs;

public sealed record ApplicableMrlLimit(Guid SubstanceId, decimal LimitValue, string Unit);
public sealed record ApplicableMrlResult(string Status, Guid? VersionId, int? VersionNo,
    DateTimeOffset? PublishedAt, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo,
    IReadOnlyList<ApplicableMrlLimit> Limits, Guid? PublishedBy = null,
    string? SourceRef = null, uint? VersionToken = null,
    Guid? MarketId = null, Guid? CommodityId = null);

// Shared point-in-time resolver. Coverage is a separate business policy: a
// published version with some limits is not automatically a complete panel.
public sealed class ApplicableMrlRead(CatalogDbContext db)
{
    public async Task<ApplicableMrlResult> ResolveAsync(Guid marketId, Guid commodityId,
        DateTimeOffset decisionAt, CancellationToken ct = default)
    {
        if (marketId == Guid.Empty || commodityId == Guid.Empty)
            throw new ArgumentException("Market and commodity are required.");
        var instant = decisionAt.ToUniversalTime();
        var candidates = await db.MrlVersions.AsNoTracking()
            .Where(x => x.MarketId == marketId && x.CommodityId == commodityId &&
                x.Status == "PUBLISHED" && x.PublishedAt <= instant &&
                x.EffectiveFrom <= instant && (x.EffectiveTo == null || instant < x.EffectiveTo))
            .OrderByDescending(x => x.VersionNo).Take(2).ToArrayAsync(ct);
        if (candidates.Length == 0) return new("NOT_AVAILABLE", null, null, null, null, null, []);
        if (candidates.Length > 1) return new("AMBIGUOUS", null, null, null, null, null, []);
        var version = candidates[0];
        var limits = await db.MrlLimits.AsNoTracking()
            .Where(x => x.MrlVersionId == version.Id)
            .OrderBy(x => x.SubstanceId)
            .Select(x => new ApplicableMrlLimit(x.SubstanceId, x.LimitValue, x.Unit))
            .ToArrayAsync(ct);
        return new("FOUND", version.Id, version.VersionNo, version.PublishedAt,
            version.EffectiveFrom, version.EffectiveTo, limits, version.PublishedBy,
            version.SourceRef, version.Version, version.MarketId, version.CommodityId);
    }
}
