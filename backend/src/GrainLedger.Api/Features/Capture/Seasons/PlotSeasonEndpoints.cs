using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Api.Features.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Api.Features.Capture;

public sealed record PlotSeasonInput(Guid EntityId, Guid PlotId, Guid SeasonId, string? RiceVariety = null, Guid? RiceVarietyId = null);
public sealed record RegisterPlotSeasonRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion, PlotSeasonInput? Payload);
public sealed record PlotSeasonResponse(Guid Id, Guid PlotId, Guid SeasonId, string RiceVariety, Guid? RiceVarietyId = null);
public sealed record PlotSeasonReferenceItem(Guid Id, Guid PlotId, Guid SeasonId, string PlotCode, string SeasonName, string RiceVariety,
    Guid? CreatedBy, int Version, bool CanEdit, bool CanDelete, Guid? RiceVarietyId, bool CanAssignRiceVariety);
public sealed record PlotSeasonReferenceResponse(Guid Id, Guid PlotId, Guid SeasonId, string PlotCode, string SeasonName, string RiceVariety,
    Guid? CreatedBy, int Version, bool CanEdit, bool CanDelete, Guid? RiceVarietyId, bool CanAssignRiceVariety);
public sealed record PlotSeasonListResponse(IReadOnlyList<PlotSeasonReferenceItem> Items,
    int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record UpdatePlotSeasonRequest(Guid PlotId, Guid SeasonId, Guid? RiceVarietyId, int ExpectedVersion);
public sealed record DeletePlotSeasonRequest(int Version);

public static class PlotSeasonEndpoints
{
    public static IEndpointRouteBuilder MapPlotSeasonEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plot-seasons").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", RegisterAsync).RequireAuthorization("farms.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("farms.write");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("farms.write");
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterPlotSeasonRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, db, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var payload = request.Payload!;
        var legacy = payload.RiceVarietyId is null;
        var deviceId = request.DeviceId!.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var canonical = legacy
            ? JsonSerializer.Serialize(new { operationType = "PLOT_SEASON_REGISTERED",
                request.PayloadSchemaVersion, capturedAt, payload.EntityId, payload.PlotId, payload.SeasonId, riceVariety = payload.RiceVariety!.Trim() })
            : JsonSerializer.Serialize(new { operationType = "PLOT_SEASON_REGISTERED",
                request.PayloadSchemaVersion, capturedAt, payload.EntityId, payload.PlotId, payload.SeasonId, riceVarietyId = payload.RiceVarietyId });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockKey = $"{companyId.Value:N}:{deviceId}:{request.ClientOperationId:N}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, GetUserId(principal),
                "farms.write", companyId.Value, ct: ct))
            return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId.Value && x.DeviceId == deviceId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.SubmittedBy != GetUserId(principal)) return Results.Forbid();
            if (prior.PayloadHash != hash) return Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.");
            if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
                return Results.Ok(JsonSerializer.Deserialize<PlotSeasonResponse>(prior.ResultPayload));
            return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
        }
        if (legacy) return Problem(409, "LEGACY_VARIETY_RESELECT_REQUIRED", "Select a rice variety from the catalog.");
        var variety = await db.RiceVarieties.AsNoTracking().Where(x => x.Id == payload.RiceVarietyId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        if (variety is null) return Problem(409, "RICE_VARIETY_NOT_AVAILABLE", "The selected rice variety is not available.");
        if (!await db.Plots.AnyAsync(x => x.CompanyId == companyId.Value && x.Id == payload.PlotId, ct) ||
            !await db.CropSeasons.AnyAsync(x => x.CompanyId == companyId.Value && x.Id == payload.SeasonId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected plot or crop season is not available.");
        if (await db.PlotSeasons.AnyAsync(x => x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The plot-season ID already exists.");
        if (await db.PlotSeasons.AnyAsync(x => x.PlotId == payload.PlotId && x.SeasonId == payload.SeasonId, ct))
            return Problem(409, "PLOT_SEASON_EXISTS", "This plot is already registered for that crop season.");

        var now = clock.GetUtcNow();
        var item = new PlotSeason
        {
            Id = payload.EntityId, CompanyId = companyId.Value, PlotId = payload.PlotId,
            SeasonId = payload.SeasonId, RiceVarietyId = payload.RiceVarietyId, RiceVariety = variety, CreatedBy = GetUserId(principal)
        };
        var result = ToResponse(item);
        var businessEvent = new CaptureBusinessEvent
        {
            CompanyId = companyId, ActorId = GetUserId(principal), EventType = "PLOT_SEASON_REGISTERED",
            OccurredAt = capturedAt, RecordedAt = now, CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, plotSeasonId = item.Id,
                item.PlotId, item.SeasonId, item.RiceVarietyId, item.RiceVariety })
        };
        db.PlotSeasons.Add(item);
        db.BusinessEvents.Add(businessEvent);
        try
        {
            await db.SaveChangesAsync(ct);
            db.OfflineOperations.Add(new CaptureOperation
            {
                CompanyId = companyId.Value, SubmittedBy = GetUserId(principal), DeviceId = deviceId,
                ClientOperationId = request.ClientOperationId, PayloadHash = hash, Payload = canonical,
                CapturedAt = capturedAt, ReceivedAt = now, Status = "APPLIED",
                AppliedEventId = businessEvent.Id, ResultPayload = JsonSerializer.Serialize(result)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            return pg.ConstraintName == "capture_ux_plot_seasons_plot_season"
                ? Problem(409, "PLOT_SEASON_EXISTS", "This plot is already registered for that crop season.")
                : Problem(409, "ENTITY_ALREADY_EXISTS", "The plot-season ID already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected plot or crop season is not available.");
        }
        return Results.Created($"/api/plot-seasons/{item.Id}", result);
    }

    private static async Task<IResult> ListAsync(Guid? plotId, Guid? seasonId, string? search, string? q,
        int? page, int? pageSize, string? sortBy, string? sortDirection,
        ClaimsPrincipal principal, ICurrentAccess access,
        AppDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, db, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var term = search ?? q;
        if (plotId == Guid.Empty || seasonId == Guid.Empty || page is < 1 || pageSize is < 1 or > 100 || term?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid filter or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 25;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var sort = sortBy?.ToLowerInvariant() ?? "id";
        var direction = sortDirection?.ToLowerInvariant() ?? "asc";
        if (sort != "id" || direction is not ("asc" or "desc"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sortBy"] = ["Unsupported sort field or direction."] });
        var query = db.PlotSeasons.AsNoTracking().Where(x => x.CompanyId == companyId.Value);
        if (plotId is { } plot) query = query.Where(x => x.PlotId == plot);
        if (seasonId is { } season) query = query.Where(x => x.SeasonId == season);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(
                db.RiceVarieties.Where(v => v.Id == x.RiceVarietyId).Select(v => v.Name).FirstOrDefault() ?? x.RiceVariety, pattern, "\\"));
        }
        var totalCount = await query.CountAsync(ct);
        var ordered = direction == "desc" ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id);
        var rows = await (from item in ordered
            join plotRecord in db.Plots.AsNoTracking() on new { item.CompanyId, Id = item.PlotId } equals new { plotRecord.CompanyId, plotRecord.Id }
            join seasonRecord in db.CropSeasons.AsNoTracking() on new { item.CompanyId, Id = item.SeasonId } equals new { seasonRecord.CompanyId, seasonRecord.Id }
            select new { Item = item, PlotCode = plotRecord.Code, SeasonName = seasonRecord.Name,
                Variety = db.RiceVarieties.Where(v => v.Id == item.RiceVarietyId).Select(v => v.Name).FirstOrDefault() ?? item.RiceVariety })
            .Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        var canManage = await access.HasPermissionAsync("farms.write", ct);
        var actorId = GetUserId(principal);
        var ids = rows.Select(x => x.Item.Id).ToArray();
        var procurementRefs = await db.ProcurementItems.AsNoTracking().Where(x => x.CompanyId == companyId.Value && ids.Contains(x.PlotSeasonId))
            .Select(x => x.PlotSeasonId).Distinct().ToArrayAsync(ct);
        var sprayRefs = await db.SprayDeclarations.AsNoTracking().Where(x => x.CompanyId == companyId.Value && ids.Contains(x.PlotSeasonId))
            .Select(x => x.PlotSeasonId).Distinct().ToArrayAsync(ct);
        var inUse = procurementRefs.Concat(sprayRefs).ToHashSet();
        var items = rows.Select(x => ToReferenceItem(x.Item, x.PlotCode, x.SeasonName, x.Variety, actorId,
            canManage, !inUse.Contains(x.Item.Id))).ToArray();
        return Results.Ok(new PlotSeasonListResponse(items, currentPage, size, totalCount,
            (long)currentPage * size < totalCount));
    }
    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access, AppDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, db, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var row = await (from item in db.PlotSeasons.AsNoTracking()
            where item.CompanyId == companyId.Value && item.Id == id
            join plot in db.Plots.AsNoTracking() on new { item.CompanyId, Id = item.PlotId } equals new { plot.CompanyId, plot.Id }
            join season in db.CropSeasons.AsNoTracking() on new { item.CompanyId, Id = item.SeasonId } equals new { season.CompanyId, season.Id }
            select new { Item = item, PlotCode = plot.Code, SeasonName = season.Name,
                Variety = db.RiceVarieties.Where(v => v.Id == item.RiceVarietyId).Select(v => v.Name).FirstOrDefault() ?? item.RiceVariety }).SingleOrDefaultAsync(ct);
        if (row is null) return Problem(404, "PLOT_SEASON_NOT_FOUND", "Plot-season not found.");
        var canManage = await access.HasPermissionAsync("farms.write", ct);
        return Results.Ok(ToReferenceResponse(row.Item, row.PlotCode, row.SeasonName, row.Variety, GetUserId(principal),
            canManage, !await PlotSeasonInUseAsync(db, companyId.Value, id, ct)));
    }
    private static async Task<IResult> UpdateAsync(Guid id, UpdatePlotSeasonRequest request,
        ClaimsPrincipal principal, ICurrentAccess access,
        AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1 || request.PlotId == Guid.Empty || request.SeasonId == Guid.Empty ||
            request.RiceVarietyId is null || request.RiceVarietyId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Invalid plot-season values or version."] });
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, db, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var actorId = GetUserId(principal);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actorId,
                "farms.write", companyId.Value, ct: ct))
            return Results.Forbid();
        var item = await db.PlotSeasons.SingleOrDefaultAsync(x => x.CompanyId == companyId.Value && x.Id == id, ct);
        if (item is null) return Problem(404, "PLOT_SEASON_NOT_FOUND", "Plot-season not found.");
        if (item.CreatedBy != actorId) return Results.Forbid();
        if (item.Version != request.ExpectedVersion) return Problem(412, "REFERENCE_STALE", "The plot-season changed. Reload before editing.");
        var inUse = await PlotSeasonInUseAsync(db, companyId.Value, id, ct);
        if (inUse && !(item.RiceVarietyId is null && item.PlotId == request.PlotId && item.SeasonId == request.SeasonId))
            return Problem(409, "REFERENCE_IN_USE", "A referenced plot-season cannot be edited.");
        var variety = await db.RiceVarieties.AsNoTracking().Where(x => x.Id == request.RiceVarietyId)
            .Select(x => x.Name).SingleOrDefaultAsync(ct);
        if (variety is null) return Problem(409, "RICE_VARIETY_NOT_AVAILABLE", "The selected rice variety is not available.");
        if (!await db.Plots.AnyAsync(x => x.CompanyId == companyId.Value && x.Id == request.PlotId, ct) ||
            !await db.CropSeasons.AnyAsync(x => x.CompanyId == companyId.Value && x.Id == request.SeasonId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected plot or crop season is not available.");
        if (await db.PlotSeasons.AnyAsync(x => x.CompanyId == companyId.Value && x.Id != id &&
                x.PlotId == request.PlotId && x.SeasonId == request.SeasonId, ct))
            return Problem(409, "PLOT_SEASON_EXISTS", "This plot is already registered for that crop season.");
        var before = new { item.PlotId, item.SeasonId, item.RiceVarietyId, item.RiceVariety, item.Version };
        item.PlotId = request.PlotId;
        item.SeasonId = request.SeasonId;
        item.RiceVarietyId = request.RiceVarietyId;
        if (!inUse) item.RiceVariety = variety;
        item.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = item.CompanyId, ActorId = actorId, EventType = "PLOT_SEASON_UPDATED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, plotSeasonId = id, before,
                after = new { item.PlotId, item.SeasonId, item.RiceVarietyId, item.RiceVariety, item.Version } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            var labels = await (from plot in db.Plots.AsNoTracking()
                join season in db.CropSeasons.AsNoTracking() on new { plot.CompanyId, Id = item.SeasonId } equals new { season.CompanyId, season.Id }
                where plot.CompanyId == item.CompanyId && plot.Id == item.PlotId
                select new { PlotCode = plot.Code, SeasonName = season.Name }).SingleAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToReferenceResponse(item, labels.PlotCode, labels.SeasonName, variety, actorId, true, !inUse));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The plot-season changed. Reload before editing.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "PLOT_SEASON_EXISTS", "This plot is already registered for that crop season.");
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeletePlotSeasonRequest request,
        ClaimsPrincipal principal, ICurrentAccess access,
        AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.Version < 1) return Results.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Version must be positive."] });
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, db, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var actorId = GetUserId(principal);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actorId,
                "farms.write", companyId.Value, ct: ct))
            return Results.Forbid();
        var item = await db.PlotSeasons.SingleOrDefaultAsync(x => x.CompanyId == companyId.Value && x.Id == id, ct);
        if (item is null) return Problem(404, "PLOT_SEASON_NOT_FOUND", "Plot-season not found.");
        if (item.CreatedBy != actorId) return Results.Forbid();
        if (item.Version != request.Version) return Problem(412, "REFERENCE_STALE", "The plot-season changed. Reload before deleting.");
        if (await PlotSeasonInUseAsync(db, companyId.Value, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced plot-season cannot be deleted.");
        var before = new { item.PlotId, item.SeasonId, item.RiceVariety, item.Version, item.IsDeleted };
        item.IsDeleted = true;
        item.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = item.CompanyId, ActorId = actorId, EventType = "PLOT_SEASON_DELETED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, plotSeasonId = id, before,
                after = new { item.PlotId, item.SeasonId, item.RiceVariety, item.Version, item.IsDeleted } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The plot-season changed. Reload before deleting.");
        }
        return Results.NoContent();
    }

    private static async Task<bool> PlotSeasonInUseAsync(AppDbContext db, Guid companyId, Guid id, CancellationToken ct) =>
        await db.ProcurementItems.AnyAsync(x => x.CompanyId == companyId && x.PlotSeasonId == id, ct) ||
        await db.SprayDeclarations.AnyAsync(x => x.CompanyId == companyId && x.PlotSeasonId == id, ct);

    private static PlotSeasonReferenceItem ToReferenceItem(PlotSeason item, string plotCode, string seasonName,
        string variety, Guid actorId, bool canManage, bool unused)
    {
        var canEdit = item.CreatedBy == actorId && canManage && unused;
        return new PlotSeasonReferenceItem(item.Id, item.PlotId, item.SeasonId, plotCode, seasonName, variety,
            item.CreatedBy, item.Version, canEdit, canEdit, item.RiceVarietyId,
            item.RiceVarietyId is null && item.CreatedBy == actorId && canManage);
    }

    private static PlotSeasonReferenceResponse ToReferenceResponse(PlotSeason item, string plotCode, string seasonName,
        string variety, Guid actorId, bool canManage, bool unused)
    {
        var canEdit = item.CreatedBy == actorId && canManage && unused;
        return new PlotSeasonReferenceResponse(item.Id, item.PlotId, item.SeasonId, plotCode, seasonName, variety,
            item.CreatedBy, item.Version, canEdit, canEdit, item.RiceVarietyId,
            item.RiceVarietyId is null && item.CreatedBy == actorId && canManage);
    }


    private static Dictionary<string, string[]> Validate(RegisterPlotSeasonRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Trim().Length > 100)
            errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (request.ClientOperationId == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (request.CapturedAt == default) errors["capturedAt"] = ["Capture time is required."];
        if (request.PayloadSchemaVersion != 1) errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        if (request.Payload is null) { errors["payload"] = ["Plot-season data is required."]; return errors; }
        if (request.Payload.EntityId == Guid.Empty) errors["payload.entityId"] = ["Plot-season ID is required."];
        if (request.Payload.PlotId == Guid.Empty) errors["payload.plotId"] = ["Plot ID is required."];
        if (request.Payload.SeasonId == Guid.Empty) errors["payload.seasonId"] = ["Crop season ID is required."];
        var hasText = !string.IsNullOrWhiteSpace(request.Payload.RiceVariety);
        if (request.Payload.RiceVarietyId == Guid.Empty || (request.Payload.RiceVarietyId is null) == !hasText)
            errors["payload.riceVarietyId"] = ["Provide exactly one rice variety ID or legacy name."];
        return errors;
    }

    private static PlotSeasonResponse ToResponse(PlotSeason x) => new(x.Id, x.PlotId, x.SeasonId, x.RiceVariety, x.RiceVarietyId);
    private static Guid GetUserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("farms.read", ct) || await access.HasPermissionAsync("farms.write", ct);
    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
