using System.Text.Json.Nodes;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GrainLedger.Modules.Ledger;

public static class ContainerEndpoints
{
    public static void MapContainers(this RouteGroupBuilder ledger)
    {
        var group = ledger.MapGroup("/containers").WithTags("Containers");
        group.MapGet("", async (HttpRequest request, LedgerService service, CancellationToken ct) =>
        {
            var query = new JsonObject();
            foreach (var key in new[] { "page", "pageSize", "search" })
                if (request.Query.TryGetValue(key, out var value))
                {
                    if (value.Count != 1 || string.IsNullOrWhiteSpace(value[0])) throw new LedgerException(400, "Invalid query value");
                    query[key] = value[0];
                }
            return Results.Ok(await service.ContainerAsync("list", query, ct));
        });
        group.MapPost("", async (JsonObject body, LedgerService service, CancellationToken ct) =>
            Results.Ok(await service.ContainerAsync("create", body, ct))).RequireAuthorization(Permissions.ContainersWrite);
        group.MapGet("/{id:guid}", async (Guid id, LedgerService service, CancellationToken ct) =>
            Results.Ok(await service.ContainerAsync("detail", new JsonObject { ["id"] = id }, ct)));
        group.MapGet("/{id:guid}/history", async (Guid id, HttpRequest request, LedgerService service, CancellationToken ct) =>
        {
            var query = new JsonObject { ["id"] = id };
            foreach (var key in new[] { "page", "pageSize" })
                if (request.Query.TryGetValue(key, out var value))
                {
                    if (value.Count != 1 || string.IsNullOrWhiteSpace(value[0])) throw new LedgerException(400, "Invalid query value");
                    query[key] = value[0];
                }
            return Results.Ok(await service.ContainerAsync("history", query, ct));
        });
        foreach (var action in new[] { "metadata", "loading-draft", "loading-check", "close-check", "commit-loading", "close" })
        {
            var operation = action;
            var route = group.MapPost("/{id:guid}/" + operation, async (Guid id, JsonObject body, LedgerService service, CancellationToken ct) =>
            {
                if (body.ContainsKey("id")) throw new LedgerException(400, "Container ID belongs in the route");
                body["id"] = id;
                var result = await service.ContainerAsync(operation, body, ct);
                // These transitions have no stock/assessment provider yet. They fail closed in SQL too.
                return operation is "commit-loading" or "close" ? Results.Json(result, statusCode: 409) : Results.Ok(result);
            });
            if (operation is not ("loading-check" or "close-check")) route.RequireAuthorization(Permissions.ContainersWrite);
        }
    }
}
