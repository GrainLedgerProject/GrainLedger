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

public sealed record CreateMeasurementUnitRequest(string? Code, string? Name, string? QuantityKind);
public sealed record UpdateMeasurementUnitRequest(string? Name, string? Code, string? QuantityKind);
public sealed record MeasurementUnitResponse(Guid Id, string Code, string Name, string QuantityKind);
public sealed record MeasurementUnitListResponse(IReadOnlyList<MeasurementUnitResponse> Items, int Page, int PageSize, bool HasMore);

public static class MeasurementUnitEndpoints
{
    public static IEndpointRouteBuilder MapMeasurementUnitEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/measurement-units").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Measurement units");
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization("platform.configure");
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateMeasurementUnitRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var quantityKind = request.QuantityKind?.Trim().ToUpperInvariant();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 30)
            errors["code"] = ["Code is required and must not exceed 30 characters."];
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Name is required."];
        if (!IsSupportedKind(quantityKind))
            errors["quantityKind"] = ["Use MASS, AREA, RESIDUE_CONCENTRATION, APPLICATION_RATE, or FRACTION."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var unit = new MeasurementUnit { Id = Guid.NewGuid(), Code = code!, Name = name!, QuantityKind = quantityKind! };
        if (await db.MeasurementUnits.AnyAsync(x => x.Code == unit.Code, ct))
            return Problem(409, "MEASUREMENT_UNIT_CODE_EXISTS", "Measurement unit code already exists.");
        db.MeasurementUnits.Add(unit);
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "MEASUREMENT_UNIT_CREATED", ResourceType = "MEASUREMENT_UNIT",
            ResourceId = unit.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            AfterState = State(unit)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "MEASUREMENT_UNIT_CODE_EXISTS", "Measurement unit code already exists.");
        }
        SetEtag(http, unit.Version);
        return Results.Created($"/api/measurement-units/{unit.Id}", ToResponse(unit));
    }

    private static async Task<IResult> ListAsync(string? q, string? page, string? pageSize,
        ClaimsPrincipal principal, IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        if (await GetActiveActorAsync(principal, identityDb, ct) is null) return Results.Forbid();
        if (!TryPage(page, pageSize, out var currentPage, out var size) || q?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var query = db.MeasurementUnits.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size + 1).ToListAsync(ct);
        return Results.Ok(new MeasurementUnitListResponse(rows.Take(size).Select(ToResponse).ToList(),
            currentPage, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        if (await GetActiveActorAsync(http.User, identityDb, ct) is null) return Results.Forbid();
        var unit = await db.MeasurementUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (unit is null) return Problem(404, "MEASUREMENT_UNIT_NOT_FOUND", "Measurement unit not found.");
        SetEtag(http, unit.Version);
        return Results.Ok(ToResponse(unit));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateMeasurementUnitRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var immutableErrors = new Dictionary<string, string[]>();
        if (request.Code is not null) immutableErrors["code"] = ["Code cannot be changed after creation."];
        if (request.QuantityKind is not null) immutableErrors["quantityKind"] = ["Quantity kind cannot be changed after creation; create a new measurement unit."];
        if (immutableErrors.Count > 0) return Results.ValidationProblem(immutableErrors);
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the ETag from GET in the If-Match header.");
        var unit = await db.MeasurementUnits.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (unit is null) return Problem(404, "MEASUREMENT_UNIT_NOT_FOUND", "Measurement unit not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(unit.Version))
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");

        var name = request.Name.Trim();
        if (name == unit.Name)
        {
            SetEtag(http, unit.Version);
            return Results.Ok(ToResponse(unit));
        }
        var before = State(unit);
        unit.Name = name;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "MEASUREMENT_UNIT_UPDATED", ResourceType = "MEASUREMENT_UNIT",
            ResourceId = unit.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = State(unit)
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        }
        SetEtag(http, unit.Version);
        return Results.Ok(ToResponse(unit));
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

    private static bool IsSupportedKind(string? kind) => kind is
        "MASS" or "AREA" or "RESIDUE_CONCENTRATION" or "APPLICATION_RATE" or "FRACTION";
    private static string State(MeasurementUnit unit) => JsonSerializer.Serialize(new { unit.Id, unit.Code, unit.Name, unit.QuantityKind });
    private static string Etag(uint version) => $"\"{version}\"";
    private static void SetEtag(HttpContext http, uint version) => http.Response.Headers.ETag = Etag(version);
    private static MeasurementUnitResponse ToResponse(MeasurementUnit x) => new(x.Id, x.Code, x.Name, x.QuantityKind);
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
