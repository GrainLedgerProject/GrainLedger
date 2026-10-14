using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Catalogs;

public sealed record CreateRiceVarietyRequest(string? Code, string? Name);
public sealed record UpdateRiceVarietyRequest(string? Name);
public sealed record RiceVarietyResponse(Guid Id, string Code, string Name);
public sealed record RiceVarietyListResponse(IReadOnlyList<RiceVarietyResponse> Items, int Page, int PageSize, bool HasMore);

public static class RiceVarietyEndpoints
{
    public static IEndpointRouteBuilder MapRiceVarietyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/rice-varieties").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Rice varieties");
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization("platform.configure");
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateRiceVarietyRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40)
            errors["code"] = ["Code is required and must not exceed 40 characters."];
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Name is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var variety = new RiceVariety { Id = Guid.NewGuid(), Code = code!, Name = name! };
        if (await db.RiceVarieties.AnyAsync(x => x.Code == variety.Code, ct))
            return Problem(409, "RICE_VARIETY_CODE_EXISTS", "Rice variety code already exists.");
        db.RiceVarieties.Add(variety);
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "RICE_VARIETY_CREATED", ResourceType = "RICE_VARIETY",
            ResourceId = variety.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            AfterState = State(variety)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "RICE_VARIETY_CODE_EXISTS", "Rice variety code already exists.");
        }
        SetEtag(http, variety.Version);
        return Results.Created($"/api/rice-varieties/{variety.Id}", ToResponse(variety));
    }

    private static async Task<IResult> ListAsync(string? q, string? page, string? pageSize,
        ClaimsPrincipal principal, IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        if (await GetActiveActorAsync(principal, identityDb, ct) is null) return Results.Forbid();
        if (!TryPage(page, pageSize, out var currentPage, out var size) || q?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var query = db.RiceVarieties.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size + 1).ToListAsync(ct);
        return Results.Ok(new RiceVarietyListResponse(rows.Take(size).Select(ToResponse).ToList(),
            currentPage, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        if (await GetActiveActorAsync(http.User, identityDb, ct) is null) return Results.Forbid();
        var variety = await db.RiceVarieties.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (variety is null) return Problem(404, "RICE_VARIETY_NOT_FOUND", "Rice variety not found.");
        SetEtag(http, variety.Version);
        return Results.Ok(ToResponse(variety));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateRiceVarietyRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the ETag from GET in the If-Match header.");
        var variety = await db.RiceVarieties.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (variety is null) return Problem(404, "RICE_VARIETY_NOT_FOUND", "Rice variety not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(variety.Version))
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");

        var name = request.Name.Trim();
        if (name == variety.Name)
        {
            SetEtag(http, variety.Version);
            return Results.Ok(ToResponse(variety));
        }
        var before = State(variety);
        variety.Name = name;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "RICE_VARIETY_UPDATED", ResourceType = "RICE_VARIETY",
            ResourceId = variety.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = State(variety)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        }
        SetEtag(http, variety.Version);
        return Results.Ok(ToResponse(variety));
    }

    private static async Task<Guid?> GetActiveActorAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (user?.Role is null || !Roles.All.TryGetValue(user.Role, out var role) ||
            user.Organization?.Kind != role.OrganizationKind) return null;
        return id;
    }

    private static bool TryPage(string? page, string? pageSize, out int currentPage, out int size)
    {
        currentPage = 1;
        size = 20;
        if (page is not null && (!int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out currentPage) || currentPage < 1)) return false;
        if (pageSize is not null && (!int.TryParse(pageSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 100)) return false;
        return currentPage - 1 <= int.MaxValue / size;
    }

    private static string State(RiceVariety variety) => JsonSerializer.Serialize(new { variety.Id, variety.Code, variety.Name });
    private static string Etag(uint version) => $"\"{version}\"";
    private static void SetEtag(HttpContext http, uint version) => http.Response.Headers.ETag = Etag(version);
    private static RiceVarietyResponse ToResponse(RiceVariety x) => new(x.Id, x.Code, x.Name);
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
