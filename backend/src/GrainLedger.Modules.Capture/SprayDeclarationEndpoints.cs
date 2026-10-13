using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record SprayItemInput(string? ProductName, Guid? SubstanceId, decimal Rate, string? RateUnit);
public sealed record SprayDeclarationInput(Guid EntityId, Guid PlotSeasonId, DateOnly AppliedOn,
    string? InformationSource, string? EvidenceRef, IReadOnlyList<SprayItemInput?>? Items);
public sealed record RecordSprayDeclarationRequest(string? DeviceId, Guid ClientOperationId,
    DateTimeOffset CapturedAt, int PayloadSchemaVersion, SprayDeclarationInput? Payload);
public sealed record SprayItemResponse(Guid Id, string? ProductName, Guid? SubstanceId, decimal Rate, string RateUnit);
public sealed record SprayDeclarationResponse(Guid Id, Guid PlotSeasonId, DateOnly AppliedOn,
    string InformationSource, string? EvidenceRef, DateTimeOffset CapturedAt, DateTimeOffset RecordedAt,
    Guid RecordedBy, IReadOnlyList<SprayItemResponse> Items);
public sealed record SprayDeclarationSummary(Guid Id, Guid PlotSeasonId, DateOnly AppliedOn,
    DateTimeOffset RecordedAt, Guid RecordedBy, string? EvidenceRef, int ItemCount);
public sealed record SprayDeclarationListResponse(IReadOnlyList<SprayDeclarationSummary> Items,
    int Page, int PageSize, bool HasMore);

public static class SprayDeclarationEndpoints
{
    private const string EventType = "SPRAY_DECLARATION_RECORDED";

    public static IEndpointRouteBuilder MapSprayDeclarationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/spray-declarations").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Spray declarations");
        group.MapPost("", RecordAsync).RequireAuthorization("farms.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> RecordAsync(RecordSprayDeclarationRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CatalogDbContext catalogDb,
        TimeProvider clock, CancellationToken ct)
    {
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var payload = request.Payload!;
        var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deviceId = request.DeviceId!.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var appliedOn = payload.AppliedOn;
        var informationSource = payload.InformationSource!.Trim();
        var evidenceRef = Clean(payload.EvidenceRef);
        var inputs = payload.Items!.Select(x => new SprayItemInput(Clean(x!.ProductName), x.SubstanceId,
            x.Rate, x.RateUnit!.Trim().ToUpperInvariant())).ToArray();
        var canonical = JsonSerializer.Serialize(new { operationType = EventType, request.PayloadSchemaVersion,
            capturedAt, payload.EntityId, payload.PlotSeasonId, appliedOn, informationSource, evidenceRef, items = inputs });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockKey = $"{companyId.Value:N}:{deviceId}:{request.ClientOperationId:N}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId.Value && x.DeviceId == deviceId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash)
                return Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.");
            if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
                return Results.Ok(JsonSerializer.Deserialize<SprayDeclarationResponse>(prior.ResultPayload));
            return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
        }

        if (!await db.PlotSeasons.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId.Value && x.Id == payload.PlotSeasonId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The plot-season is not available.");
        if (await db.SprayDeclarations.AsNoTracking().AnyAsync(x => x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The spray declaration ID already exists.");

        var substanceIds = inputs.Where(x => x.SubstanceId.HasValue).Select(x => x.SubstanceId!.Value).Distinct().ToArray();
        var knownSubstances = await catalogDb.ActiveSubstances.AsNoTracking()
            .Where(x => substanceIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
        if (knownSubstances.Count != substanceIds.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["payload.items"] = ["An active substance does not exist in the catalog."] });
        var unitCodes = inputs.Select(x => x.RateUnit!).Distinct().ToArray();
        var knownUnits = await catalogDb.MeasurementUnits.AsNoTracking()
            .Where(x => unitCodes.Contains(x.Code) && x.QuantityKind == "APPLICATION_RATE")
            .Select(x => x.Code).ToListAsync(ct);
        if (knownUnits.Count != unitCodes.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["payload.items"] = ["Each rate unit must be an APPLICATION_RATE unit in the catalog."] });

        var now = clock.GetUtcNow();
        var header = new SprayDeclaration { Id = payload.EntityId, CompanyId = companyId.Value,
            PlotSeasonId = payload.PlotSeasonId, AppliedOn = appliedOn, RecordedBy = actorId,
            RecordedAt = now, EvidenceRef = evidenceRef };
        var items = inputs.Select(x => new SprayItem { Id = Guid.NewGuid(), CompanyId = companyId.Value,
            DeclarationId = header.Id, ProductName = x.ProductName, SubstanceId = x.SubstanceId,
            Rate = x.Rate, RateUnit = x.RateUnit! }).OrderBy(x => x.Id).ToArray();
        var itemResponses = items.Select(x => new SprayItemResponse(x.Id, x.ProductName, x.SubstanceId,
            x.Rate, x.RateUnit)).ToArray();
        var result = new SprayDeclarationResponse(header.Id, header.PlotSeasonId, header.AppliedOn,
            informationSource, header.EvidenceRef, capturedAt, now, actorId, itemResponses);
        var businessEvent = new CaptureBusinessEvent { CompanyId = companyId, ActorId = actorId,
            EventType = EventType, OccurredAt = capturedAt, RecordedAt = now,
            CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, sprayDeclarationId = header.Id,
                header.PlotSeasonId, appliedOn, informationSource, evidenceRef, items = itemResponses }) };
        db.SprayDeclarations.Add(header);
        db.SprayItems.AddRange(items);
        db.BusinessEvents.Add(businessEvent);
        db.OfflineOperations.Add(new CaptureOperation { CompanyId = companyId.Value, SubmittedBy = actorId,
            DeviceId = deviceId, ClientOperationId = request.ClientOperationId, PayloadHash = hash,
            Payload = canonical, CapturedAt = capturedAt, ReceivedAt = now, Status = "APPLIED",
            AppliedEventId = businessEvent.Id, ResultPayload = JsonSerializer.Serialize(result) });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            if (pg.ConstraintName == "PK_spray_declarations")
                return Problem(409, "ENTITY_ALREADY_EXISTS", "The spray declaration ID already exists.");
            if (pg.ConstraintName == "ux_offline_operations_company_device_operation")
                return Problem(409, "OPERATION_CONFLICT", "The operation ID was used concurrently. Retry the original request.");
            throw;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } pg)
        {
            if (pg.ConstraintName == "fk_spray_declarations_company_plot_season")
                return Problem(409, "SOURCE_RECORD_NOT_READY", "The plot-season is not available.");
            if (pg.ConstraintName == "fk_spray_items_substance")
                return Problem(409, "CATALOG_REFERENCE_CHANGED", "An active substance changed. Read the catalog again.");
            throw;
        }
        return Results.Created($"/api/spray-declarations/{header.Id}", result);
    }

    private static async Task<IResult> ListAsync(HttpContext http, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        if (!TryQuery(http, out var plotSeasonId, out var page, out var pageSize))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid filter or pagination value."] });
        var query = db.SprayDeclarations.AsNoTracking().Where(x => x.CompanyId == companyId.Value);
        if (plotSeasonId is { } id) query = query.Where(x => x.PlotSeasonId == id);
        var rows = await query.OrderByDescending(x => x.AppliedOn).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(x => new SprayDeclarationSummary(x.Id, x.PlotSeasonId, x.AppliedOn,
                x.RecordedAt, x.RecordedBy, x.EvidenceRef,
                db.SprayItems.Count(item => item.CompanyId == companyId.Value && item.DeclarationId == x.Id)))
            .ToListAsync(ct);
        return Results.Ok(new SprayDeclarationListResponse(rows.Take(pageSize).ToArray(),
            page, pageSize, rows.Count > pageSize));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        if (companyId is null || !await access.CanAccessOrganizationAsync(companyId.Value, ct)) return Results.Forbid();
        var header = await db.SprayDeclarations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId.Value && x.Id == id, ct);
        if (header is null) return Problem(404, "SPRAY_DECLARATION_NOT_FOUND", "Spray declaration not found.");
        var eventFilter = JsonSerializer.Serialize(new { sprayDeclarationId = id });
        var businessEvent = await db.BusinessEvents.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId.Value && x.EventType == EventType &&
            EF.Functions.JsonContains(x.Payload, eventFilter), ct);
        if (businessEvent is null)
            return Problem(500, "SPRAY_EVENT_MISSING", "The declaration history is incomplete.");
        using var eventPayload = JsonDocument.Parse(businessEvent.Payload);
        if (!eventPayload.RootElement.TryGetProperty("informationSource", out var source) ||
            source.ValueKind != JsonValueKind.String)
            return Problem(500, "SPRAY_EVENT_INVALID", "The declaration history is incomplete.");
        var items = await db.SprayItems.AsNoTracking().Where(x =>
            x.CompanyId == companyId.Value && x.DeclarationId == header.Id)
            .OrderBy(x => x.Id).Select(x => new SprayItemResponse(x.Id, x.ProductName,
                x.SubstanceId, x.Rate, x.RateUnit)).ToListAsync(ct);
        return Results.Ok(new SprayDeclarationResponse(header.Id, header.PlotSeasonId, header.AppliedOn,
            source.GetString()!, header.EvidenceRef, businessEvent.OccurredAt, header.RecordedAt,
            header.RecordedBy, items));
    }

    private static Dictionary<string, string[]> Validate(RecordSprayDeclarationRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Trim().Length > 100)
            errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (request.ClientOperationId == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (request.CapturedAt == default) errors["capturedAt"] = ["Capture time is required."];
        if (request.PayloadSchemaVersion != 1) errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        if (request.Payload is null) { errors["payload"] = ["Spray data is required."]; return errors; }
        var payload = request.Payload;
        if (payload.EntityId == Guid.Empty) errors["payload.entityId"] = ["Declaration ID is required."];
        if (payload.PlotSeasonId == Guid.Empty) errors["payload.plotSeasonId"] = ["Plot-season ID is required."];
        if (payload.AppliedOn == default || (request.CapturedAt != default &&
            payload.AppliedOn > DateOnly.FromDateTime(request.CapturedAt.ToOffset(TimeSpan.FromHours(7)).DateTime)))
            errors["payload.appliedOn"] = ["Application date must exist and must not be after the capture date (Vietnam time)."];
        if (string.IsNullOrWhiteSpace(payload.InformationSource))
            errors["payload.informationSource"] = ["Information source is required."];
        if (payload.Items is null || payload.Items.Count == 0)
        {
            errors["payload.items"] = ["At least one spray item is required."];
            return errors;
        }
        for (var i = 0; i < payload.Items.Count; i++)
        {
            var item = payload.Items[i];
            if (item is null) { errors[$"payload.items[{i}]"] = ["Spray item is required."]; continue; }
            if (Clean(item.ProductName) is null && item.SubstanceId is null)
                errors[$"payload.items[{i}].productName"] = ["Provide a product name or an active substance."];
            if (item.SubstanceId == Guid.Empty)
                errors[$"payload.items[{i}].substanceId"] = ["Active substance ID must not be empty."];
            if (item.Rate <= 0 || item.Rate >= 1_000_000_000_000m ||
                decimal.Truncate(item.Rate * 1_000_000_000_000m) != item.Rate * 1_000_000_000_000m)
                errors[$"payload.items[{i}].rate"] = ["Rate must be positive and fit numeric(24,12) exactly."];
            if (string.IsNullOrWhiteSpace(item.RateUnit) || item.RateUnit.Trim().Length > 40)
                errors[$"payload.items[{i}].rateUnit"] = ["Rate unit is required and must not exceed 40 characters."];
        }
        return errors;
    }

    private static bool TryQuery(HttpContext http, out Guid? plotSeasonId, out int page, out int pageSize)
    {
        plotSeasonId = null; page = 1; pageSize = 20;
        var query = http.Request.Query;
        if (query.TryGetValue("plotSeasonId", out var plotValues))
        {
            if (plotValues.Count != 1 || !Guid.TryParse(plotValues[0], out var parsed) || parsed == Guid.Empty)
                return false;
            plotSeasonId = parsed;
        }
        if (query.TryGetValue("page", out var pageValues) &&
            (pageValues.Count != 1 || !int.TryParse(pageValues[0], NumberStyles.None,
                CultureInfo.InvariantCulture, out page) || page < 1)) return false;
        if (query.TryGetValue("pageSize", out var sizeValues) &&
            (sizeValues.Count != 1 || !int.TryParse(sizeValues[0], NumberStyles.None,
                CultureInfo.InvariantCulture, out pageSize) || pageSize is < 1 or > 100)) return false;
        return page - 1 <= int.MaxValue / pageSize;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("farms.read", ct) || await access.HasPermissionAsync("farms.write", ct);
    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
