using Npgsql;

namespace GrainLedger.Api.Features.Capture;

public sealed record ProcurementReceivingOption(Guid CollectionSiteId, string CollectionSiteCode,
    string CollectionSiteName, Guid ReceivingLocationId, string ReceivingLocationCode);
public sealed record ProcurementReceivingOptionsPage(
    IReadOnlyList<ProcurementReceivingOption> Items, int TotalCount);
public sealed record ProcurementListPageItem(Guid Id, string Status);
public sealed record ProcurementListPage(IReadOnlyList<ProcurementListPageItem> Items, int TotalCount);
public sealed record ProcurementStockPosition(Guid CurrentStockLotId, string CurrentStockLotCode,
    Guid SourceLotId, Guid LocationId, string OnHandKg, string AvailableKg, string PoolTotalKg,
    bool IsMixedBulk, long? PoolRevision);
public sealed record ProcurementLineageLink(Guid LotId, string LotCode, Guid? LocationId,
    bool IsCurrentPool, long? PoolRevision, string QuantityKg);
public sealed record ProcurementSourceLineage(IReadOnlyList<ProcurementLineageLink> Descendants);
public sealed record ProcurementReceivingState(string Status, int Version);


/// <summary>Ledger validates and registers pending source packages on the caller's transaction.</summary>
public sealed class ProcurementLedgerUnavailableException : Exception
{
    public ProcurementLedgerUnavailableException() : base("Protected procurement stock is unavailable.") { }
}

