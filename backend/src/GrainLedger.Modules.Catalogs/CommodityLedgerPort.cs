using Npgsql;

namespace GrainLedger.Modules.Catalogs;

public interface ICommodityLedgerPort
{
    Task ProvisionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, CancellationToken ct);
}
