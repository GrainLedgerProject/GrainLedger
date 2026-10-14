using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record PlotSeasonInput(Guid EntityId, Guid PlotId, Guid SeasonId, string? RiceVariety);
public sealed record RegisterPlotSeasonRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion, PlotSeasonInput? Payload);
public sealed record PlotSeasonResponse(Guid Id, Guid PlotId, Guid SeasonId, string RiceVariety);
public sealed record PlotSeasonListResponse(IReadOnlyList<PlotSeasonResponse> Items, int Page, int PageSize, bool HasMore);

public static class PlotSeasonEndpoints
{
    public static IEndpointRouteBuilder MapPlotSeasonEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plot-seasons").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", RegisterAsync).RequireAuthorization("farms.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterPlotSeasonRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var payload = request.Payload!;
        var variety = payload.RiceVariety!.Trim();
        var deviceId = request.DeviceId!.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var canonical = JsonSerializer.Serialize(new { operationType = "PLOT_SEASON_REGISTERED",
            request.PayloadSchemaVersion, capturedAt, payload.EntityId, payload.PlotId, payload.SeasonId, riceVariety = variety });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockKey = $"{companyId.Value:N}:{deviceId}:{request.ClientOperationId:N}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId.Value && x.DeviceId == deviceId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash) return Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.");
            if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
                return Results.Ok(JsonSerializer.Deserialize<PlotSeasonResponse>(prior.ResultPayload));
            return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
        }
        if (!await db.Plots.AnyAsync(x => x.CompanyId == companyId.Value && x.Id == payload.PlotId, ct) ||
            !await db.CropSeasons.AnyAsync(x => x.CompanyId == companyId.Value && x.Id == payload.SeasonId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected plot or crop season is not available.");
        if (await db.PlotSeasons.AnyAsync(x => x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The plot-season ID already exists.");
        if (await db.PlotSeasons.AnyAsync(x => x.PlotId == payload.PlotId && x.SeasonId == payload.SeasonId, ct))
            return Problem(409, "PLOT_SEASON_EXISTS", "This plot is already registered for that crop season.");

        var now = clock.GetUtcNow();
        var item = new PlotSeason { Id = payload.EntityId, CompanyId = companyId.Value,
            PlotId = payload.PlotId, SeasonId = payload.SeasonId, RiceVariety = variety };
        var result = ToResponse(item);
        var businessEvent = new CaptureBusinessEvent
        {
            CompanyId = companyId, ActorId = GetUserId(principal), EventType = "PLOT_SEASON_REGISTERED",
            OccurredAt = capturedAt, RecordedAt = now, CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, plotSeasonId = item.Id,
                item.PlotId, item.SeasonId, item.RiceVariety })
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
            return pg.ConstraintName == "ux_plot_seasons_plot_season"
                ? Problem(409, "PLOT_SEASON_EXISTS", "This plot is already registered for that crop season.")
                : Problem(409, "ENTITY_ALREADY_EXISTS", "The plot-season ID already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The selected plot or crop season is not available.");
        }
        return Results.Created($"/api/plot-seasons/{item.Id}", result);
    }

    private static async Task<IResult> ListAsync(Guid? plotId, Guid? seasonId, int? page, int? pageSize,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        if (plotId == Guid.Empty || seasonId == Guid.Empty || page is < 1 || pageSize is < 1 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid filter or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 20;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var query = db.PlotSeasons.AsNoTracking().Where(x => x.CompanyId == companyId.Value);
        if (plotId is { } plot) query = query.Where(x => x.PlotId == plot);
        if (seasonId is { } season) query = query.Where(x => x.SeasonId == season);
        var rows = await query.OrderBy(x => x.Id).Skip((currentPage - 1) * size).Take(size + 1).ToListAsync(ct);
        return Results.Ok(new PlotSeasonListResponse(rows.Take(size).Select(ToResponse).ToList(), currentPage, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var item = await db.PlotSeasons.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId.Value && x.Id == id, ct);
        return item is null ? Problem(404, "PLOT_SEASON_NOT_FOUND", "Plot-season not found.") : Results.Ok(ToResponse(item));
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
        if (string.IsNullOrWhiteSpace(request.Payload.RiceVariety)) errors["payload.riceVariety"] = ["Rice variety is required."];
        return errors;
    }

    private static PlotSeasonResponse ToResponse(PlotSeason x) => new(x.Id, x.PlotId, x.SeasonId, x.RiceVariety);
    private static Guid GetUserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("farms.read", ct) || await access.HasPermissionAsync("farms.write", ct);
    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
