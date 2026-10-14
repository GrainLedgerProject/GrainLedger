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

public sealed record CreateQualityAttributeRequest(string? Code, string? Name, string? Unit);
public sealed record UpdateQualityAttributeRequest(string? Name, string? Code, string? Unit);
public sealed record QualityAttributeResponse(Guid Id, string Code, string Name, string Unit);
public sealed record QualityAttributeListResponse(IReadOnlyList<QualityAttributeResponse> Items, int Page, int PageSize, bool HasMore);

public static class QualityAttributeEndpoints
{
    public static IEndpointRouteBuilder MapQualityAttributeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/quality-attributes").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Quality attributes");
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization("platform.configure");
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateQualityAttributeRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var unit = request.Unit?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 60)
            errors["code"] = ["Code is required and must not exceed 60 characters."];
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(unit) || unit.Length > 30)
            errors["unit"] = ["Unit is required and must not exceed 30 characters."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var attribute = new QualityAttribute { Id = Guid.NewGuid(), Code = code!, Name = name!, Unit = unit! };
        if (await db.QualityAttributes.AnyAsync(x => x.Code == attribute.Code, ct))
            return Problem(409, "QUALITY_ATTRIBUTE_CODE_EXISTS", "Quality attribute code already exists.");
        db.QualityAttributes.Add(attribute);
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "QUALITY_ATTRIBUTE_CREATED", ResourceType = "QUALITY_ATTRIBUTE",
            ResourceId = attribute.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            AfterState = State(attribute)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "QUALITY_ATTRIBUTE_CODE_EXISTS", "Quality attribute code already exists.");
        }
        SetEtag(http, attribute.Version);
        return Results.Created($"/api/quality-attributes/{attribute.Id}", ToResponse(attribute));
    }

    private static async Task<IResult> ListAsync(string? q, string? page, string? pageSize,
        ClaimsPrincipal principal, IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        if (await GetActiveActorAsync(principal, identityDb, ct) is null) return Results.Forbid();
        if (!TryPage(page, pageSize, out var currentPage, out var size) || q?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var query = db.QualityAttributes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size + 1).ToListAsync(ct);
        return Results.Ok(new QualityAttributeListResponse(rows.Take(size).Select(ToResponse).ToList(),
            currentPage, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        if (await GetActiveActorAsync(http.User, identityDb, ct) is null) return Results.Forbid();
        var attribute = await db.QualityAttributes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (attribute is null) return Problem(404, "QUALITY_ATTRIBUTE_NOT_FOUND", "Quality attribute not found.");
        SetEtag(http, attribute.Version);
        return Results.Ok(ToResponse(attribute));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateQualityAttributeRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var immutableErrors = new Dictionary<string, string[]>();
        if (request.Code is not null) immutableErrors["code"] = ["Code cannot be changed after creation."];
        if (request.Unit is not null) immutableErrors["unit"] = ["Unit cannot be changed after creation; create a new quality attribute."];
        if (immutableErrors.Count > 0) return Results.ValidationProblem(immutableErrors);
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the ETag from GET in the If-Match header.");
        var attribute = await db.QualityAttributes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (attribute is null) return Problem(404, "QUALITY_ATTRIBUTE_NOT_FOUND", "Quality attribute not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(attribute.Version))
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");

        var name = request.Name.Trim();
        if (name == attribute.Name)
        {
            SetEtag(http, attribute.Version);
            return Results.Ok(ToResponse(attribute));
        }
        var before = State(attribute);
        attribute.Name = name;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "QUALITY_ATTRIBUTE_UPDATED", ResourceType = "QUALITY_ATTRIBUTE",
            ResourceId = attribute.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = State(attribute)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        }
        SetEtag(http, attribute.Version);
        return Results.Ok(ToResponse(attribute));
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

    private static string State(QualityAttribute attribute) => JsonSerializer.Serialize(new { attribute.Id, attribute.Code, attribute.Name, attribute.Unit });
    private static string Etag(uint version) => $"\"{version}\"";
    private static void SetEtag(HttpContext http, uint version) => http.Response.Headers.ETag = Etag(version);
    private static QualityAttributeResponse ToResponse(QualityAttribute x) => new(x.Id, x.Code, x.Name, x.Unit);
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
