using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record PlotInput(Guid EntityId, Guid FarmerId, string? Code, JsonElement Geometry, decimal AreaHa);
public sealed record RegisterPlotRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion, PlotInput? Payload);
public sealed record PlotResponse(Guid Id, Guid FarmerId, string Code, JsonElement Geometry, decimal AreaHa);
public sealed record PlotListItemResponse(Guid Id, Guid FarmerId, string Code, decimal AreaHa,
    Guid? CreatedBy, int Version, bool CanEdit, bool CanDelete);
public sealed record PlotReferenceResponse(Guid Id, Guid FarmerId, string Code, JsonElement Geometry,
    decimal AreaHa, Guid? CreatedBy, int Version, bool CanEdit, bool CanDelete);
public sealed record PlotListResponse(IReadOnlyList<PlotListItemResponse> Items,
    int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record UpdatePlotRequest(Guid FarmerId, string? Code, JsonElement Geometry,
    decimal AreaHa, int ExpectedVersion);

public static class PlotEndpoints
{
    public static IEndpointRouteBuilder MapPlotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plots").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", RegisterAsync).RequireAuthorization("farms.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("farms.write");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("farms.write");
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterPlotRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct))
            return Results.Forbid();
        var errors = Validate(request, out var geometry);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var payload = request.Payload!;
        var companyId = actor.Value.CompanyId;
        var code = payload.Code is null
            ? $"TR-{payload.EntityId:N}".ToUpperInvariant()
            : payload.Code.Trim();
        var deviceId = request.DeviceId!.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var normalizedArea = decimal.Parse(payload.AreaHa.ToString("G29", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        var canonical = JsonSerializer.Serialize(new
        {
            operationType = "PLOT_REGISTERED", request.PayloadSchemaVersion, capturedAt,
            payload.EntityId, payload.FarmerId, code,
            geometry = JsonSerializer.Deserialize<JsonElement>(geometry), areaHa = normalizedArea
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockKey = $"{companyId:N}:{deviceId}:{request.ClientOperationId:N}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor.Value.UserId,
                "farms.write", companyId, ct: ct))
            return Results.Forbid();
        var prior = await db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.DeviceId == deviceId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash)
                return Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.");
            if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
                return Results.Ok(JsonSerializer.Deserialize<PlotResponse>(prior.ResultPayload));
            return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
        }

        if (!await db.Farmers.AnyAsync(x => x.CompanyId == companyId && x.Id == payload.FarmerId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected farmer is not available.");
        if (await db.Plots.AnyAsync(x => x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The plot ID already exists.");
        if (await db.Plots.AnyAsync(x => x.FarmerId == payload.FarmerId && x.Code == code, ct))
            return Problem(409, "PLOT_CODE_EXISTS", "The plot code already exists for this farmer.");

        var now = clock.GetUtcNow();
        var plot = new Plot
        {
            Id = payload.EntityId, CompanyId = companyId, FarmerId = payload.FarmerId,
            Code = code, BoundaryGeojson = geometry, AreaHa = normalizedArea, CreatedBy = actor.Value.UserId
        };
        var result = ToResponse(plot);
        var businessEvent = new CaptureBusinessEvent
        {
            CompanyId = companyId, ActorId = actor.Value.UserId, EventType = "PLOT_REGISTERED",
            OccurredAt = capturedAt, RecordedAt = now, CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, plotId = plot.Id, plot.FarmerId, plot.Code,
                geometry = JsonSerializer.Deserialize<JsonElement>(geometry), plot.AreaHa
            })
        };
        db.Plots.Add(plot);
        db.BusinessEvents.Add(businessEvent);
        try
        {
            await db.SaveChangesAsync(ct);
            db.OfflineOperations.Add(new CaptureOperation
            {
                CompanyId = companyId, SubmittedBy = actor.Value.UserId, DeviceId = deviceId,
                ClientOperationId = request.ClientOperationId, PayloadHash = hash,
                Payload = canonical, CapturedAt = capturedAt, ReceivedAt = now, Status = "APPLIED",
                AppliedEventId = businessEvent.Id, ResultPayload = JsonSerializer.Serialize(result)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            return pg.ConstraintName == "ux_plots_farmer_code"
                ? Problem(409, "PLOT_CODE_EXISTS", "The plot code already exists for this farmer.")
                : Problem(409, "ENTITY_ALREADY_EXISTS", "The plot ID already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected farmer is not available.");
        }
        return Results.Created($"/api/plots/{plot.Id}", result);
    }

    private static async Task<IResult> ListAsync(Guid? farmerId, string? search, string? q,
        int? page, int? pageSize, string? sortBy, string? sortDirection,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        var term = search ?? q;
        if (farmerId == Guid.Empty || page is < 1 || pageSize is < 1 or > 100 || term?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 25;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var sort = sortBy?.ToLowerInvariant() ?? "code";
        var direction = sortDirection?.ToLowerInvariant() ?? "asc";
        if (sort is not ("code" or "areaha") || direction is not ("asc" or "desc"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sortBy"] = ["Unsupported sort field or direction."] });
        var query = db.Plots.AsNoTracking().Where(x => x.CompanyId == actor.Value.CompanyId);
        if (farmerId is { } id) query = query.Where(x => x.FarmerId == id);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\"));
        }
        var totalCount = await query.CountAsync(ct);
        IOrderedQueryable<Plot> ordered = (sort, direction == "desc") switch
        {
            ("areaha", false) => query.OrderBy(x => x.AreaHa),
            ("areaha", true) => query.OrderByDescending(x => x.AreaHa),
            ("code", true) => query.OrderByDescending(x => x.Code),
            _ => query.OrderBy(x => x.Code)
        };
        var rows = await ordered.ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        var canManage = await access.HasPermissionAsync("farms.write", ct);
        var items = rows.Select(x => ToListItem(x, actor.Value.UserId, canManage)).ToArray();
        return Results.Ok(new PlotListResponse(items, currentPage, size, totalCount,
            (long)currentPage * size < totalCount));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        var plot = await db.Plots.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        if (plot is null) return Problem(404, "PLOT_NOT_FOUND", "Plot not found.");
        var canManage = await access.HasPermissionAsync("farms.write", ct);
        return Results.Ok(ToReferenceResponse(plot, actor.Value.UserId, canManage));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdatePlotRequest request,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1 || request.FarmerId == Guid.Empty ||
            request.Code is not null && (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 40) ||
            request.AreaHa <= 0 || request.AreaHa >= 1_000_000_000_000m ||
            decimal.Round(request.AreaHa, 12) != request.AreaHa ||
            !PlotGeometry.TryNormalize(request.Geometry, out var geometry))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Invalid plot values or version."] });
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor.Value.UserId,
                "farms.write", actor.Value.CompanyId, ct: ct))
            return Results.Forbid();
        var plot = await db.Plots.SingleOrDefaultAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        if (plot is null) return Problem(404, "PLOT_NOT_FOUND", "Plot not found.");
        if (plot.CreatedBy != actor.Value.UserId) return Results.Forbid();
        if (plot.Version != request.ExpectedVersion) return Problem(412, "REFERENCE_STALE", "The plot changed. Reload before editing.");
        if (await PlotInUseAsync(db, actor.Value.CompanyId, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced plot cannot be edited.");
        if (!await db.Farmers.AnyAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id == request.FarmerId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected farmer is not available.");
        var code = request.Code?.Trim() ?? $"TR-{id:N}".ToUpperInvariant();
        if (await db.Plots.AnyAsync(x => x.CompanyId == actor.Value.CompanyId && x.FarmerId == request.FarmerId &&
                x.Id != id && x.Code == code, ct))
            return Problem(409, "PLOT_CODE_EXISTS", "The plot code already exists for this farmer.");
        var before = new { plot.FarmerId, plot.Code, plot.BoundaryGeojson, plot.AreaHa, plot.Version };
        plot.FarmerId = request.FarmerId;
        plot.Code = code;
        plot.BoundaryGeojson = geometry;
        plot.AreaHa = decimal.Parse(request.AreaHa.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        plot.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = plot.CompanyId, ActorId = actor.Value.UserId, EventType = "PLOT_UPDATED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, plotId = plot.Id, before,
                after = new { plot.FarmerId, plot.Code, plot.BoundaryGeojson, plot.AreaHa, plot.Version } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The plot changed. Reload before editing.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "PLOT_CODE_EXISTS", "The plot code already exists for this farmer.");
        }
        return Results.Ok(ToReferenceResponse(plot, actor.Value.UserId, true));
    }

    private static async Task<IResult> DeleteAsync(Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeleteReferenceRequest request,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.Version < 1) return Results.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Version must be positive."] });
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor.Value.UserId,
                "farms.write", actor.Value.CompanyId, ct: ct))
            return Results.Forbid();
        var plot = await db.Plots.SingleOrDefaultAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        if (plot is null) return Problem(404, "PLOT_NOT_FOUND", "Plot not found.");
        if (plot.CreatedBy != actor.Value.UserId) return Results.Forbid();
        if (plot.Version != request.Version) return Problem(412, "REFERENCE_STALE", "The plot changed. Reload before deleting.");
        if (await PlotInUseAsync(db, actor.Value.CompanyId, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced plot cannot be deleted.");
        var before = new { plot.FarmerId, plot.Code, plot.BoundaryGeojson, plot.AreaHa, plot.Version, plot.IsDeleted };
        plot.IsDeleted = true;
        plot.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = plot.CompanyId, ActorId = actor.Value.UserId, EventType = "PLOT_DELETED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, plotId = plot.Id, before,
                after = new { plot.FarmerId, plot.Code, plot.BoundaryGeojson, plot.AreaHa, plot.Version, plot.IsDeleted } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The plot changed. Reload before deleting.");
        }
        return Results.NoContent();
    }

    private static Task<bool> PlotInUseAsync(CaptureDbContext db, Guid companyId, Guid id, CancellationToken ct) =>
        db.PlotSeasons.AnyAsync(x => x.CompanyId == companyId && x.PlotId == id, ct);

    private static Dictionary<string, string[]> Validate(RegisterPlotRequest request, out string geometry)
    {
        geometry = string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Trim().Length > 100)
            errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (request.ClientOperationId == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (request.CapturedAt == default) errors["capturedAt"] = ["Capture time is required."];
        if (request.PayloadSchemaVersion != 1) errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        if (request.Payload is null) { errors["payload"] = ["Plot data is required."]; return errors; }
        if (request.Payload.EntityId == Guid.Empty) errors["payload.entityId"] = ["Plot ID is required."];
        if (request.Payload.FarmerId == Guid.Empty) errors["payload.farmerId"] = ["Farmer ID is required."];
        if (request.Payload.Code is not null &&
            (string.IsNullOrWhiteSpace(request.Payload.Code) || request.Payload.Code.Trim().Length > 40))
            errors["payload.code"] = ["If supplied, code must not be blank or exceed 40 characters."];
        if (!PlotGeometry.TryNormalize(request.Payload.Geometry, out geometry))
            errors["payload.geometry"] = ["A valid WGS84 Point or Polygon is required."];
        if (request.Payload.AreaHa <= 0 || request.Payload.AreaHa >= 1_000_000_000_000m ||
            decimal.Round(request.Payload.AreaHa, 12) != request.Payload.AreaHa)
            errors["payload.areaHa"] = ["Area must be positive and fit numeric(24,12) hectares."];
        return errors;
    }

    private static PlotResponse ToResponse(Plot plot) => new(plot.Id, plot.FarmerId, plot.Code,
        JsonSerializer.Deserialize<JsonElement>(plot.BoundaryGeojson), plot.AreaHa);
    private static PlotListItemResponse ToListItem(Plot plot, Guid actorId, bool canManage)
    {
        var canEdit = plot.CreatedBy == actorId && canManage;
        return new PlotListItemResponse(plot.Id, plot.FarmerId, plot.Code, plot.AreaHa,
            plot.CreatedBy, plot.Version, canEdit, canEdit);
    }

    private static PlotReferenceResponse ToReferenceResponse(Plot plot, Guid actorId, bool canManage)
    {
        var canEdit = plot.CreatedBy == actorId && canManage;
        return new PlotReferenceResponse(plot.Id, plot.FarmerId, plot.Code,
            JsonSerializer.Deserialize<JsonElement>(plot.BoundaryGeojson), plot.AreaHa,
            plot.CreatedBy, plot.Version, canEdit, canEdit);
    }

    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("farms.read", ct) || await access.HasPermissionAsync("farms.write", ct);

    private static async Task<(Guid UserId, Guid CompanyId)?> GetActorAsync(ClaimsPrincipal principal,
        IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user?.OrganizationId is not { } companyId || user.Organization?.Kind != OrganizationKind.Customer) return null;
        return (userId, companyId);
    }

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
