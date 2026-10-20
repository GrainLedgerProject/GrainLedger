using System.Text.Json.Nodes;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GrainLedger.Modules.Ledger;

public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ledger").RequireAuthorization().AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>().AddEndpointFilter<LedgerErrorFilter>();
        foreach (var route in new[] { "setup", "lookups" })
        {
            var action = route;
            group.MapGet('/' + route, async (LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.ReadAsync(action, new JsonObject(), ct)));
        }
        foreach (var route in new[] { "lots", "operations", "holds", "corrections", "notifications", "history", "receiving-tasks" })
        {
            var action = route;
            group.MapGet('/' + route, async (HttpRequest request, LedgerService ledger, CancellationToken ct) =>
            {
                var query = new JsonObject();
                foreach (var key in new[] { "page", "pageSize", "search", "sortBy", "sortDirection", "ownerId", "facilityId", "locationId", "lotId", "stage", "status", "kind", "ids", "queueOnly", "availableOnly" })
                {
                    if (!request.Query.TryGetValue(key, out var value)) continue;
                    if (value.Count != 1 || string.IsNullOrWhiteSpace(value[0])) throw new LedgerException(400, "Invalid query value");
                    query[key] = value[0];
                }
                return Results.Ok(await ledger.ReadAsync(action, query, ct));
            });
        }
        group.MapGet("/lots/{id:guid}", async (Guid id, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.ReadAsync("lot", new JsonObject { ["lotId"] = id }, ct)));
        group.MapGet("/operations/{id:guid}", async (Guid id, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.ReadAsync("operation", new JsonObject { ["id"] = id }, ct)));
        group.MapGet("/receiving/{id:guid}", async (Guid id, LedgerService ledger, CancellationToken ct) =>
            Results.Ok(await ledger.ReceivingAsync("detail", new JsonObject { ["id"] = id }, ct)));
        group.MapPost("/receiving/{id:guid}/measurements", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            request["id"] = id;
            return Results.Ok(await ledger.ReceivingAsync("measurements", request, ct));
        }).RequireAuthorization("lots.receive");
        group.MapPost("/receiving/{id:guid}/reviews", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) =>
        {
            request["id"] = id;
            return Results.Ok(await ledger.ReceivingAsync("reviews", request, ct));
        }).RequireAuthorization("receiving.reconcile");
        foreach (var route in new[] { "facilities", "locations", "equipment", "assignments", "policies", "compatibilities", "mixed-bulk-policies" })
        {
            var action = route;
            group.MapPost('/' + route, async (JsonObject request, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.CommandAsync(action, request, ct))).RequireAuthorization("platform.configure");
        }
        group.MapPost("/locations/{id:guid}/clearance", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { request["id"] = id; return Results.Ok(await ledger.CommandAsync("clearance", request, ct)); }).RequireAuthorization("platform.configure");
        group.MapPost("/operations", async (JsonObject request, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.CommandAsync("draft", request, ct))).RequireAuthorization("conversions.write");
        group.MapPut("/operations/{id:guid}", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { LedgerManagementEndpoints.RequireVersion(request); request["id"] = id; return Results.Ok(await ledger.CommandAsync("draft", request, ct)); }).RequireAuthorization("conversions.write");
        group.MapPost("/operations/{id:guid}/confirm", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { request["id"] = id; return Results.Ok(await ledger.CommandAsync("confirm", request, ct)); }).RequireAuthorization("conversions.confirm");
        group.MapPost("/operations/{id:guid}/commit", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { request["id"] = id; return Results.Ok(await ledger.CommandAsync("commit", request, ct)); }).RequireAuthorization("conversions.write");
        group.MapPost("/transfers", async (JsonObject request, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.CommandAsync("transfer", request, ct)));
        group.MapPost("/holds", async (JsonObject request, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.CommandAsync("hold", request, ct))).RequireAuthorization("holds.manage");
        group.MapPost("/holds/{id:guid}/respond", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { request["id"] = id; return Results.Ok(await ledger.CommandAsync("hold_response", request, ct)); }).RequireAuthorization("holds.process");
        group.MapPost("/corrections", async (JsonObject request, LedgerService ledger, CancellationToken ct) => Results.Ok(await ledger.CommandAsync("correction_request", request, ct))).RequireAuthorization("conversions.write");
        foreach (var suffix in new[] { "reconcile", "cancel", "commit" })
        {
            var action = "correction_" + suffix;
            group.MapPost("/corrections/{id:guid}/" + suffix, async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { request["id"] = id; return Results.Ok(await ledger.CommandAsync(action, request, ct)); }).RequireAuthorization(suffix == "commit" ? "conversions.write" : "corrections.reconcile");
        }
        group.MapPost("/notifications/{id:guid}/read", async (Guid id, JsonObject request, LedgerService ledger, CancellationToken ct) => { request["id"] = id; return Results.Ok(await ledger.CommandAsync("notification_read", request, ct)); });
        group.MapPost("/evidence", async (EvidenceRequest request, LedgerService ledger, CancellationToken ct) =>
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(request.ContentBase64); } catch (FormatException) { throw new LedgerException(400, "Invalid evidence base64"); }
            await ledger.StoreEvidenceAsync(request.Id, request.OwnerId, request.FacilityId, request.FileName, request.ContentType, bytes, ct);
            return Results.Ok(new { request.Id, sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) });
        });
        group.MapGet("/evidence/{id:guid}", async (Guid id, LedgerService ledger, CancellationToken ct) => { var evidence = await ledger.ReadEvidenceAsync(id, ct); return Results.File(evidence.Content, evidence.ContentType, evidence.FileName); });
        LedgerManagementEndpoints.MapManagementRoutes(group);
        return endpoints;
    }
}
public sealed record EvidenceRequest(Guid Id, Guid OwnerId, Guid FacilityId, string FileName, string ContentType, string ContentBase64);
public sealed class LedgerErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (LedgerException ex) { return Results.Problem(statusCode: ex.StatusCode, title: ex.Message); }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        { return Results.Problem(statusCode: 400, title: "Invalid request shape or value"); }
    }
}
