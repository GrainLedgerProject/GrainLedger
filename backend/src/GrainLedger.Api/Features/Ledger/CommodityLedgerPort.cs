using GrainLedger.Api.Features.Catalogs;
using Npgsql;

namespace GrainLedger.Api.Features.Ledger;

public sealed class CommodityLedgerPort
{
    public async Task ProvisionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, CancellationToken ct)
    {
        if (transaction.Connection != connection) throw new InvalidOperationException("Caller-owned transaction required.");
        await using var command = new NpgsqlCommand("SELECT grainledger.ledger_provision_catalog_commodities($1)", connection, transaction);
        command.Parameters.AddWithValue(actorId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
