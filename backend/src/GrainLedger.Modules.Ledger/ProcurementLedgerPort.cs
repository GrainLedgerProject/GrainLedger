using System.Data;
using System.Text.Json;
using System.Globalization;
using GrainLedger.Modules.Capture;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

public sealed class ProcurementLedgerPort(IConfiguration configuration) : IProcurementLedgerPort
{
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<ProcurementReceivingOptionsPage> ListReceivingOptionsAsync(Guid companyId,
        Guid? collectionSiteId, string? search, string sortBy, bool descending, int offset, int limit, CancellationToken ct)
    {
        var order = sortBy switch
        {
            "collectionSiteName" => "f.name",
            "receivingLocationCode" => "l.name",
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy))
        };
        var direction = descending ? "DESC" : "ASC";
        var pattern = string.IsNullOrWhiteSpace(search) ? null :
            "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM ledger.locations l JOIN ledger.facilities f ON f.id=l.facility_id
            WHERE NOT l.is_deleted AND NOT f.is_deleted AND l.kind IN ('STORE','SILO','COMPARTMENT')
              AND (l.owner_id=$1 OR l.owner_id IS NULL AND (l.kind<>'COMPARTMENT' OR l.cleared))
              AND ($2::uuid IS NULL OR f.id=$2)
              AND EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=$1 AND "Kind"='Customer')
              AND ($3::text IS NULL OR f.name ILIKE $3 ESCAPE '\' OR l.name ILIKE $3 ESCAPE '\'
                   OR replace(f.id::text,'-','') ILIKE $3 ESCAPE '\' OR replace(l.id::text,'-','') ILIKE $3 ESCAPE '\')
            """, connection, transaction);
        count.Parameters.AddWithValue(companyId);
        count.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)collectionSiteId ?? DBNull.Value });
        count.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)pattern ?? DBNull.Value });
        var totalCount = Convert.ToInt32(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await using var command = new NpgsqlCommand($"""
            SELECT f.id,f.name,l.id,l.name FROM ledger.locations l JOIN ledger.facilities f ON f.id=l.facility_id
            WHERE NOT l.is_deleted AND NOT f.is_deleted AND l.kind IN ('STORE','SILO','COMPARTMENT')
              AND (l.owner_id=$1 OR l.owner_id IS NULL AND (l.kind<>'COMPARTMENT' OR l.cleared))
              AND ($2::uuid IS NULL OR f.id=$2)
              AND EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=$1 AND "Kind"='Customer')
              AND ($3::text IS NULL OR f.name ILIKE $3 ESCAPE '\' OR l.name ILIKE $3 ESCAPE '\'
                   OR replace(f.id::text,'-','') ILIKE $3 ESCAPE '\' OR replace(l.id::text,'-','') ILIKE $3 ESCAPE '\')
            ORDER BY {order} {direction}, f.id, l.id OFFSET $4 LIMIT $5
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)collectionSiteId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)pattern ?? DBNull.Value });
        command.Parameters.AddWithValue(offset);
        command.Parameters.AddWithValue(limit);
        var options = new List<ProcurementReceivingOption>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                options.Add(new(reader.GetGuid(0), reader.GetGuid(0).ToString("N"), reader.GetString(1),
                    reader.GetGuid(2), reader.GetString(3)));
        }
        await transaction.CommitAsync(ct);
        return new ProcurementReceivingOptionsPage(options, totalCount);
    }

    public async Task<ProcurementListPage> ListProcurementPageAsync(Guid companyId, Guid? farmerId,
        DateTimeOffset? receivedFromInclusive, DateTimeOffset? receivedBeforeExclusive,
        string? search, string sortBy, bool descending, int offset, int limit, CancellationToken ct)
    {
        var order = sortBy switch
        {
            "receivedAt" => "p.received_at",
            "code" => "p.code",
            "status" => "COALESCE(r.status,p.status)",
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy))
        };
        var direction = descending ? "DESC" : "ASC";
        var pattern = string.IsNullOrWhiteSpace(search) ? null :
            "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM capture.procurements p
            JOIN capture.farmers f ON f.company_id=p.company_id AND f.id=p.farmer_id
            LEFT JOIN ledger.receiving r ON r.procurement_id=p.id
            WHERE p.company_id=$1 AND p.status='COMMITTED'
              AND ($2::uuid IS NULL OR p.farmer_id=$2)
              AND ($3::timestamptz IS NULL OR p.received_at >= $3)
              AND ($4::timestamptz IS NULL OR p.received_at < $4)
              AND ($5::text IS NULL OR p.code ILIKE $5 ESCAPE '\' OR f.name ILIKE $5 ESCAPE '\'
                   OR COALESCE(r.status,p.status) ILIKE $5 ESCAPE '\')
            """, connection, transaction);
        AddProcurementFilters(count, companyId, farmerId, receivedFromInclusive, receivedBeforeExclusive, pattern);
        var totalCount = Convert.ToInt32(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await using var command = new NpgsqlCommand($"""
            SELECT p.id,COALESCE(r.status,p.status) FROM capture.procurements p
            JOIN capture.farmers f ON f.company_id=p.company_id AND f.id=p.farmer_id
            LEFT JOIN ledger.receiving r ON r.procurement_id=p.id
            WHERE p.company_id=$1 AND p.status='COMMITTED'
              AND ($2::uuid IS NULL OR p.farmer_id=$2)
              AND ($3::timestamptz IS NULL OR p.received_at >= $3)
              AND ($4::timestamptz IS NULL OR p.received_at < $4)
              AND ($5::text IS NULL OR p.code ILIKE $5 ESCAPE '\' OR f.name ILIKE $5 ESCAPE '\'
                   OR COALESCE(r.status,p.status) ILIKE $5 ESCAPE '\')
            ORDER BY {order} {direction}, p.id OFFSET $6 LIMIT $7
            """, connection, transaction);
        AddProcurementFilters(command, companyId, farmerId, receivedFromInclusive, receivedBeforeExclusive, pattern);
        command.Parameters.AddWithValue(offset);
        command.Parameters.AddWithValue(limit);
        var items = new List<ProcurementListPageItem>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1)));
        }
        await transaction.CommitAsync(ct);
        return new ProcurementListPage(items, totalCount);
    }

    private static void AddProcurementFilters(NpgsqlCommand command, Guid companyId, Guid? farmerId,
        DateTimeOffset? receivedFromInclusive, DateTimeOffset? receivedBeforeExclusive, string? pattern)
    {
        command.Parameters.AddWithValue(companyId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)farmerId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)receivedFromInclusive ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)receivedBeforeExclusive ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)pattern ?? DBNull.Value });
    }

    public async Task<bool> IsValidReceivingOptionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, Guid collectionSiteId, Guid receivingLocationId, CancellationToken ct)
    {
        // Reuse caller connection: lock waiters must not exhaust pool while lock holder opens another one.
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM ledger.locations l JOIN ledger.facilities f ON f.id=l.facility_id
                WHERE l.id=$3 AND l.facility_id=$2 AND NOT l.is_deleted AND NOT f.is_deleted
                AND l.kind IN ('STORE','SILO','COMPARTMENT')
                AND (l.owner_id=$1 OR l.owner_id IS NULL AND (l.kind<>'COMPARTMENT' OR l.cleared)))
            AND EXISTS(SELECT 1 FROM identity.organizations WHERE "Id"=$1 AND "Kind"='Customer')
            """, connection, transaction);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(collectionSiteId);
        command.Parameters.AddWithValue(receivingLocationId);
        return Equals(await command.ExecuteScalarAsync(ct), true);
    }

    public async Task RegisterPendingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid procurementId, CancellationToken ct)
    {
        if (transaction.Connection != connection) throw new InvalidOperationException("Caller-owned transaction required.");
        await using var command = new NpgsqlCommand("SELECT ledger.register_pending_procurement($1,$2)", connection, transaction);
        command.Parameters.AddWithValue(actorId);
        command.Parameters.AddWithValue(procurementId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<string> ReserveSourceLotCodeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid companyId, Guid procurementId, Guid lotId, CancellationToken ct)
    {
        if (transaction.Connection != connection) throw new InvalidOperationException("Caller-owned transaction required.");
        await using var command = new NpgsqlCommand("SELECT ledger.reserve_source_lot_code($1,$2,$3,$4)", connection, transaction);
        command.Parameters.AddWithValue(actorId);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(procurementId);
        command.Parameters.AddWithValue(lotId);
        return (string)(await command.ExecuteScalarAsync(ct)
            ?? throw new ProcurementLedgerUnavailableException());
    }

    public async Task<ProcurementReceivingState?> GetReceivingStateAsync(Guid procurementId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT status,version FROM ledger.receiving WHERE procurement_id=$1", connection);
        command.Parameters.AddWithValue(procurementId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetString(0), reader.GetInt32(1)) : null;
    }

    public Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockAsync(Guid companyId,
        Guid sourceLotId, CancellationToken ct) => GetCurrentStockBulkAsync(companyId, [sourceLotId], ct);

    public async Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockBulkAsync(Guid companyId,
        IReadOnlyCollection<Guid> sourceLotIds, CancellationToken ct)
    {
        var ids = sourceLotIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT source_lot_id,current_stock_lot_id,current_stock_lot_code,location_id,on_hand_kg,available_kg,pool_total_kg,is_mixed_bulk,pool_revision FROM ledger.procurement_stock_bulk($1,$2)",
            connection);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid, Value = ids });
        await using var reader = await command.ExecuteReaderAsync(ct);
        var positions = new List<ProcurementStockPosition>();
        while (await reader.ReadAsync(ct))
        {
            positions.Add(new(reader.GetGuid(1), reader.GetString(2), reader.GetGuid(0), reader.GetGuid(3),
                reader.GetDecimal(4).ToString("0.######", CultureInfo.InvariantCulture),
                reader.GetDecimal(5).ToString("0.######", CultureInfo.InvariantCulture),
                reader.GetDecimal(6).ToString("0.######", CultureInfo.InvariantCulture),
                reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetInt64(8)));
        }
        return positions;
    }

    public async Task<ProcurementSourceLineage> GetSourceLineageAsync(Guid companyId, Guid sourceLotId,
        CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT lot_id,lot_code,location_id,is_current_pool,pool_revision,quantity_kg FROM ledger.procurement_source_lineage($1,$2)",
            connection);
        command.Parameters.AddWithValue(companyId);
        command.Parameters.AddWithValue(sourceLotId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var descendants = new List<ProcurementLineageLink>();
            while (await reader.ReadAsync(ct))
            {
                descendants.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.GetBoolean(3), reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.GetDecimal(5).ToString("0.######", CultureInfo.InvariantCulture)));
            }
            return descendants.Count == 0
                ? throw new ProcurementLedgerUnavailableException()
                : new(descendants);
        }
        catch (PostgresException ex) when (ex.SqlState == "GL001")
        {
            throw new ProcurementLedgerUnavailableException();
        }
    }
}
