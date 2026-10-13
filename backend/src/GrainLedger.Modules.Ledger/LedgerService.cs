using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

public sealed record ReceiptStockLine(Guid ReceiptId, Guid ReceiptLineId, Guid LotId, string Code, Guid OwnerId, Guid CommodityId, Guid LocationId, string QuantityKg, string OriginalQuantity, string OriginalUnit, DateTimeOffset OccurredAt, Guid? ReplacementForReceiptId = null);
public sealed record LedgerEvidence(Guid Id, string FileName, string ContentType, byte[] Content, string Sha256);
public sealed class LedgerException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class LedgerService(IConfiguration configuration, IHttpContextAccessor accessor, IdentityDbContext identity, ICurrentAccess access)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<Guid> ActorAsync(CancellationToken ct = default)
    {
        if (accessor.HttpContext?.User.Identity?.IsAuthenticated != true || !Guid.TryParse(accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !await identity.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsActive && x.Role != null, ct))
            throw new LedgerException(403, "Active persisted actor required");
        return id;
    }
    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        return connection;
    }
    private static async Task<object?> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct, params object?[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            if (value is JsonNode node) command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = node.ToJsonString(JsonOptions) });
            else if (value is JsonElement element) command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = element.GetRawText() });
            else command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
        }
        try { return await command.ExecuteScalarAsync(ct); }
        catch (PostgresException ex) when (ex.SqlState is "23514" or "23505" or "23503" or "23502" or "42501" or "P0002" or "22P02" or "22003" or "22023" or "22007" or "22008")
        { throw new LedgerException(ex.SqlState == "42501" ? 403 : ex.SqlState == "23502" || ex.SqlState.StartsWith("22", StringComparison.Ordinal) ? 400 : 409, ex.MessageText); }
    }
    public async Task<JsonElement> CommandAsync(string action, JsonObject payload, CancellationToken ct = default)
    {
        var actor = await ActorAsync(ct);
        if (payload.ContainsKey("confirmedBy") || payload.ContainsKey("actorId") || payload.ContainsKey("confirmed_by")) throw new LedgerException(400, "Actor and confirmer derive from authenticated identity");
        await using var connection = await OpenAsync(ct);
        var value = await ExecuteAsync(connection, null, "SELECT ledger.command($1,$2,$3)::text", ct, actor, action, payload);
        return JsonSerializer.Deserialize<JsonElement>((string)value!);
    }
    public async Task<JsonElement> ReadAsync(string action, JsonObject payload, CancellationToken ct = default)
    {
        if (accessor.HttpContext?.User.Identity?.IsAuthenticated != true || !Guid.TryParse(accessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            throw new LedgerException(403, "Active persisted actor required");
        // ponytail: ledger.read validates persisted active role and scope in the same database round trip.
        await using var connection = await OpenAsync(ct);
        var value = await ExecuteAsync(connection, null, "SELECT ledger.read($1,$2,$3)::text", ct, actor, action, payload);
        return JsonSerializer.Deserialize<JsonElement>((string)value!);
    }
    public async Task<Guid> PostReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, ReceiptStockLine line, CancellationToken ct = default)
    {
        if (actorId != await ActorAsync(ct)) throw new LedgerException(403, "Receipt actor mismatch");
        var payload = JsonSerializer.SerializeToNode(line, JsonOptions)!;
        return (Guid)(await ExecuteAsync(connection, transaction, "SELECT ledger.receipt($1,$2)", ct, actorId, payload))!;
    }
    public async Task SetReceiptCorrectionBlockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, Guid correctionId, Guid receiptId, Guid ownerId, CancellationToken ct = default)
    {
        if (actorId != await ActorAsync(ct)) throw new LedgerException(403, "Correction actor mismatch");
        await ExecuteAsync(connection, transaction, "SELECT ledger.receipt_correction_request($1,$2,$3,$4)", ct, actorId, correctionId, receiptId, ownerId);
    }
    public async Task ReverseUnusedReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, Guid correctionId, Guid receiptId, Guid ownerId, Guid evidenceId, string reason, CancellationToken ct = default)
    {
        if (actorId != await ActorAsync(ct)) throw new LedgerException(403, "Correction actor mismatch");
        await ExecuteAsync(connection, transaction, "SELECT ledger.reverse_receipt($1,$2,$3,$4,$5,$6)", ct, actorId, correctionId, receiptId, ownerId, evidenceId, reason);
    }
    public async Task SetReceivingBlockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, Guid discrepancyId, Guid lotId, bool blocked, Guid evidenceId, string reason, CancellationToken ct = default)
    {
        if (actorId != await ActorAsync(ct)) throw new LedgerException(403, "Receiving actor mismatch");
        await ExecuteAsync(connection, transaction, "SELECT ledger.receiving_block($1,$2,$3,$4,$5,$6)", ct, actorId, discrepancyId, lotId, blocked, evidenceId, reason);
    }
    public async Task<Guid> GetLocationFacilityAsync(Guid locationId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        var facility = await ExecuteAsync(connection, null, "SELECT facility_id FROM ledger.locations WHERE id=$1", ct, locationId);
        return facility is Guid id ? id : throw new LedgerException(400, "Unknown location");
    }
    public async Task<bool> CanAccessLocationAsync(Guid locationId, string permission, CancellationToken ct = default)
    {
        if (!await access.HasPermissionAsync(permission, ct)) return false;
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        return Equals(await ExecuteAsync(connection, null, "SELECT EXISTS(SELECT 1 FROM ledger.locations l WHERE l.id=$2 AND (ledger.role($1)='PurchasingStaff' OR ledger.assigned($1,l.facility_id)))", ct, actor, locationId), true);
    }
    public async Task<bool> CanReadLotAsync(Guid lotId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        return Equals(await ExecuteAsync(connection, null, "SELECT ledger.can_read_lot($1,$2)", ct, await ActorAsync(ct), lotId), true);
    }
    public async Task<bool> CanAccessEvidenceAsync(Guid evidenceId, Guid ownerId, Guid facilityId, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        return Equals(await ExecuteAsync(connection, null, "SELECT EXISTS(SELECT 1 FROM ledger.evidence WHERE id=$2 AND owner_id=$3 AND facility_id=$4 AND (ledger.own($1,owner_id) OR ledger.role($1)='Administrator' OR ledger.role($1)='MillManager' AND ledger.assigned($1,facility_id) OR actor_id=$1 AND ledger.assigned($1,facility_id)))", ct, await ActorAsync(ct), evidenceId, ownerId, facilityId), true);
    }
    public async Task<Guid> StoreEvidenceAsync(Guid id, Guid ownerId, Guid facilityId, string fileName, string contentType, byte[] content, CancellationToken ct = default)
    {
        if (content.Length is 0 or > 10485760 || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 || string.IsNullOrWhiteSpace(contentType) || contentType.Length > 120) throw new LedgerException(400, "Evidence requires file name, content type and 1–10485760 bytes");
        await CommandAsync("evidence", new JsonObject { ["id"] = id, ["ownerId"] = ownerId, ["facilityId"] = facilityId, ["fileName"] = fileName, ["contentType"] = contentType, ["contentBase64"] = Convert.ToBase64String(content), ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content)) }, ct);
        return id;
    }
    public async Task<LedgerEvidence> ReadEvidenceAsync(Guid id, CancellationToken ct = default)
    {
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT owner_id,facility_id,file_name,content_type,content,sha256 FROM ledger.evidence WHERE id=$1", connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new LedgerException(403, "Evidence access denied");
        var owner = reader.GetGuid(0); var facility = reader.GetGuid(1);
        if (!await CanAccessEvidenceAsync(id, owner, facility, ct)) throw new LedgerException(403, "Evidence access denied");
        return new LedgerEvidence(id, reader.GetString(2), reader.GetString(3), reader.GetFieldValue<byte[]>(4), reader.GetString(5));
    }
}
