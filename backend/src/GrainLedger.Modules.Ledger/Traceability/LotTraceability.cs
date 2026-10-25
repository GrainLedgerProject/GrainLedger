using System.Data;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Ledger;

public sealed record LotTraceRequest(Guid LotId, string Direction = "forward", string View = "current");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LabTraceRequest(Guid ReportId, Guid ResultId, string View = "current");
public sealed record TraceStartQuery(string Mode = "pools", string? Search = null, Guid? FacilityId = null,
    Guid? LocationId = null, int Page = 1, int PageSize = 25);

public static class LotTraceability
{
    private static async Task<bool> CanTraceAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync(Permissions.TraceReadTenant, ct) || await access.HasPermissionAsync(Permissions.TraceReadAll, ct);

    public static void MapTraceability(this RouteGroupBuilder group)
    {
        group.MapGet("/trace/starts", async (HttpRequest http, [AsParameters] TraceStartQuery query, LedgerService ledger,
            IConfiguration configuration, ICurrentAccess access, CancellationToken ct) =>
        {
            if (!await CanTraceAsync(access, ct)) return Results.Forbid();
            if (http.Query.Keys.Any(key => !new[] { "mode", "search", "facilityId", "locationId", "page", "pageSize" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                || query.Mode is not ("pools" or "sources" or "all") || query.Page is < 1 or > 1000000 || query.PageSize is < 1 or > 100
                || query.Search?.Length > 200 || query.FacilityId == Guid.Empty || query.LocationId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Invalid trace starting-lot filters.");
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
            await connection.OpenAsync(ct);
            try
            {
                await using var command = new NpgsqlCommand("SELECT ledger.trace_starts($1,$2,$3,$4,$5,$6,$7)::text", connection) { CommandTimeout = 25 };
                command.Parameters.AddWithValue(await ledger.ActorAsync(ct)); command.Parameters.AddWithValue(query.Mode);
                command.Parameters.AddWithValue(query.Search ?? "");
                command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)query.FacilityId ?? DBNull.Value);
                command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)query.LocationId ?? DBNull.Value);
                command.Parameters.AddWithValue(query.Page); command.Parameters.AddWithValue(query.PageSize);
                return Results.Ok(JsonNode.Parse((string)(await command.ExecuteScalarAsync(ct))!));
            }
            catch (PostgresException ex) when (ex.SqlState is "42501" or "22023" or "42883" or "57014")
            { return Error(ex); }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            { return Results.Problem(statusCode: 503, title: "Starting lots did not load; narrow the filters and retry."); }
        });
        group.MapPost("/trace", async (LotTraceRequest request, LedgerService ledger, IConfiguration configuration,
            ICurrentAccess access, ISourceProvenanceReader sources, IConfirmedLabTraceReader lab, CancellationToken ct) =>
            await RunAsync(request, null, ledger, configuration, access, sources, lab, ct));
        group.MapPost("/trace/from-lab", async (LabTraceRequest request, LedgerService ledger, IConfiguration configuration,
            ICurrentAccess access, ISourceProvenanceReader sources, IConfirmedLabTraceReader lab, CancellationToken ct) =>
            await RunAsync(new(Guid.Empty, "forward", request.View), request, ledger, configuration, access, sources, lab, ct));
    }

    private static async Task<IResult> RunAsync(LotTraceRequest request, LabTraceRequest? labRequest, LedgerService ledger,
        IConfiguration configuration, ICurrentAccess access, ISourceProvenanceReader sources, IConfirmedLabTraceReader lab, CancellationToken ct)
    {
        if (!await CanTraceAsync(access, ct)) return Results.Forbid();
        if (labRequest is not null && !await access.HasPermissionAsync(Permissions.LabRead, ct)
            && !await access.HasPermissionAsync(Permissions.LabWrite, ct)) return Results.Forbid();
        if (labRequest is null && request.LotId == Guid.Empty || request.Direction is not ("forward" or "backward")
            || request.View is not ("current" or "history") || labRequest is not null && (labRequest.ReportId == Guid.Empty || labRequest.ResultId == Guid.Empty))
            return Results.Problem(statusCode: 400, title: "Valid Ledger lot or confirmed report/result IDs and trace view are required.");
        var actor = await ledger.ActorAsync(ct);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await using (var timeout = new NpgsqlCommand("SET LOCAL statement_timeout='25s'", connection, transaction))
                await timeout.ExecuteNonQueryAsync(ct);
            ConfirmedLabTraceOrigin? origin = null;
            if (labRequest is not null)
            {
                origin = await lab.ReadAsync(connection, transaction, actor, labRequest.ReportId, labRequest.ResultId, ct);
                if (origin is null) return Results.Problem(statusCode: 404, title: "Confirmed laboratory result unavailable in current scope.");
                await using var accepted = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM ledger.lots WHERE id=$1 AND owner_id=$2)", connection, transaction);
                accepted.Parameters.AddWithValue(origin.LotId); accepted.Parameters.AddWithValue(origin.OwnerId);
                if (!Equals(await accepted.ExecuteScalarAsync(ct), true))
                    return Results.Problem(statusCode: 409, title: "The sampled source has no accepted Ledger lot yet.");
                request = request with { LotId = origin.LotId };
            }
            await using var command = new NpgsqlCommand("SELECT ledger.trace_lot($1,$2,$3,$4)::text", connection, transaction) { CommandTimeout = 30 };
            command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(request.LotId);
            command.Parameters.AddWithValue(request.Direction); command.Parameters.AddWithValue(request.View);
            var result = JsonNode.Parse((string)(await command.ExecuteScalarAsync(ct))!)!.AsObject();
            if (result["schemaVersion"]?.GetValue<int>() != 2)
                return Results.Problem(statusCode: 503, title: "Custody trace upgrade is not installed. Run approved owner migrations and runtime-role configuration.");
            if (result["sourceContributionsVisible"]!.GetValue<bool>())
            {
                var sourceIds = result["lots"]!.AsArray().SelectMany(l => l!["contributions"]!.AsArray())
                    .Select(c => c!["sourceLotId"]!.GetValue<Guid>()).Distinct().ToArray();
                var metadata = (await sources.ReadAsync(connection, transaction, actor, result["ownerId"]!.GetValue<Guid>(), sourceIds, ct)).ToDictionary(s => s.LotId);
                result["sources"] = new JsonArray(sourceIds.Order().Select(id =>
                {
                    metadata.TryGetValue(id, out var source);
                    return (JsonNode)new JsonObject { ["lotId"] = id, ["status"] = source is null ? "LEGACY_SOURCE_UNAVAILABLE" : "RECORDED",
                        ["provenance"] = JsonSerializer.SerializeToNode(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
                }).ToArray());
            }
            else result["sources"] = new JsonArray();
            if (origin is not null)
                result["labOrigin"] = JsonSerializer.SerializeToNode(origin, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await using var authorize = new NpgsqlCommand("""
                SELECT CASE WHEN $3 THEN ledger.role($1) IN ('Administrator','CustomerManager')
                    ELSE ledger.role($1) IN ('Administrator','CustomerManager','ExportStaff') END
                    AND (ledger.role($1)='Administrator' OR ledger.own($1,$2))
                """, connection, transaction);
            authorize.Parameters.AddWithValue(actor); authorize.Parameters.AddWithValue(result["ownerId"]!.GetValue<Guid>());
            authorize.Parameters.AddWithValue(result["sourceContributionsVisible"]!.GetValue<bool>());
            if (!Equals(await authorize.ExecuteScalarAsync(ct), true)) return Results.Forbid();
            if (labRequest is not null && await lab.ReadAsync(connection, transaction, actor, labRequest.ReportId, labRequest.ResultId, ct) is null)
                return Results.Forbid();
            await transaction.CommitAsync(ct);
            return Results.Ok(result);
        }
        catch (PostgresException ex) when (ex.SqlState is "42501" or "22023" or "23514" or "42883" or "57014") { return Error(ex); }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        { return Results.Problem(statusCode: 503, title: "Trace did not complete; retry with a narrower starting lot."); }
    }

    private static IResult Error(PostgresException ex) => Results.Problem(
        statusCode: ex.SqlState == "42501" ? 403 : ex.SqlState == "22023" ? 400 : ex.SqlState is "57014" or "42883" ? 503 : 409,
        title: ex.SqlState == "42883" ? "Trace feature upgrade is not installed. Run approved owner migrations and runtime-role configuration."
            : ex.SqlState == "57014" ? "Trace did not complete; narrow the filters and retry." : ex.MessageText);
}
