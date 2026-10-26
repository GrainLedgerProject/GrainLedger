using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

public sealed record PlanningQualityObservation(Guid Id, Guid LotId,
    Guid AttributeId, int VersionNo, string MeasuredValue, string Unit,
    Guid EvidenceId, DateTimeOffset MeasuredAt, DateTimeOffset ConfirmedAt);

public sealed partial class LedgerService
{
    public async Task<JsonElement> ConfirmPlanningQualityObservationAsync(JsonObject request,
        CancellationToken ct = default)
    {
        if (request.ContainsKey("actorId") || request.ContainsKey("confirmedBy") ||
            request.ContainsKey("confirmedAt"))
            throw new LedgerException(400, "Confirmer derives from authenticated identity");
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        object? value;
        try { value = await ExecuteAsync(connection, null,
            "SELECT ledger.confirm_planning_quality_observation($1,$2)::text",
            ct, actor, request); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedFunction)
        { throw new LedgerException(503, "Planning quality schema or grant is not ready"); }
        return JsonSerializer.Deserialize<JsonElement>((string)value!);
    }

    public async Task<IReadOnlyList<PlanningQualityObservation>> ReadPlanningQualityObservationsAsync(
        Guid ownerId, Guid facilityId, IReadOnlyList<Guid> lotIds,
        DateTimeOffset? at, CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty || facilityId == Guid.Empty || lotIds.Count is < 1 or > 100 ||
            lotIds.Contains(Guid.Empty) || lotIds.Distinct().Count() != lotIds.Count)
            throw new LedgerException(400, "Owner, facility and 1–100 distinct lots are required");
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT ledger.planning_quality_observations($1,$2,$3,$4,$5)::text", connection);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(facilityId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            Value = lotIds.ToArray() });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = at?.ToUniversalTime() ?? (object)DBNull.Value });
        object? value;
        try { value = await command.ExecuteScalarAsync(ct); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedFunction)
        { throw new LedgerException(503, "Planning quality schema or grant is not ready"); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        { throw new LedgerException(403, "Planning quality access denied"); }
        using var document = JsonDocument.Parse((string)value!);
        return document.RootElement.EnumerateArray().Select(row => new PlanningQualityObservation(
            row.GetProperty("id").GetGuid(), row.GetProperty("lot_id").GetGuid(),
            row.GetProperty("attribute_id").GetGuid(), row.GetProperty("version_no").GetInt32(),
            decimal.Parse(row.GetProperty("measured_value").GetString()!,
                CultureInfo.InvariantCulture).ToString("0.############", CultureInfo.InvariantCulture),
            row.GetProperty("unit").GetString()!, row.GetProperty("evidence_id").GetGuid(),
            row.GetProperty("measured_at").GetDateTimeOffset(),
            row.GetProperty("confirmed_at").GetDateTimeOffset())).ToArray();
    }
}
