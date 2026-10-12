using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Capture;

public sealed record CreateCropSeasonRequest(Guid CompanyId, string? Name, DateOnly StartsOn, DateOnly EndsOn);
public sealed record CropSeasonResponse(Guid Id, Guid CompanyId, string Name, DateOnly StartsOn, DateOnly EndsOn);
public sealed record CropSeasonListResponse(IReadOnlyList<CropSeasonResponse> Items, int Page, int PageSize, bool HasMore);

public static class CropSeasonEndpoints
{
    public static IEndpointRouteBuilder MapCropSeasonEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/crop-seasons").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateCropSeasonRequest request,
        ClaimsPrincipal principal, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) ||
            !await identityDb.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.IsActive, ct)) return Results.Forbid();
        var errors = new Dictionary<string, string[]>();
        if (request.CompanyId == Guid.Empty) errors["companyId"] = ["Customer company ID is required."];
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Season name is required."];
        if (request.StartsOn == default) errors["startsOn"] = ["Start date is required."];
        if (request.EndsOn == default || request.EndsOn < request.StartsOn)
            errors["endsOn"] = ["End date must not precede start date."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var companyId = request.CompanyId;
        if (!await identityDb.Organizations.AsNoTracking().AnyAsync(x => x.Id == companyId && x.Kind == OrganizationKind.Customer, ct))
            return Results.Problem(statusCode: 404, title: "Customer company not found.",
                extensions: new Dictionary<string, object?> { ["code"] = "CUSTOMER_COMPANY_NOT_FOUND" });

        var now = clock.GetUtcNow();
        var season = new CropSeason { Id = Guid.NewGuid(), CompanyId = companyId,
            Name = request.Name!.Trim(), StartsOn = request.StartsOn, EndsOn = request.EndsOn };
        var result = new CropSeasonResponse(season.Id, companyId, season.Name, season.StartsOn, season.EndsOn);
        db.CropSeasons.Add(season);
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = companyId, ActorId = actorId, EventType = "CROP_SEASON_CREATED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, seasonId = season.Id,
                season.Name, season.StartsOn, season.EndsOn })
        });
        await db.SaveChangesAsync(ct);
        return Results.Json(result, statusCode: 201);
    }

    private static async Task<IResult> ListAsync(string? companyId, string? q, int? page, int? pageSize,
        HttpRequest request, ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        Guid? requestedCompanyId = null;
        if (request.Query.ContainsKey("companyId"))
        {
            if (!Guid.TryParse(companyId, out var parsedCompanyId) || parsedCompanyId == Guid.Empty)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["companyId"] = ["Enter a valid customer company ID."] });
            requestedCompanyId = parsedCompanyId;
        }
        Guid targetCompanyId;
        if (await access.HasPermissionAsync("platform.configure", ct))
        {
            if (requestedCompanyId is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["companyId"] = ["Select a customer company."] });
            targetCompanyId = requestedCompanyId.Value;
            if (!await identityDb.Organizations.AsNoTracking().AnyAsync(x => x.Id == targetCompanyId && x.Kind == OrganizationKind.Customer, ct))
                return Results.Problem(statusCode: 404, title: "Customer company not found.",
                    extensions: new Dictionary<string, object?> { ["code"] = "CUSTOMER_COMPANY_NOT_FOUND" });
        }
        else
        {
            if (!await access.HasPermissionAsync("farms.read", ct) && !await access.HasPermissionAsync("farms.write", ct)) return Results.Forbid();
            var ownCompanyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
            if (ownCompanyId is null || !await access.CanAccessOrganizationAsync(ownCompanyId.Value, ct)) return Results.Forbid();
            if (requestedCompanyId is not null && requestedCompanyId != ownCompanyId) return Results.Forbid();
            targetCompanyId = ownCompanyId.Value;
        }
        if (page is < 1 || pageSize is < 1 or > 100 || q?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 20;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var query = db.CropSeasons.AsNoTracking().Where(x => x.CompanyId == targetCompanyId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var rows = await query.OrderByDescending(x => x.StartsOn).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size + 1).ToListAsync(ct);
        return Results.Ok(new CropSeasonListResponse(rows.Take(size).Select(x =>
            new CropSeasonResponse(x.Id, x.CompanyId, x.Name, x.StartsOn, x.EndsOn)).ToList(),
            currentPage, size, rows.Count > size));
    }
}

internal static class CaptureActor
{
    public static async Task<Guid?> GetCustomerCompanyAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        return user?.Organization?.Kind == OrganizationKind.Customer ? user.OrganizationId : null;
    }
}
