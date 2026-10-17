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
public sealed record CropSeasonReferenceItem(Guid Id, Guid CompanyId, string Name, DateOnly StartsOn,
    DateOnly EndsOn, Guid? CreatedBy, int Version, bool CanEdit, bool CanDelete);
public sealed record CropSeasonListResponse(IReadOnlyList<CropSeasonReferenceItem> Items,
    int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record UpdateCropSeasonRequest(Guid CompanyId, string? Name, DateOnly StartsOn, DateOnly EndsOn,
    int ExpectedVersion);
public sealed record DeleteCropSeasonRequest(int Version);

public static class CropSeasonEndpoints
{
    public static IEndpointRouteBuilder MapCropSeasonEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/crop-seasons").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("platform.configure");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("platform.configure");
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

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await HasPermissionInTransactionAsync(db, actorId, "platform.configure",
                targetCompanyId: companyId, ct: ct))
            return Results.Forbid();
        if (!await IsCustomerCompanyInTransactionAsync(db, companyId, ct))
            return Problem(404, "CUSTOMER_COMPANY_NOT_FOUND", "Customer company not found.");
        var now = clock.GetUtcNow();
        var season = new CropSeason { Id = Guid.NewGuid(), CompanyId = companyId,
            Name = request.Name!.Trim(), StartsOn = request.StartsOn, EndsOn = request.EndsOn, CreatedBy = actorId };
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
        await transaction.CommitAsync(ct);
        return Results.Json(result, statusCode: 201);

    }

    private static async Task<IResult> ListAsync(string? companyId, string? search, string? q,
        int? page, int? pageSize, string? sortBy, string? sortDirection,
        HttpRequest request, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        Guid? requestedCompanyId = null;
        if (request.Query.ContainsKey("companyId"))
        {
            if (!Guid.TryParse(companyId, out var parsedCompanyId) || parsedCompanyId == Guid.Empty)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["companyId"] = ["Enter a valid customer company ID."] });
            requestedCompanyId = parsedCompanyId;
        }
        Guid targetCompanyId;
        var canConfigure = await access.HasPermissionAsync("platform.configure", ct);
        if (canConfigure)
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
        var term = search ?? q;
        if (page is < 1 || pageSize is < 1 or > 100 || term?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 25;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var sort = sortBy?.ToLowerInvariant() ?? "startson";
        var direction = sortDirection?.ToLowerInvariant() ?? "desc";
        if (sort is not ("name" or "startson") || direction is not ("asc" or "desc"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sortBy"] = ["Unsupported sort field or direction."] });
        var query = db.CropSeasons.AsNoTracking().Where(x => x.CompanyId == targetCompanyId);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var totalCount = await query.CountAsync(ct);
        IOrderedQueryable<CropSeason> ordered = (sort, direction == "desc") switch
        {
            ("name", false) => query.OrderBy(x => x.Name),
            ("name", true) => query.OrderByDescending(x => x.Name),
            ("startson", false) => query.OrderBy(x => x.StartsOn),
            _ => query.OrderByDescending(x => x.StartsOn)
        };
        var rows = await ordered.ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Results.Forbid();
        var canManage = canConfigure || await access.HasPermissionAsync("farms.write", ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var inUse = (await db.PlotSeasons.AsNoTracking().Where(x => x.CompanyId == targetCompanyId && ids.Contains(x.SeasonId))
            .Select(x => x.SeasonId).Distinct().ToArrayAsync(ct)).ToHashSet();
        var items = rows.Select(x => ToReferenceItem(x, actorId, canManage && !inUse.Contains(x.Id))).ToArray();
        return Results.Ok(new CropSeasonListResponse(items, currentPage, size, totalCount,
            (long)currentPage * size < totalCount));
    }
    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) ||
            !await identityDb.Users.AsNoTracking().AnyAsync(x => x.Id == actorId && x.IsActive, ct))
            return Results.Forbid();
        var canConfigure = await access.HasPermissionAsync("platform.configure", ct);
        if (!canConfigure && !await access.HasPermissionAsync("farms.read", ct) &&
            !await access.HasPermissionAsync("farms.write", ct)) return Results.Forbid();
        var season = await db.CropSeasons.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (season is null) return Problem(404, "CROP_SEASON_NOT_FOUND", "Crop season not found.");
        if (!await identityDb.Organizations.AsNoTracking().AnyAsync(
                x => x.Id == season.CompanyId && x.Kind == OrganizationKind.Customer, ct))
            return Problem(404, "CUSTOMER_COMPANY_NOT_FOUND", "Customer company not found.");
        if (!canConfigure)
        {
            var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
            if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct))
                return Results.Forbid();
            if (companyId != season.CompanyId) return Problem(404, "CROP_SEASON_NOT_FOUND", "Crop season not found.");
        }
        var canManage = canConfigure || await access.HasPermissionAsync("farms.write", ct);
        return Results.Ok(ToReferenceItem(season, actorId,
            canManage && !await CropSeasonInUseAsync(db, season.CompanyId, id, ct)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateCropSeasonRequest request,
        ClaimsPrincipal principal, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.CompanyId == Guid.Empty || request.ExpectedVersion < 1 ||
            string.IsNullOrWhiteSpace(request.Name) || request.StartsOn == default ||
            request.EndsOn == default || request.EndsOn < request.StartsOn)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Invalid crop-season values or version."] });
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await HasPermissionInTransactionAsync(db, actorId, "platform.configure", ct: ct))
            return Results.Forbid();
        var season = await db.CropSeasons.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (season is null) return Problem(404, "CROP_SEASON_NOT_FOUND", "Crop season not found.");
        if (season.CreatedBy != actorId) return Results.Forbid();
        if (season.Version != request.ExpectedVersion) return Problem(412, "REFERENCE_STALE", "The crop season changed. Reload before editing.");
        if (season.CompanyId != request.CompanyId)
            return Problem(409, "REFERENCE_SCOPE_MISMATCH", "Crop season company cannot be changed.");
        if (!await IsCustomerCompanyInTransactionAsync(db, season.CompanyId, ct))
            return Problem(404, "CUSTOMER_COMPANY_NOT_FOUND", "Customer company not found.");
        if (await CropSeasonInUseAsync(db, season.CompanyId, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced crop season cannot be edited.");
        var before = new { season.Name, season.StartsOn, season.EndsOn, season.Version };
        season.Name = request.Name.Trim();
        season.StartsOn = request.StartsOn;
        season.EndsOn = request.EndsOn;
        season.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = season.CompanyId, ActorId = actorId, EventType = "CROP_SEASON_UPDATED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, seasonId = id, before,
                after = new { season.Name, season.StartsOn, season.EndsOn, season.Version } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The crop season changed. Reload before editing.");
        }
        return Results.Ok(ToReferenceItem(season, actorId, true));
    }

    private static async Task<IResult> DeleteAsync(Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeleteCropSeasonRequest request,
        ClaimsPrincipal principal, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.Version < 1) return Results.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Version must be positive."] });
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await HasPermissionInTransactionAsync(db, actorId, "platform.configure", ct: ct))
            return Results.Forbid();
        var season = await db.CropSeasons.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (season is null) return Problem(404, "CROP_SEASON_NOT_FOUND", "Crop season not found.");
        if (season.CreatedBy != actorId) return Results.Forbid();
        if (season.Version != request.Version) return Problem(412, "REFERENCE_STALE", "The crop season changed. Reload before deleting.");
        var isCustomerCompany = await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM identity.organizations WHERE \"Id\"={season.CompanyId} AND \"Kind\"='Customer') AS \"Value\"").SingleAsync(ct);
        if (!isCustomerCompany)
            return Problem(404, "CUSTOMER_COMPANY_NOT_FOUND", "Customer company not found.");
        if (await CropSeasonInUseAsync(db, season.CompanyId, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced crop season cannot be deleted.");
        var before = new { season.Name, season.StartsOn, season.EndsOn, season.Version, season.IsDeleted };
        season.IsDeleted = true;
        season.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = season.CompanyId, ActorId = actorId, EventType = "CROP_SEASON_DELETED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, seasonId = id, before,
                after = new { season.Name, season.StartsOn, season.EndsOn, season.Version, season.IsDeleted } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The crop season changed. Reload before deleting.");
        }
        return Results.NoContent();
    }

    private static Task<bool> CropSeasonInUseAsync(CaptureDbContext db, Guid companyId, Guid id, CancellationToken ct) =>
        db.PlotSeasons.AnyAsync(x => x.CompanyId == companyId && x.SeasonId == id, ct);

    private static CropSeasonReferenceItem ToReferenceItem(CropSeason season, Guid actorId, bool canManage)
    {
        var canEdit = season.CreatedBy == actorId && canManage;
        return new CropSeasonReferenceItem(season.Id, season.CompanyId, season.Name, season.StartsOn,
            season.EndsOn, season.CreatedBy, season.Version, canEdit, canEdit);
    }
    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static async Task<bool> HasPermissionInTransactionAsync(CaptureDbContext db, Guid userId,
        string permission, Guid? companyId = null, Guid? targetCompanyId = null, CancellationToken ct = default)
    {
        var actor = await db.Database.SqlQuery<string>($"""
            SELECT u."Role" || ':' || org."Kind" AS "Value"
            FROM identity.users AS u
            JOIN identity.organizations AS org ON org."Id"=u."OrganizationId"
            WHERE u."Id"={userId} AND u."IsActive"=true AND u."Role" IS NOT NULL
              AND ({companyId}::uuid IS NULL OR (u."OrganizationId"={companyId}::uuid AND org."Kind"='Customer'))
              AND ({targetCompanyId}::uuid IS NULL OR EXISTS (
                  SELECT 1 FROM identity.organizations AS target
                  WHERE target."Id"={targetCompanyId}::uuid AND target."Kind"='Customer'))
            """).SingleOrDefaultAsync(ct);
        if (actor is null) return false;
        var split = actor.IndexOf(':');
        return split > 0 && Roles.All.TryGetValue(actor[..split], out var definition) &&
            Enum.TryParse<OrganizationKind>(actor[(split + 1)..], out var organizationKind) &&
            definition.OrganizationKind == organizationKind &&
            definition.Permissions.Contains(permission, StringComparer.Ordinal);
    }

    public static Task<bool> IsCustomerCompanyInTransactionAsync(CaptureDbContext db, Guid companyId,
        CancellationToken ct = default) =>
        db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM identity.organizations WHERE \"Id\"={companyId} AND \"Kind\"='Customer') AS \"Value\"")
            .SingleAsync(ct);
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
