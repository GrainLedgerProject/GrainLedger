using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

public sealed record PlanningYieldRule(Guid Id, Guid FacilityId, Guid CompatibilityPolicyId,
    int VersionNo, string OutputFraction, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, Guid EvidenceId, DateTimeOffset PublishedAt);

public sealed partial class LedgerService
{
    public async Task<JsonElement> PublishPlanningYieldRuleAsync(JsonObject request,
        CancellationToken ct = default)
    {
        if (request.ContainsKey("actorId") || request.ContainsKey("publishedBy"))
            throw new LedgerException(400, "Actor derives from authenticated identity");
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        object? value;
        try { value = await ExecuteAsync(connection, null,
            "SELECT ledger.publish_planning_yield_rule($1,$2)::text", ct, actor, request); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedFunction)
        { throw new LedgerException(503, "Planning yield rule schema or grant is not ready"); }
        return JsonSerializer.Deserialize<JsonElement>((string)value!);
    }

    public async Task<PlanningYieldRule?> ReadPlanningYieldRuleAsync(Guid ownerId,
        Guid facilityId, Guid compatibilityPolicyId, DateTimeOffset? at,
        CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty || facilityId == Guid.Empty || compatibilityPolicyId == Guid.Empty)
            throw new LedgerException(400, "Owner, facility and compatibility policy are required");
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT ledger.planning_yield_rule($1,$2,$3,$4,COALESCE($5,statement_timestamp()))::text",
            connection);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(facilityId);
        command.Parameters.AddWithValue(compatibilityPolicyId);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = at?.ToUniversalTime() ?? (object)DBNull.Value });
        object? value;
        try { value = await command.ExecuteScalarAsync(ct); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedFunction)
        { throw new LedgerException(503, "Planning yield rule schema or grant is not ready"); }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        { throw new LedgerException(403, "Planning yield access denied"); }
        if (value is null or DBNull) return null;
        var rule = JsonSerializer.Deserialize<PlanningYieldRule>((string)value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return rule with { OutputFraction = decimal.Parse(rule.OutputFraction,
            CultureInfo.InvariantCulture).ToString("0.############", CultureInfo.InvariantCulture) };
    }
}
