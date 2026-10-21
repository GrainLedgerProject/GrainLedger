using System.Data;
using System.Text.Json.Nodes;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GrainLedger.Modules.Ledger;

public sealed record LotTraceRequest(Guid LotId, string Direction = "forward", string View = "current");

public static class LotTraceability
{
    public static void MapTraceability(this RouteGroupBuilder group)
    {
        group.MapPost("/trace", async (LotTraceRequest request, LedgerService ledger,
            IConfiguration configuration, ICurrentAccess access, ISourceProvenanceReader sources, CancellationToken ct) =>
        {
            if (!await access.HasPermissionAsync(Permissions.TraceReadTenant, ct)
                && !await access.HasPermissionAsync(Permissions.TraceReadAll, ct)) return Results.Forbid();
            if (request.LotId == Guid.Empty || request.Direction is not ("forward" or "backward") || request.View is not ("current" or "history"))
                return Results.Problem(statusCode: 400, title: "A Ledger lot ID, forward/backward direction and current/history view are required.");
            var actor = await ledger.ActorAsync(ct);
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                await using (var timeout = new NpgsqlCommand("SET LOCAL statement_timeout='25s'", connection, transaction))
                    await timeout.ExecuteNonQueryAsync(ct);
                await using var command = new NpgsqlCommand("SELECT ledger.trace_lot($1,$2,$3,$4)::text", connection, transaction) { CommandTimeout = 30 };
                command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(request.LotId);
                command.Parameters.AddWithValue(request.Direction); command.Parameters.AddWithValue(request.View);
                var result = JsonNode.Parse((string)(await command.ExecuteScalarAsync(ct))!)!.AsObject();
                if (result["schemaVersion"]?.GetValue<int>() != 2)
                    return Results.Problem(statusCode: 503,
                        title: "Custody trace upgrade is not installed. Run approved owner migrations and runtime-role configuration.");
                if (result["sourceContributionsVisible"]!.GetValue<bool>())
                {
                    var sourceIds = result["lots"]!.AsArray().SelectMany(l => l!["contributions"]!.AsArray())
                        .Select(c => c!["sourceLotId"]!.GetValue<Guid>()).Distinct().ToArray();
                    var metadata = (await sources.ReadAsync(connection, transaction, actor, result["ownerId"]!.GetValue<Guid>(), sourceIds, ct)).ToDictionary(s => s.LotId);
                    result["sources"] = new JsonArray(sourceIds.Order().Select(id =>
                    {
                        metadata.TryGetValue(id, out var source);
                        return (JsonNode)new JsonObject { ["lotId"] = id,
                            ["status"] = source is null ? "LEGACY_SOURCE_UNAVAILABLE" : "RECORDED",
                            ["provenance"] = JsonSerializer.SerializeToNode(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
                    }).ToArray());
                }
                else result["sources"] = new JsonArray();
                // A role/owner change between graph capture and immutable source enrichment
                // must fail rather than relabel restricted source facts as legacy/missing.
                await using var authorize = new NpgsqlCommand("""
                    SELECT CASE WHEN $3 THEN ledger.role($1) IN ('Administrator','CustomerManager')
                        ELSE ledger.role($1) IN ('Administrator','CustomerManager','ExportStaff') END
                        AND (ledger.role($1)='Administrator' OR ledger.own($1,$2))
                    """, connection, transaction);
                authorize.Parameters.AddWithValue(actor); authorize.Parameters.AddWithValue(result["ownerId"]!.GetValue<Guid>());
                authorize.Parameters.AddWithValue(result["sourceContributionsVisible"]!.GetValue<bool>());
                if (!Equals(await authorize.ExecuteScalarAsync(ct), true)) return Results.Forbid();
                await transaction.CommitAsync(ct);
                return Results.Ok(result);
            }
            catch (PostgresException ex) when (ex.SqlState is "42501" or "22023" or "23514" or "57014")
            {
                return Results.Problem(statusCode: ex.SqlState == "42501" ? 403 : ex.SqlState == "22023" ? 400 : ex.SqlState == "57014" ? 503 : 409,
                    title: ex.SqlState == "57014" ? "Trace did not complete; retry with a narrower starting lot." : ex.MessageText);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                return Results.Problem(statusCode: 503, title: "Trace did not complete; retry with a narrower starting lot.");
            }
        });
    }
}
