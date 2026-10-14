using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record ProcurementReceivingOption(Guid CollectionSiteId, string CollectionSiteCode,
    string CollectionSiteName, Guid ReceivingLocationId, string ReceivingLocationCode);
public sealed record ProcurementStockPosition(Guid LocationId, string OnHandKg, string AvailableKg);
public sealed record ProcurementReceivingState(string Status, int Version);

/// <summary>Ledger validates and registers pending source packages on the caller's transaction.</summary>
public interface IProcurementLedgerPort
{
    Task<IReadOnlyList<ProcurementReceivingOption>> ListReceivingOptionsAsync(Guid companyId,
        Guid? collectionSiteId, int offset, int limit, CancellationToken ct);
    Task<bool> IsValidReceivingOptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid collectionSiteId, Guid receivingLocationId, CancellationToken ct);
    Task RegisterPendingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid procurementId, CancellationToken ct);
    Task<ProcurementReceivingState?> GetReceivingStateAsync(Guid procurementId, CancellationToken ct);
    Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockAsync(Guid companyId, Guid lotId, CancellationToken ct);
}

public sealed class ProcurementLedgerUnavailableException : Exception
{
    public ProcurementLedgerUnavailableException() : base("Protected procurement stock is unavailable.") { }
}

