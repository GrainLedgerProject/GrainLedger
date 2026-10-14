using System.Text.Json;
using System.Globalization;
using GrainLedger.Modules.Capture;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

public sealed class ProcurementLedgerPort(IConfiguration configuration) : IProcurementLedgerPort
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<IReadOnlyList<ProcurementReceivingOption>> ListReceivingOptionsAsync(Guid companyId,
        Guid? collectionSiteId, int offset, int limit, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT f.id,f.name,l.id,l.name FROM ledger.locations l JOIN ledger.facilities f ON f.id=l.facility_id
            WHERE l.kind IN ('STORE','SILO','COMPARTMENT')
              AND (l.owner_id=$1 OR l.owner_id IS NULL AND (l.kind<>'COMPARTMENT' OR l.cleared))
              AND ($2::uuid IS NULL OR f.id=$2)
              AND EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=$1 AND "Kind"='Customer')
            ORDER BY f.name,f.id,l.name,l.id OFFSET $3 LIMIT $4
            """, connection);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)collectionSiteId ?? DBNull.Value });
        command.Parameters.AddWithValue(offset);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var options = new List<ProcurementReceivingOption>();
        while (await reader.ReadAsync(ct))
            options.Add(new(reader.GetGuid(0), reader.GetGuid(0).ToString("N"), reader.GetString(1),
                reader.GetGuid(2), reader.GetString(3)));
        return options;
    }

    public async Task<bool> IsValidReceivingOptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid collectionSiteId, Guid receivingLocationId, CancellationToken ct)
    {
        // Reuse caller connection: lock waiters must not exhaust pool while lock holder opens another one.
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM ledger.locations WHERE id=$3 AND facility_id=$2
                AND kind IN ('STORE','SILO','COMPARTMENT')
                AND (owner_id=$1 OR owner_id IS NULL AND (kind<>'COMPARTMENT' OR cleared)))
            AND EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=$1 AND "Kind"='Customer')
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(collectionSiteId);
        command.Parameters.AddWithValue(receivingLocationId);
        return Equals(await command.ExecuteScalarAsync(ct), true);
    }

    public async Task PostReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid actorId, Guid eventId, Guid procurementId, Guid commodityId,
        Guid collectionSiteId, Guid receivingLocationId, IReadOnlyList<ProcurementStockLine> lines, CancellationToken ct)
    {
        if (transaction.Connection != connection) throw new InvalidOperationException("Caller-owned transaction required.");
        await using var command = new NpgsqlCommand("SELECT ledger.procurement_receipt($1,$2,$3,$4,$5,$6,$7,$8)", connection, transaction);
        command.Parameters.AddWithValue(actorId);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(procurementId);
        command.Parameters.AddWithValue(commodityId);
        command.Parameters.AddWithValue(collectionSiteId);
        command.Parameters.AddWithValue(receivingLocationId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = JsonSerializer.Serialize(lines, JsonOptions) });
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockAsync(Guid companyId, Guid lotId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT p.location_id,p.quantity_kg FROM capture.source_lots s
            LEFT JOIN ledger.lots l ON l.id=s.lot_id AND l.owner_id=s.company_id
            LEFT JOIN ledger.positions p ON p.lot_id=l.id
            WHERE s.lot_id=$2 AND s.company_id=$1 ORDER BY p.location_id
            """, connection);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(lotId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var positions = new List<ProcurementStockPosition>();
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0)) throw new ProcurementLedgerUnavailableException();
            positions.Add(new(reader.GetGuid(0), reader.GetDecimal(1).ToString("0.######", CultureInfo.InvariantCulture)));
        }
        return positions;
    }
}
