using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record FarmerInput(Guid EntityId, string? Code, string? Name, string? Contact);
public sealed record RegisterFarmerRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion, FarmerInput? Payload);
public sealed record FarmerResponse(Guid Id, string Code, string Name, string? Contact, DateTimeOffset CreatedAt);
public sealed record FarmerReferenceItem(Guid Id, string Code, string Name, string? Contact, DateTimeOffset CreatedAt,
    Guid? CreatedBy, int Version, bool CanEdit, bool CanDelete);
public sealed record FarmerListResponse(IReadOnlyList<FarmerReferenceItem> Items,
    int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record UpdateFarmerRequest(string? Code, string? Name, string? Contact, int ExpectedVersion);
public sealed record DeleteReferenceRequest(int Version);

public static class CaptureModule
{
    public static IServiceCollection AddCaptureModule(this IServiceCollection services)
    {
        services.AddScoped<ISourceProvenanceReader, SourceProvenanceReader>();
        services.AddDbContext<CaptureDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:GrainLedger is required.");
            options.UseNpgsql(connection, n => n.MigrationsAssembly(typeof(CaptureDbContext).Assembly.FullName));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapCaptureModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/farmers").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
        group.MapPost("", RegisterAsync).RequireAuthorization("farms.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("farms.write");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("farms.write");
        endpoints.MapPlotEndpoints();
        endpoints.MapCropSeasonEndpoints();
        endpoints.MapPlotSeasonEndpoints();
        endpoints.MapSprayDeclarationEndpoints();
        endpoints.MapProcurementEndpoints();
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(RegisterFarmerRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null) return Results.Forbid();
        var companyId = actor.Value.CompanyId;
        if (!await access.CanAccessOrganizationAsync(companyId, ct)) return Results.Forbid();

        var errors = Validate(request);
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        var payload = request.Payload!;
        var deviceId = request.DeviceId!.Trim();
        var code = payload.Code is null
            ? $"ND-{payload.EntityId.ToString("N")[..12].ToUpperInvariant()}"
            : payload.Code.Trim();
        var name = payload.Name!.Trim();
        var contact = string.IsNullOrWhiteSpace(payload.Contact) ? null : payload.Contact.Trim();
        var capturedAt = request.CapturedAt.ToUniversalTime();
        var canonical = JsonSerializer.Serialize(new
        {
            operationType = "FARMER_REGISTERED", request.PayloadSchemaVersion, capturedAt,
            payload.EntityId, code, name, contact
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var lockKey = $"{companyId:N}:{deviceId}:{request.ClientOperationId:N}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor.Value.UserId,
                "farms.write", companyId, ct: ct))
            return Results.Forbid();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var prior = await db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.DeviceId == deviceId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash)
                return Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.");
            if (prior.Status == "APPLIED" && prior.ResultPayload is not null)
                return Results.Ok(JsonSerializer.Deserialize<FarmerResponse>(prior.ResultPayload));
            return Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
        }

        if (await db.Farmers.AnyAsync(x => x.CompanyId == companyId && x.Id == payload.EntityId, ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The farmer ID already exists.");
        if (await db.Farmers.AnyAsync(x => x.CompanyId == companyId && x.Code == code, ct))
            return Problem(409, "FARMER_CODE_EXISTS", "The farmer code already exists.");

        var now = clock.GetUtcNow();
        var farmer = new Farmer
        {
            Id = payload.EntityId, CompanyId = companyId, Code = code,
            Name = name, Contact = contact, CreatedAt = now, CreatedBy = actor.Value.UserId
        };
        var result = ToResponse(farmer);
        var businessEvent = new CaptureBusinessEvent
        {
            CompanyId = companyId, ActorId = actor.Value.UserId, EventType = "FARMER_REGISTERED",
            OccurredAt = capturedAt, RecordedAt = now, CorrelationId = request.ClientOperationId,
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, farmerId = farmer.Id, farmer.Code })
        };
        db.Farmers.Add(farmer);
        db.BusinessEvents.Add(businessEvent);
        try
        {
            // The database FK to business_events is cross-context. Save the event first,
            // then the operation, within the same transaction.
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "FARMER_CONFLICT", "The farmer ID or code already exists.");
        }
        return Results.Created($"/api/farmers/{farmer.Id}", result);
    }

    private static async Task<IResult> ListAsync(string? search, string? q, int? page, int? pageSize,
        string? sortBy, string? sortDirection, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        var term = search ?? q;
        if (page is < 1 || pageSize is < 1 or > 100 || term?.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search or pagination value."] });
        var currentPage = page ?? 1;
        var size = pageSize ?? 25;
        if (currentPage - 1 > int.MaxValue / size)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["page"] = ["Page is too large."] });
        var sort = sortBy?.ToLowerInvariant() ?? "createdat";
        var direction = sortDirection?.ToLowerInvariant() ?? "asc";
        if (sort is not ("name" or "code" or "createdat") || direction is not ("asc" or "desc"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sortBy"] = ["Unsupported sort field or direction."] });
        var query = db.Farmers.AsNoTracking().Where(x => x.CompanyId == actor.Value.CompanyId);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var totalCount = await query.CountAsync(ct);
        IOrderedQueryable<Farmer> ordered = (sort, direction == "desc") switch
        {
            ("name", false) => query.OrderBy(x => x.Name),
            ("name", true) => query.OrderByDescending(x => x.Name),
            ("code", false) => query.OrderBy(x => x.Code),
            ("code", true) => query.OrderByDescending(x => x.Code),
            ("createdat", false) => query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt)
        };
        var rows = await ordered.ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        var canManage = await access.HasPermissionAsync("farms.write", ct);
        var items = rows.Select(x => ToReferenceItem(x, actor.Value.UserId, canManage)).ToArray();
        return Results.Ok(new FarmerListResponse(items, currentPage, size, totalCount,
            (long)currentPage * size < totalCount));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        var farmer = await db.Farmers.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        if (farmer is null) return Problem(404, "FARMER_NOT_FOUND", "Farmer not found.");
        var canManage = await access.HasPermissionAsync("farms.write", ct);
        return Results.Ok(ToReferenceItem(farmer, actor.Value.UserId, canManage));
    }


    private static async Task<IResult> UpdateAsync(Guid id, UpdateFarmerRequest request,
        ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext identityDb,
        CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (request.ExpectedVersion < 1 ||
            (request.Code is not null && (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 40)) ||
            string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Invalid farmer values or version."] });
        var actor = await GetActorAsync(principal, identityDb, ct);
        if (actor is null || !await access.CanAccessOrganizationAsync(actor.Value.CompanyId, ct)) return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor.Value.UserId,
                "farms.write", actor.Value.CompanyId, ct: ct))
            return Results.Forbid();
        var farmer = await db.Farmers.SingleOrDefaultAsync(
            x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        if (farmer is null) return Problem(404, "FARMER_NOT_FOUND", "Farmer not found.");
        if (farmer.CreatedBy != actor.Value.UserId) return Results.Forbid();
        if (farmer.Version != request.ExpectedVersion) return Problem(412, "REFERENCE_STALE", "The farmer changed. Reload before editing.");
        if (await FarmerInUseAsync(db, actor.Value.CompanyId, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced farmer cannot be edited.");
        var code = request.Code?.Trim() ?? $"ND-{id.ToString("N")[..12].ToUpperInvariant()}";
        if (await db.Farmers.AnyAsync(x => x.CompanyId == actor.Value.CompanyId && x.Id != id && x.Code == code, ct))
            return Problem(409, "FARMER_CODE_EXISTS", "The farmer code already exists.");
        var before = new { farmer.Code, farmer.Name, farmer.Contact, farmer.Version };
        farmer.Code = code;
        farmer.Name = request.Name.Trim();
        farmer.Contact = string.IsNullOrWhiteSpace(request.Contact) ? null : request.Contact.Trim();
        farmer.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = farmer.CompanyId, ActorId = actor.Value.UserId, EventType = "FARMER_UPDATED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, farmerId = farmer.Id, before,
                after = new { farmer.Code, farmer.Name, farmer.Contact, farmer.Version } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The farmer changed. Reload before editing.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "FARMER_CODE_EXISTS", "The farmer code already exists.");
        }
        return Results.Ok(ToReferenceItem(farmer, actor.Value.UserId, true));
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
        var farmer = await db.Farmers.SingleOrDefaultAsync(
            x => x.CompanyId == actor.Value.CompanyId && x.Id == id, ct);
        if (farmer is null) return Problem(404, "FARMER_NOT_FOUND", "Farmer not found.");
        if (farmer.CreatedBy != actor.Value.UserId) return Results.Forbid();
        if (farmer.Version != request.Version) return Problem(412, "REFERENCE_STALE", "The farmer changed. Reload before deleting.");
        if (await FarmerInUseAsync(db, actor.Value.CompanyId, id, ct))
            return Problem(409, "REFERENCE_IN_USE", "A referenced farmer cannot be deleted.");
        var before = new { farmer.Code, farmer.Name, farmer.Contact, farmer.Version, farmer.IsDeleted };
        farmer.IsDeleted = true;
        farmer.Version++;
        var now = clock.GetUtcNow();
        db.BusinessEvents.Add(new CaptureBusinessEvent
        {
            CompanyId = farmer.CompanyId, ActorId = actor.Value.UserId, EventType = "FARMER_DELETED",
            OccurredAt = now, RecordedAt = now, CorrelationId = Guid.NewGuid(),
            Payload = JsonSerializer.Serialize(new { schemaVersion = 1, farmerId = farmer.Id, before,
                after = new { farmer.Code, farmer.Name, farmer.Contact, farmer.Version, farmer.IsDeleted } })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "REFERENCE_STALE", "The farmer changed. Reload before deleting.");
        }
        return Results.NoContent();
    }

    private static async Task<bool> FarmerInUseAsync(CaptureDbContext db, Guid companyId, Guid id, CancellationToken ct)
    {
        return await db.Plots.AnyAsync(x => x.CompanyId == companyId && x.FarmerId == id, ct) ||
            await db.Procurements.AnyAsync(x => x.CompanyId == companyId && x.FarmerId == id, ct);
    }

    private static FarmerReferenceItem ToReferenceItem(Farmer farmer, Guid actorId, bool canManage)
    {
        var owner = farmer.CreatedBy == actorId;
        return new FarmerReferenceItem(farmer.Id, farmer.Code, farmer.Name, farmer.Contact,
            farmer.CreatedAt, farmer.CreatedBy, farmer.Version, owner && canManage, owner && canManage);
    }
    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) =>
        await access.HasPermissionAsync("farms.read", ct) || await access.HasPermissionAsync("farms.write", ct);

    private static async Task<(Guid UserId, Guid CompanyId)?> GetActorAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user?.OrganizationId is not { } companyId || user.Organization?.Kind != OrganizationKind.Customer) return null;
        return (userId, companyId);
    }

    private static Dictionary<string, string[]> Validate(RegisterFarmerRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Trim().Length > 100) errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (request.ClientOperationId == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (request.CapturedAt == default) errors["capturedAt"] = ["Capture time is required."];
        if (request.PayloadSchemaVersion != 1) errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        if (request.Payload is null) { errors["payload"] = ["Farmer data is required."]; return errors; }
        if (request.Payload.EntityId == Guid.Empty) errors["payload.entityId"] = ["Farmer ID is required."];
        if (request.Payload.Code is not null &&
            (string.IsNullOrWhiteSpace(request.Payload.Code) || request.Payload.Code.Trim().Length > 40))
            errors["payload.code"] = ["If supplied, code must not be blank or exceed 40 characters."];
        if (string.IsNullOrWhiteSpace(request.Payload.Name)) errors["payload.name"] = ["Name is required."];
        return errors;
    }

    private static FarmerResponse ToResponse(Farmer farmer) =>
        new(farmer.Id, farmer.Code, farmer.Name, farmer.Contact, farmer.CreatedAt);

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
