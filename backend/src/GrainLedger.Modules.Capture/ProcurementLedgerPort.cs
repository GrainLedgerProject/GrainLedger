using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record ProcurementReceivingOption(Guid CollectionSiteId, string CollectionSiteCode,
    string CollectionSiteName, Guid ReceivingLocationId, string ReceivingLocationCode);
public sealed record ProcurementStockLine(Guid LotId, string LotCode, Guid SourceLotId,
    Guid ProcurementItemId, decimal NetMassKg);
public sealed record ProcurementStockPosition(Guid LocationId, string OnHandKg);

/// <summary>
/// Ledger implements this port without taking ownership of the caller's transaction.
/// A successful return from PostReceiptAsync means every lot and initial posting was written.
/// </summary>
public interface IProcurementLedgerPort
{
    Task<IReadOnlyList<ProcurementReceivingOption>> ListReceivingOptionsAsync(Guid companyId,
        Guid? collectionSiteId, int offset, int limit, CancellationToken ct);
    Task<bool> IsValidReceivingOptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid collectionSiteId, Guid receivingLocationId, CancellationToken ct);
    Task PostReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid actorId, Guid eventId, Guid procurementId, Guid commodityId,
        Guid collectionSiteId, Guid receivingLocationId, IReadOnlyList<ProcurementStockLine> lines,
        CancellationToken ct);
    Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockAsync(Guid companyId, Guid lotId, CancellationToken ct);
}

public sealed class ProcurementLedgerUnavailableException : Exception
{
    public ProcurementLedgerUnavailableException() : base("Protected procurement stock is unavailable.") { }
}

