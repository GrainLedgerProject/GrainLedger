using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrainLedger.Api.Features.Identity;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.Features.Ledger;

public static class ResidueRiskEndpoints
{
    public static void MapResidueRiskRoutes(this IEndpointRouteBuilder endpoints)
    {
        var changes = endpoints.MapGroup("/api/residue-changes").WithTags("Residue change history")
            .RequireAuthorization(Permissions.ResidueEvaluate, Permissions.TraceReadTenant, Permissions.ExportOrdersRead)
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<LedgerErrorFilter>();
        changes.MapGet("/{changeId:guid}", (Guid changeId, HttpContext http, ResidueRiskReadService service, CancellationToken ct) =>
            service.ReadAsync(http, "change", changeId, ct));
        changes.MapGet("/{changeId:guid}/targets", (Guid changeId, HttpContext http, ResidueRiskReadService service, CancellationToken ct) =>
            service.ReadAsync(http, "targets", changeId, ct));
        var risks = endpoints.MapGroup("/api/residue-risk-events").WithTags("Residue risk history")
            .RequireAuthorization(Permissions.ResidueEvaluate, Permissions.TraceReadTenant, Permissions.ExportOrdersRead)
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<LedgerErrorFilter>();
        risks.MapGet("", (HttpContext http, ResidueRiskReadService service, CancellationToken ct) => service.ReadAsync(http, "risks", null, ct));
        risks.MapGet("/{eventId:guid}", (Guid eventId, HttpContext http, ResidueRiskReadService service, CancellationToken ct) =>
            service.ReadAsync(http, "risk", eventId, ct));
    }
}

public sealed class ResidueRiskReadService(NpgsqlDataSource dataSource)
{
    public async Task<IResult> ReadAsync(HttpContext http, string action, Guid? target, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        if (target == Guid.Empty) throw new LedgerException(400, "Nonempty resource ID required");
        var payload = ValidateQuery(http.Request.Query, action);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx)) await command.ExecuteNonQueryAsync(ct);
        // Fail closed on older DBs: the foundation reader returned private rows,
        // so never fall back to it before the public projection migration exists.
        await using (var ready = new NpgsqlCommand("SELECT to_regprocedure('grainledger.ledger_residue_public_risk(uuid,boolean)') IS NOT NULL", connection, tx))
            if (!(bool)(await ready.ExecuteScalarAsync(ct))!)
                return Results.Problem(statusCode: 503, title: "Residue read schema is not deployed");
        await using var read = new NpgsqlCommand("SELECT grainledger.ledger_residue_change_read($1,$2,$3,$4)::text", connection, tx);
        read.Parameters.AddWithValue(actor); read.Parameters.AddWithValue(action);
        read.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)target ?? DBNull.Value });
        read.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload.ToJsonString());
        try
        {
            using var document = JsonDocument.Parse((string)(await read.ExecuteScalarAsync(ct))!);
            var result = document.RootElement.Clone();
            await tx.CommitAsync(ct);
            return Results.Ok(result);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        { return Results.Forbid(); }
        catch (PostgresException ex) when (ex.SqlState == "P0002")
        { return Results.Problem(statusCode: 404, title: "Residue resource not found"); }
        catch (PostgresException ex) when (ex.SqlState is "22023" or "22P02" or "22003" or "22007" or "22008")
        { return Results.Problem(statusCode: 400, title: "Invalid residue query"); }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "42P01")
        { return Results.Problem(statusCode: 503, title: "Residue read schema is not deployed"); }
    }

    private static JsonObject ValidateQuery(IQueryCollection query, string action)
    {
        string[] allowed = action switch
        {
            "targets" => ["page", "pageSize", "targetKind", "targetId", "state"],
            "risks" => ["page", "pageSize", "targetKind", "targetId", "assessmentStatus", "from", "to"],
            _ => []
        };
        var payload = new JsonObject();
        foreach (var (key, values) in query)
        {
            if (!allowed.Contains(key, StringComparer.Ordinal) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
                throw new LedgerException(400, "Unknown, repeated or empty residue query parameter");
            var value = values[0]!;
            switch (key)
            {
                case "page": case "pageSize":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number > (key == "page" ? 1000000 : 100))
                        throw new LedgerException(400, "Invalid residue pagination");
                    payload[key] = number; break;
                case "targetId":
                    if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty) throw new LedgerException(400, "Invalid target ID");
                    payload[key] = id; break;
                case "targetKind":
                    if (value is not ("LOT" or "PLAN" or "CONTAINER")) throw new LedgerException(400, "Invalid target kind");
                    payload[key] = value; break;
                case "assessmentStatus":
                    if (value is not ("COMPLETE" or "UNRESOLVED" or "PASS" or "FAIL" or "INCOMPLETE")) throw new LedgerException(400, "Invalid assessment status");
                    payload[key] = value; break;
                case "state":
                    if (value is not ("PENDING" or "PROCESSING" or "COMPLETED" or "FAILED_RETRYABLE")) throw new LedgerException(400, "Invalid target state");
                    payload[key] = value; break;
                case "from": case "to":
                    if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
                        || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                        throw new LedgerException(400, "Timestamp requires ISO 8601 with an explicit offset");
                    payload[key] = timestamp.ToUniversalTime(); break;
            }
        }
        if (payload["from"] is { } start && payload["to"] is { } end && start.GetValue<DateTimeOffset>() >= end.GetValue<DateTimeOffset>())
            throw new LedgerException(400, "From must precede to");
        return payload;
    }
}
