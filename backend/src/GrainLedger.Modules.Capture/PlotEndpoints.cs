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
public sealed record PlotListItemResponse(Guid Id, Guid FarmerId, string Code, decimal AreaHa);
public sealed record PlotListResponse(IReadOnlyList<PlotListItemResponse> Items, int Page, int PageSize, bool HasMore);

public static class PlotEndpoints
{
    public static IEndpointRouteBuilder MapPlotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plots").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", RegisterAsync).RequireAuthorization("farms.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
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
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
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
            Code = code, BoundaryGeojson = geometry, AreaHa = normalizedArea
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

    private static async Task<IResult> ListAsync(Guid? farmerId, string? q, int? page, int? pageSize,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        if (farmerId == Guid.Empty || page is < 1 || pageSize is < 1 or > 100 || q?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 20;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var query = db.Plots.AsNoTracking().Where(x => x.CompanyId == actor.Value.CompanyId);
        if (farmerId is { } id) query = query.Where(x => x.FarmerId == id);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\"));
        }
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((currentPage - 1) * size).Take(size + 1).ToListAsync(ct);
        return Results.Ok(new PlotListResponse(rows.Take(size).Select(x =>
            new PlotListItemResponse(x.Id, x.FarmerId, x.Code, x.AreaHa)).ToList(),
            currentPage, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        var plot = await db.Plots.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        return plot is null ? Problem(404, "PLOT_NOT_FOUND", "Plot not found.") : Results.Ok(ToResponse(plot));
    }

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
