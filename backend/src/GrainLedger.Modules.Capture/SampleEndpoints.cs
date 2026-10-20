using System.Globalization;
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

public sealed record SampleInput(Guid EntityId, Guid SourceLotId, string? Code, string? SealCode,
    DateTimeOffset CollectedAt);
public sealed record CaptureSampleRequest(string? DeviceId, Guid ClientOperationId,
    DateTimeOffset CapturedAt, int PayloadSchemaVersion, SampleInput? Payload);
public sealed record SampleResponse(Guid Id, Guid SourceLotId, string Code, string SealCode,
    Guid CollectedBy, DateTimeOffset CollectedAt, DateTimeOffset CapturedAt,
    DateTimeOffset RecordedAt, int HandoverCount);
public sealed record SampleSummary(Guid Id, Guid SourceLotId, string Code, string SealCode,
    Guid CollectedBy, DateTimeOffset CollectedAt, int HandoverCount);
public sealed record SampleListResponse(IReadOnlyList<SampleSummary> Items, int Page, int PageSize, bool HasMore);

public sealed record SampleHandoverInput(Guid EntityId, string? FromParty, string? ToParty,
    DateTimeOffset HandedOverAt, string? SealCondition, string? EvidenceRef);
public sealed record RecordSampleHandoverRequest(string? DeviceId, Guid ClientOperationId,
    DateTimeOffset CapturedAt, int PayloadSchemaVersion, SampleHandoverInput? Payload);
public sealed record SampleHandoverResponse(Guid Id, Guid SampleId, Guid RecordedBy,
    string FromParty, string ToParty, DateTimeOffset HandedOverAt, string SealCondition,
    string? EvidenceRef, DateTimeOffset CapturedAt, DateTimeOffset RecordedAt);
public sealed record SampleHandoverSummary(Guid Id, Guid SampleId, Guid RecordedBy,
    string FromParty, string ToParty, DateTimeOffset HandedOverAt, string SealCondition,
    string? EvidenceRef);
public sealed record SampleHandoverListResponse(IReadOnlyList<SampleHandoverSummary> Items,
    int Page, int PageSize, bool HasMore);

public static class SampleEndpoints
{
    private const string SampleEvent = "SAMPLE_COLLECTED";
    private const string HandoverEvent = "SAMPLE_HANDOVER_RECORDED";

    public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/samples").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Samples");
        group.MapPost("", CreateAsync).RequireAuthorization("purchases.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/handovers", HandoverAsync).RequireAuthorization("purchases.write");
        group.MapGet("/{id:guid}/handovers", ListHandoversAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CaptureSampleRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db,
        TimeProvider clock, CancellationToken ct)
    {
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var errors = ValidateEnvelope(request.DeviceId, request.ClientOperationId,
            request.CapturedAt, request.PayloadSchemaVersion, allowGeneratedCode: true);
        var payload = request.Payload;
        if (payload is null) errors["payload"] = ["Sample data is required."];
        else
        {
            if (payload.EntityId == Guid.Empty) errors["payload.entityId"] = ["Sample ID is required."];
            if (payload.SourceLotId == Guid.Empty) errors["payload.sourceLotId"] = ["Source lot ID is required."];
            if (request.PayloadSchemaVersion == 1 &&
                (string.IsNullOrWhiteSpace(payload.Code) || payload.Code.Trim().Length > 60))
                errors["payload.code"] = ["Sample code is required and must not exceed 60 characters."];
            if (request.PayloadSchemaVersion == 2 && payload.Code is not null)
                errors["payload.code"] = ["The server assigns the sample code."];
            if (string.IsNullOrWhiteSpace(payload.SealCode) || payload.SealCode.Trim().Length > 100)
                errors["payload.sealCode"] = ["Seal code is required and must not exceed 100 characters."];
            if (payload.CollectedAt == default || request.CapturedAt != default && payload.CollectedAt > request.CapturedAt)
                errors["payload.collectedAt"] = ["Collection time must exist and must not follow capture time."];
        }
        if (errors.Count != 0) return Results.ValidationProblem(errors);

        var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var collectedAt = payload!.CollectedAt.ToUniversalTime();
        var deviceId = request.DeviceId!.Trim();
        var legacyCode = payload.Code?.Trim();
        var sealCode = payload.SealCode!.Trim();
        var canonical = JsonSerializer.Serialize(new { operationType = SampleEvent,
            request.PayloadSchemaVersion, capturedAt, payload.EntityId, payload.SourceLotId,
            code = request.PayloadSchemaVersion == 1 ? legacyCode : null,
            sealCode, collectedAt });
        var hash = Hash(canonical);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        await LockAsync(db, companyId.Value, deviceId, request.ClientOperationId, ct);
        var prior = await FindOperationAsync(db, companyId.Value, deviceId, request.ClientOperationId, ct);
        if (prior is not null) return Replay<SampleResponse>(prior, hash);

        if (!await AcceptedSourceAsync(db, companyId.Value, payload.SourceLotId, ct))
            return Problem(409, "SOURCE_RECORD_NOT_READY", "The source lot has not been accepted.");
        if (await db.Samples.AsNoTracking().AnyAsync(x => x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The sample ID already exists.");
        var code = request.PayloadSchemaVersion == 1 ? legacyCode!
            : await CaptureCodes.DatedAsync(db, companyId.Value, "SMP", collectedAt, ct);
        if (await db.Samples.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct))
            return Problem(409, "SAMPLE_CODE_EXISTS", "The sample code already exists in this company.");

        var now = clock.GetUtcNow();
        var sample = new Sample { Id = payload.EntityId, CompanyId = companyId.Value, SourceLotId = payload.SourceLotId,
            Code = code, SealCode = sealCode, CollectedBy = actorId, CollectedAt = collectedAt };
        var response = new SampleResponse(sample.Id, sample.SourceLotId, code, sealCode,
            actorId, collectedAt, capturedAt, now, 0);
        var ev = new CaptureBusinessEvent { CompanyId = companyId, ActorId = actorId, EventType = SampleEvent,
            OccurredAt = capturedAt, RecordedAt = now, CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, sampleId = sample.Id, response }) };
        db.BusinessEvents.Add(ev);
        await db.SaveChangesAsync(ct);
        db.Samples.Add(sample);
        db.OfflineOperations.Add(Operation(companyId.Value, actorId, deviceId, request.ClientOperationId,
            hash, canonical, capturedAt, now, ev.Id, response));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            if (pg.ConstraintName == "ux_samples_company_code")
                return Problem(409, "SAMPLE_CODE_EXISTS", "The sample code already exists in this company.");
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The sample or operation ID already exists.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { return Problem(409, "SOURCE_RECORD_NOT_READY", "The source lot changed. Reload it and try again."); }
        return Results.Created($"/api/samples/{sample.Id}", response);
    }

    private static async Task<IResult> HandoverAsync(Guid id, RecordSampleHandoverRequest request,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var errors = ValidateEnvelope(request.DeviceId, request.ClientOperationId,
            request.CapturedAt, request.PayloadSchemaVersion);
        var payload = request.Payload;
        if (payload is null) errors["payload"] = ["Handover data is required."];
        else
        {
            if (payload.EntityId == Guid.Empty) errors["payload.entityId"] = ["Handover ID is required."];
            if (string.IsNullOrWhiteSpace(payload.FromParty)) errors["payload.fromParty"] = ["From party is required."];
            if (string.IsNullOrWhiteSpace(payload.ToParty)) errors["payload.toParty"] = ["To party is required."];
            if (string.IsNullOrWhiteSpace(payload.SealCondition)) errors["payload.sealCondition"] = ["Seal condition is required."];
            if (payload.HandedOverAt == default || request.CapturedAt != default && payload.HandedOverAt > request.CapturedAt)
                errors["payload.handedOverAt"] = ["Handover time must exist and must not follow capture time."];
        }
        if (errors.Count != 0) return Results.ValidationProblem(errors);

        var actorId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var deviceId = request.DeviceId!.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var handedOverAt = payload!.HandedOverAt.ToUniversalTime();
        var fromParty = payload.FromParty!.Trim();
        var toParty = payload.ToParty!.Trim();
        var sealCondition = payload.SealCondition!.Trim();
        var evidenceRef = Clean(payload.EvidenceRef);
        var canonical = JsonSerializer.Serialize(new { operationType = HandoverEvent,
            request.PayloadSchemaVersion, capturedAt, sampleId = id, payload.EntityId,
            fromParty, toParty, handedOverAt, sealCondition, evidenceRef });
        var hash = Hash(canonical);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, companyId.Value, deviceId, request.ClientOperationId, ct);
        var prior = await FindOperationAsync(db, companyId.Value, deviceId, request.ClientOperationId, ct);
        if (prior is not null) return Replay<SampleHandoverResponse>(prior, hash);

        var sample = await db.Samples.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        if (sample is null) return Problem(409, "SOURCE_RECORD_NOT_READY", "The sample has not been accepted.");
        if (handedOverAt < sample.CollectedAt)
            return Results.ValidationProblem(new Dictionary<string, string[]> {
                ["payload.handedOverAt"] = ["Handover time must not precede sample collection."] });
        if (await db.SampleHandovers.AsNoTracking().AnyAsync(x => x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The handover ID already exists.");

        var now = clock.GetUtcNow();
        var handover = new SampleHandover { Id = payload.EntityId, CompanyId = companyId.Value,
            SampleId = id, RecordedBy = actorId, FromParty = fromParty, ToParty = toParty,
            HandedOverAt = handedOverAt, SealCondition = sealCondition, EvidenceRef = evidenceRef };
        var response = new SampleHandoverResponse(handover.Id, id, actorId, fromParty, toParty,
            handedOverAt, sealCondition, evidenceRef, capturedAt, now);
        var ev = new CaptureBusinessEvent { CompanyId = companyId, ActorId = actorId, EventType = HandoverEvent,
            OccurredAt = capturedAt, RecordedAt = now, CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, sampleId = id,
                handoverId = handover.Id, response }) };
        db.BusinessEvents.Add(ev);
        await db.SaveChangesAsync(ct);
        db.SampleHandovers.Add(handover);
        db.OfflineOperations.Add(Operation(companyId.Value, actorId, deviceId, request.ClientOperationId,
            hash, canonical, capturedAt, now, ev.Id, response));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Problem(409, "ENTITY_ALREADY_EXISTS", "The handover or operation ID already exists."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { return Problem(409, "SOURCE_RECORD_NOT_READY", "The sample changed. Reload it and try again."); }
        return Results.Created($"/api/samples/{id}/handovers", response);
    }

    private static async Task<IResult> ListAsync(HttpContext http, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        if (!TryPage(http, out var page, out var size)) return ValidationQuery();
        Guid? sourceLotId = null;
        if (http.Request.Query.TryGetValue("sourceLotId", out var filter))
        {
            if (filter.Count != 1 || !Guid.TryParse(filter[0], out var parsed) || parsed == Guid.Empty)
                return ValidationQuery();
            sourceLotId = parsed;
        }
        var query = db.Samples.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (sourceLotId is { } id) query = query.Where(x => x.SourceLotId == id);
        var rows = await query.OrderByDescending(x => x.CollectedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size + 1)
            .Select(x => new SampleSummary(x.Id, x.SourceLotId, x.Code, x.SealCode,
                x.CollectedBy, x.CollectedAt,
                db.SampleHandovers.Count(h => h.CompanyId == companyId && h.SampleId == x.Id)))
            .ToListAsync(ct);
        return Results.Ok(new SampleListResponse(rows.Take(size).ToArray(), page, size, rows.Count > size));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        var sample = await db.Samples.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, ct);
        if (sample is null) return Problem(404, "SAMPLE_NOT_FOUND", "Sample not found.");
        var eventFilter = JsonSerializer.Serialize(new { sampleId = id });
        var ev = await db.BusinessEvents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
            x.EventType == SampleEvent && EF.Functions.JsonContains(x.Payload, eventFilter), ct);
        if (ev is null) return Problem(500, "SAMPLE_EVENT_MISSING", "The sample history is incomplete.");
        var count = await db.SampleHandovers.CountAsync(x => x.CompanyId == companyId && x.SampleId == id, ct);
        return Results.Ok(new SampleResponse(sample.Id, sample.SourceLotId, sample.Code, sample.SealCode,
            sample.CollectedBy, sample.CollectedAt, ev.OccurredAt, ev.RecordedAt, count));
    }

    private static async Task<IResult> ListHandoversAsync(Guid id, HttpContext http,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var companyId = await CompanyAsync(principal, access, identityDb, ct);
        if (companyId is null) return Results.Forbid();
        if (!TryPage(http, out var page, out var size)) return ValidationQuery();
        if (!await db.Samples.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == id, ct))
            return Problem(404, "SAMPLE_NOT_FOUND", "Sample not found.");
        var rows = await db.SampleHandovers.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SampleId == id)
            .OrderByDescending(x => x.HandedOverAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size + 1)
            .Select(x => new SampleHandoverSummary(x.Id, x.SampleId, x.RecordedBy,
                x.FromParty, x.ToParty, x.HandedOverAt, x.SealCondition, x.EvidenceRef))
            .ToListAsync(ct);
        return Results.Ok(new SampleHandoverListResponse(rows.Take(size).ToArray(),
            page, size, rows.Count > size));
    }

    private static async Task<bool> AcceptedSourceAsync(CaptureDbContext db, Guid companyId, Guid sourceLotId,
        CancellationToken ct) => await (from source in db.SourceLots.AsNoTracking()
            join line in db.ProcurementItems.AsNoTracking() on new { source.CompanyId, Id = source.ProcurementItemId }
                equals new { line.CompanyId, line.Id }
            join receipt in db.Procurements.AsNoTracking() on new { line.CompanyId, Id = line.ProcurementId }
                equals new { receipt.CompanyId, receipt.Id }
            join ev in db.BusinessEvents.AsNoTracking() on receipt.EventId equals (Guid?)ev.Id
            where source.CompanyId == companyId && source.Id == sourceLotId &&
                receipt.Status == "COMMITTED" && ev.CompanyId == companyId && ev.EventType == "PROCUREMENT_COMMITTED"
            select source.Id).AnyAsync(ct);

    private static Dictionary<string, string[]> ValidateEnvelope(string? deviceId, Guid operationId,
        DateTimeOffset capturedAt, int version, bool allowGeneratedCode = false)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Trim().Length > 100)
            errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (operationId == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (capturedAt == default) errors["capturedAt"] = ["Capture time is required."];
        if (version != 1 && (!allowGeneratedCode || version != 2))
            errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        return errors;
    }

    private static async Task<Guid?> CompanyAsync(ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CancellationToken ct)
    {
        var id = await CaptureActor.GetCustomerCompanyAsync(principal, identityDb, ct);
        return id is not null && await access.CanAccessOrganizationAsync(id.Value, ct) ? id : null;
    }

    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("purchases.write", ct) || await access.HasPermissionAsync("farms.read", ct);

    private static async Task LockAsync(CaptureDbContext db, Guid companyId, string deviceId,
        Guid operationId, CancellationToken ct)
    {
        var key = $"{companyId:N}:{deviceId}:{operationId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }

    private static Task<CaptureOperation?> FindOperationAsync(CaptureDbContext db, Guid companyId,
        string deviceId, Guid operationId, CancellationToken ct) => db.OfflineOperations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DeviceId == deviceId &&
            x.ClientOperationId == operationId, ct);

    private static IResult Replay<T>(CaptureOperation prior, string hash)
    {
        if (prior.PayloadHash != hash)
            return Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.");
        if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
            return Results.Ok(JsonSerializer.Deserialize<T>(prior.ResultPayload));
        return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
    }

    private static CaptureOperation Operation<T>(Guid companyId, Guid actorId, string deviceId,
        Guid operationId, string hash, string canonical, DateTimeOffset capturedAt,
        DateTimeOffset recordedAt, Guid eventId, T response) => new()
        {
            CompanyId = companyId, SubmittedBy = actorId, DeviceId = deviceId,
            ClientOperationId = operationId, PayloadHash = hash, Payload = canonical,
            CapturedAt = capturedAt, ReceivedAt = recordedAt, Status = "APPLIED",
            AppliedEventId = eventId, ResultPayload = JsonSerializer.Serialize(response)
        };

    private static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool TryPage(HttpContext http, out int page, out int size)
    {
        page = 1; size = 20;
        if (http.Request.Query.TryGetValue("page", out var p) &&
            (p.Count != 1 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)) return false;
        if (http.Request.Query.TryGetValue("pageSize", out var s) &&
            (s.Count != 1 || !int.TryParse(s[0], NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 100)) return false;
        return page - 1 <= int.MaxValue / size;
    }

    private static IResult ValidationQuery() => Results.ValidationProblem(new Dictionary<string, string[]> {
        ["query"] = ["Invalid filter or pagination value."] });
    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
