using Npgsql;

namespace GrainLedger.Modules.Capture;

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
public interface IProcurementLedgerPort
{
    Task<ProcurementReceivingOptionsPage> ListReceivingOptionsAsync(Guid companyId, Guid? collectionSiteId,
        string? search, string sortBy, bool descending, int offset, int limit, CancellationToken ct);
    Task<ProcurementListPage> ListProcurementPageAsync(Guid companyId, Guid? farmerId,
        DateTimeOffset? receivedFromInclusive, DateTimeOffset? receivedBeforeExclusive,
        string? search, string sortBy, bool descending, int offset, int limit, CancellationToken ct);
    Task<bool> IsValidReceivingOptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid collectionSiteId, Guid receivingLocationId, CancellationToken ct);
    Task RegisterPendingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid procurementId, CancellationToken ct);
    Task<string> ReserveSourceLotCodeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid companyId, Guid procurementId, Guid lotId, CancellationToken ct);
    Task<ProcurementReceivingState?> GetReceivingStateAsync(Guid procurementId, CancellationToken ct);
    Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockAsync(Guid companyId, Guid sourceLotId, CancellationToken ct);
    Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockBulkAsync(Guid companyId, IReadOnlyCollection<Guid> sourceLotIds, CancellationToken ct);
    Task<ProcurementSourceLineage> GetSourceLineageAsync(Guid companyId, Guid sourceLotId, CancellationToken ct);
}

public sealed class ProcurementLedgerUnavailableException : Exception
{
    public ProcurementLedgerUnavailableException() : base("Protected procurement stock is unavailable.") { }
}

