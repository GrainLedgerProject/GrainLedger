using GrainLedger.Modules.Catalogs;
using Npgsql;

namespace GrainLedger.Modules.Ledger;

public sealed class CommodityLedgerPort : ICommodityLedgerPort
{
    public async Task ProvisionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, CancellationToken ct)
    {
        if (transaction.Connection != connection) throw new InvalidOperationException("Caller-owned transaction required.");
        await using var command = new NpgsqlCommand("SELECT ledger.provision_catalog_commodities($1)", connection, transaction);
        command.Parameters.AddWithValue(actorId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
