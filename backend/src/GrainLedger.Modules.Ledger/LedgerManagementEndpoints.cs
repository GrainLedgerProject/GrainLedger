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
    }
}
