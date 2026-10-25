using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GrainLedger.Modules.Ledger;

public static class LedgerManagementEndpoints
{
    public static void RequireVersion(JsonObject request)
    {
        if (request["version"] is not JsonValue value || !value.TryGetValue<string>(out var version) ||
            !Guid.TryParse(version, out _))
            throw new LedgerException(400, "Row version string required");
    }

    public static void MapManagementRoutes(RouteGroupBuilder group)
    {
        foreach (var resource in new[] { "facilities", "locations", "equipment" })
        {
            var action = resource;
            group.MapPut('/' + resource + "/{id:guid}", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) =>
            {
                RequireVersion(request);
                request["id"] = id;
                return Results.Ok(await ledger.CommandAsync(action + "_update", request, ct));
            }).RequireAuthorization("platform.configure");
            group.MapDelete('/' + resource + "/{id:guid}", async (Guid id, [FromBody] JsonObject request, LedgerService ledger, CancellationToken ct) =>
            {
                RequireVersion(request);
                request["id"] = id;
                return Results.Ok(await ledger.CommandAsync(action + "_delete", request, ct));
            }).RequireAuthorization("platform.configure");
        }
        group.MapPost("/equipment/{id:guid}/retire", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireVersion(request);
            request["id"] = id;
            return Results.Ok(await ledger.CommandAsync("equipment_retire", request, ct));
        }).RequireAuthorization("platform.configure");
        group.MapPut("/assignments/{userId:guid}/{facilityId:guid}", async (Guid userId, Guid facilityId, JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireVersion(request);
            request["userId"] = userId;
            request["facilityId"] = facilityId;
            return Results.Ok(await ledger.CommandAsync("assignments_update", request, ct));
        }).RequireAuthorization("platform.configure");
        group.MapDelete("/assignments/{userId:guid}/{facilityId:guid}", async (Guid userId, Guid facilityId, [FromBody] JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireVersion(request);
            request["userId"] = userId;
            request["facilityId"] = facilityId;
            return Results.Ok(await ledger.CommandAsync("assignments_delete", request, ct));
        }).RequireAuthorization("platform.configure");
        group.MapDelete("/operations/{id:guid}", async (Guid id, [FromBody] JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireVersion(request);
            request["id"] = id;
            return Results.Ok(await ledger.CommandAsync("draft_delete", request, ct));
        }).RequireAuthorization("conversions.write");
        group.MapGet("/receiving-thresholds", async (LedgerService ledger, CancellationToken ct) =>
            Results.Ok(await ledger.ReceivingThresholdAsync(false, "settings", new JsonObject(), ct)))
            .RequireAuthorization("platform.configure");
        group.MapGet("/receiving-thresholds/companies", async (HttpRequest request, LedgerService ledger, CancellationToken ct) =>
        {
            var query = ThresholdPageQuery(request, includeSearch: true);
            return Results.Ok(await ledger.ReceivingThresholdAsync(false, "companies", query, ct));
        }).RequireAuthorization("platform.configure");
        group.MapGet("/receiving-thresholds/history", async (HttpRequest request, LedgerService ledger, CancellationToken ct) =>
        {
            var query = ThresholdPageQuery(request, includeSearch: false, includeHistory: true);
            return Results.Ok(await ledger.ReceivingThresholdAsync(false, "history", query, ct));
        }).RequireAuthorization("platform.configure");
        group.MapGet("/receiving-thresholds/own-company", async (LedgerService ledger, CancellationToken ct) =>
            Results.Ok(await ledger.ReceivingThresholdAsync(false, "own-company", new JsonObject(), ct)));
        group.MapPut("/receiving-thresholds/default", async (JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireThresholdChange(request, versionRequired: true);
            return Results.Ok(await ledger.ReceivingThresholdAsync(true, "default", request, ct));
        }).RequireAuthorization("platform.configure");
        group.MapPost("/receiving-thresholds/companies/{companyId:guid}", async (Guid companyId, JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireThresholdChange(request, versionRequired: false);
            request["companyId"] = companyId;
            return Results.Ok(await ledger.ReceivingThresholdAsync(true, "create", request, ct));
        }).RequireAuthorization("platform.configure");
        group.MapPut("/receiving-thresholds/companies/{companyId:guid}", async (Guid companyId, JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireThresholdChange(request, versionRequired: true);
            request["companyId"] = companyId;
            return Results.Ok(await ledger.ReceivingThresholdAsync(true, "update", request, ct));
        }).RequireAuthorization("platform.configure");
        group.MapDelete("/receiving-thresholds/companies/{companyId:guid}", async (Guid companyId, [FromBody] JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            RequireThresholdChange(request, versionRequired: true, percentageRequired: false);
            request["companyId"] = companyId;
            return Results.Ok(await ledger.ReceivingThresholdAsync(true, "delete", request, ct));
        }).RequireAuthorization("platform.configure");
    }

    private static void RequireThresholdChange(JsonObject request, bool versionRequired, bool percentageRequired = true)
    {
        if (percentageRequired && (request["percentage"] is not JsonValue percentage ||
            !percentage.TryGetValue<string>(out _)))
            throw new LedgerException(400, "Percentage must be a base-10 string");
        if (request["reason"] is not JsonValue reason || !reason.TryGetValue<string>(out _))
            throw new LedgerException(400, "Reason string required");
        if (versionRequired) RequireVersion(request);
    }

    private static JsonObject ThresholdPageQuery(HttpRequest request, bool includeSearch, bool includeHistory = false)
    {
        var query = new JsonObject();
        foreach (var key in includeSearch ? new[] { "search", "page", "pageSize" } : new[] { "page", "pageSize" })
            if (request.Query.TryGetValue(key, out var value))
            {
                if (value.Count != 1 || string.IsNullOrWhiteSpace(value[0])) throw new LedgerException(400, "Invalid query value");
                query[key] = value[0];
            }
        if (includeHistory)
            foreach (var key in new[] { "companyId", "scope" })
                if (request.Query.TryGetValue(key, out var value))
                {
                    if (value.Count != 1 || string.IsNullOrWhiteSpace(value[0])) throw new LedgerException(400, "Invalid query value");
                    query[key] = value[0];
                }
        return query;
    }
}
