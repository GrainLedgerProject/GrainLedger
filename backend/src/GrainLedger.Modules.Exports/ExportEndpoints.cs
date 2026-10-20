using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GrainLedger.Modules.Exports;

public static class ExportModule
{
    private sealed class ReadyFilter(ExportDbContext db) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            if (!await ReadyAsync(db, context.HttpContext.RequestAborted))
                return Problem(503, "EXPORT_SCHEMA_NOT_READY", "Export order storage is not deployed yet.");
            return await next(context);
        }
    }

    private static async Task<bool> ReadyAsync(ExportDbContext db, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<bool>("""
            SELECT COALESCE(
                to_regnamespace('exports') IS NOT NULL
                AND has_schema_privilege(current_user, to_regnamespace('exports'), 'USAGE')
                AND (SELECT bool_and(
                    to_regclass('exports.' || required.name) IS NOT NULL
                    AND has_table_privilege(current_user, to_regclass('exports.' || required.name), 'SELECT,INSERT,UPDATE'))
                     FROM (VALUES ('export_orders'), ('order_specifications'), ('specification_limits'),
                                  ('containers'), ('events'), ('idempotent_operations'), ('code_counters')) AS required(name)),
                false) AS "Value"
            """).SingleAsync(ct);

    public static IServiceCollection AddExportsModule(this IServiceCollection services)
    {
        services.AddDbContext<ExportDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger")
                ?? throw new InvalidOperationException("ConnectionStrings:GrainLedger is required."),
            n => n.MigrationsAssembly(typeof(ExportDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "exports")));
        return services;
    }

    public static IEndpointRouteBuilder MapExportsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/export-orders").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .AddEndpointFilter<ReadyFilter>()
            .WithTags("Export orders");
        endpoints.MapGet("/api/export-availability", async (ExportDbContext db, CancellationToken ct) =>
                Results.Ok(new { ready = await ReadyAsync(db, ct) }))
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>();
        group.MapGet("", ListAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapPost("", CreateAsync).RequireAuthorization(Permissions.ExportOrdersWrite);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization(Permissions.ExportOrdersWrite);
        group.MapGet("/{id:guid}/specifications", SpecificationsAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapPost("/{id:guid}/specifications", AddSpecificationAsync).RequireAuthorization(Permissions.ExportOrdersWrite);
        group.MapGet("/{id:guid}/containers", ContainersAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapPost("/{id:guid}/containers", AddContainersAsync).RequireAuthorization(Permissions.ExportOrdersWrite);
        group.MapGet("/{id:guid}/history", HistoryAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        endpoints.MapGet("/api/export-containers/{id:guid}", ContainerAsync)
            .RequireAuthorization(Permissions.ExportOrdersRead).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<ReadyFilter>();
        endpoints.MapPatch("/api/export-containers/{id:guid}", UpdateContainerAsync)
            .RequireAuthorization(Permissions.ExportOrdersWrite).AddEndpointFilter<NoStoreFilter>()
            .AddEndpointFilter<MutationAntiforgeryFilter>().AddEndpointFilter<ReadyFilter>();
        return endpoints;
    }

    private static async Task<(Guid Actor, Guid Company)?> ScopeAsync(HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, string permission, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            !await access.HasPermissionAsync(permission, ct)) return null;
        var user = await identity.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == actor && x.IsActive, ct);
        if (user?.OrganizationId is not Guid company || user.Organization?.Kind != OrganizationKind.Customer ||
            user.Role is not (Roles.ExportStaff or Roles.CustomerManager)) return null;
        return (actor, company);
    }

    private static IResult Invalid(Dictionary<string, string[]> errors) => Results.ValidationProblem(errors);
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status,
        title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
    private static bool Page(string? rawPage, string? rawSize, out int page, out int size)
    {
        page = 1; size = 25;
        return (rawPage is null || int.TryParse(rawPage, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page > 0) &&
               (rawSize is null || int.TryParse(rawSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) && size is >= 1 and <= 100) &&
               page - 1 <= int.MaxValue / size;
    }
    private static string Etag(int version) => $"\"{version}\"";
    private static bool Matches(HttpContext http, int version) => http.Request.Headers.IfMatch == Etag(version);
    private static void SetEtag(HttpContext http, int version) => http.Response.Headers.ETag = Etag(version);
    private static IResult Replay(ExportOperation prior, string hash, HttpContext http)
    {
        if (prior.RequestHash != hash)
            return Problem(409, "EXPORT_OPERATION_CONFLICT", "Operation ID was used for another request.");
        SetEtag(http, prior.ResponseVersion);
        if (prior.ResponseStatus == 201) http.Response.Headers.Location = $"/api/export-orders/{prior.OrderId}";
        return Results.Json(JsonSerializer.Deserialize<JsonElement>(prior.ResponseJson), statusCode: prior.ResponseStatus);
    }
    private static async Task StoreResultAsync<T>(ExportDbContext db, ExportOperation operation,
        T response, int status, int version, CancellationToken ct)
    {
        operation.ResponseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        operation.ResponseStatus = status;
        operation.ResponseVersion = version;
        db.Operations.Add(operation);
        await db.SaveChangesAsync(ct);
    }
    private static ExportContainerDto ToDto(ExportContainer c) => new(c.Id, c.OrderId, c.InternalCode,
        ExportRules.Text(c.TargetMassKg), c.OriginalMass, c.OriginalUnit, c.Status, c.Version);
    private static ExportLimitDto ToDto(SpecificationLimit x) => new(x.AttributeId, x.AttributeCode,
        x.AttributeName, x.Unit, x.MinValue is null ? null : ExportRules.BoundText(x.MinValue.Value),
        x.MaxValue is null ? null : ExportRules.BoundText(x.MaxValue.Value));
    private static ExportOrderSummary ToSummary(ExportOrder o) => new(o.Id, o.Code, o.MarketId,
        o.CommodityId, ExportRules.Text(o.RequiredMassKg), o.ShippingFrom, o.ShippingTo, o.CreatedAt, o.Version);

    private static async Task<ExportOrderDto> DetailAsync(ExportDbContext db, ExportOrder order, bool canEdit,
        CancellationToken ct)
    {
        var spec = await db.Specifications.AsNoTracking().SingleAsync(x => x.OrderId == order.Id &&
            x.VersionNo == order.CurrentSpecificationVersion, ct);
        var limits = await db.Limits.AsNoTracking().Where(x => x.SpecificationId == spec.Id)
            .OrderBy(x => x.AttributeCode).ToListAsync(ct);
        var allocated = await db.Containers.AsNoTracking().Where(x => x.OrderId == order.Id)
            .SumAsync(x => (decimal?)x.TargetMassKg, ct) ?? 0m;
        return new(order.Id, order.Code, order.MarketId, order.CommodityId,
            ExportRules.Text(order.RequiredMassKg), order.OriginalMass, order.OriginalUnit,
            order.ShippingFrom, order.ShippingTo, order.Version, order.CurrentSpecificationVersion,
            limits.Select(ToDto).ToArray(),
            ExportRules.Text(allocated), ExportRules.Text(order.RequiredMassKg - allocated), canEdit);
    }

    private static async Task<IResult> ListAsync(HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        var q = http.Request.Query;
        if (!Page(q["page"], q["pageSize"], out var page, out var size) || q["q"].ToString().Length > 100 ||
            q["sortBy"].ToString() is not ("" or "createdAt" or "code" or "shippingFrom") ||
            q["sortDirection"].ToString() is not ("" or "asc" or "desc"))
            return Invalid(new() { ["query"] = ["Invalid search, sort or pagination."] });
        IQueryable<ExportOrder> query = db.Orders.AsNoTracking().Where(x => x.CompanyId == scope.Value.Company);
        if (!string.IsNullOrWhiteSpace(q["q"]))
        {
            var search = q["q"].ToString().Trim();
            query = query.Where(x => x.Code.Contains(search));
        }
        if (Guid.TryParse(q["marketId"], out var market)) query = query.Where(x => x.MarketId == market);
        else if (!string.IsNullOrEmpty(q["marketId"])) return Invalid(new() { ["marketId"] = ["Invalid market ID."] });
        if (Guid.TryParse(q["commodityId"], out var commodity)) query = query.Where(x => x.CommodityId == commodity);
        else if (!string.IsNullOrEmpty(q["commodityId"])) return Invalid(new() { ["commodityId"] = ["Invalid commodity ID."] });
        if (DateOnly.TryParseExact(q["from"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            query = query.Where(x => x.ShippingFrom >= from);
        else if (!string.IsNullOrEmpty(q["from"])) return Invalid(new() { ["from"] = ["Use YYYY-MM-DD."] });
        if (DateOnly.TryParseExact(q["to"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            query = query.Where(x => x.ShippingTo <= to);
        else if (!string.IsNullOrEmpty(q["to"])) return Invalid(new() { ["to"] = ["Use YYYY-MM-DD."] });
        var total = await query.CountAsync(ct);
        var asc = q["sortDirection"] == "asc";
        query = q["sortBy"].ToString() switch
        {
            "code" => asc ? query.OrderBy(x => x.Code).ThenBy(x => x.Id) : query.OrderByDescending(x => x.Code).ThenBy(x => x.Id),
            "shippingFrom" => asc ? query.OrderBy(x => x.ShippingFrom).ThenBy(x => x.Id) : query.OrderByDescending(x => x.ShippingFrom).ThenBy(x => x.Id),
            _ => asc ? query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id) : query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
        };
        var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new ExportPage<ExportOrderSummary>(rows.Select(ToSummary).ToArray(), page, size,
            total, (long)page * size < total));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, IdentityDbContext identity,
        ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (order is null) return Results.NotFound();
        SetEtag(http, order.Version);
        return Results.Ok(await DetailAsync(db, order, await access.HasPermissionAsync(Permissions.ExportOrdersWrite, ct), ct));
    }

    private static async Task<IResult> CreateAsync(CreateExportOrderRequest request, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CatalogDbContext catalog,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersWrite, ct);
        if (scope is null) return Results.Forbid();
        var errors = ExportRules.ValidateHeader(request.MarketId, request.CommodityId, request.RequiredMass,
            request.Unit, request.ShippingFrom, request.ShippingTo, out var required);
        foreach (var entry in ExportRules.ValidateLimits(request.Limits)) errors[entry.Key] = entry.Value;
        var containerErrors = ExportRules.ValidateContainers(request.Containers, out var allocated);
        foreach (var entry in containerErrors) errors[entry.Key] = entry.Value;
        if (request.OperationId == Guid.Empty) errors["operationId"] = ["Provide a stable operation ID."];
        if (allocated > required) errors["containers"] = ["Container targets exceed the required mass."];
        if (errors.Count > 0) return Invalid(errors);
        var hash = ExportRules.Hash("create", request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null)
            return Replay(prior, hash, http);
        var catalogError = await CheckCatalogAsync(catalog, request.MarketId, request.CommodityId,
            request.Unit!, request.Limits!, request.Containers!, ct);
        if (catalogError.Count > 0) return Invalid(catalogError);
        var now = clock.GetUtcNow();
        var id = Guid.NewGuid();
        var order = new ExportOrder { Id = id, CompanyId = scope.Value.Company, Code = await CodeAsync(db,
            scope.Value.Company, "EXP", now, ct), MarketId = request.MarketId, CommodityId = request.CommodityId,
            RequiredMassKg = required, OriginalMass = request.RequiredMass!, OriginalUnit = request.Unit!,
            ShippingFrom = request.ShippingFrom, ShippingTo = request.ShippingTo,
            CreatedBy = scope.Value.Actor, CreatedAt = now };
        db.Orders.Add(order);
        await AddSpecificationRowsAsync(db, catalog, order, 1, request.Limits!, scope.Value.Actor, now, ct);
        await AddContainerRowsAsync(db, order, request.Containers!, now, ct);
        Event(db, order, scope.Value.Actor, "ORDER_CREATED", request, now);
        await db.SaveChangesAsync(ct);
        var response = await DetailAsync(db, order, true, ct);
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), ActorId = scope.Value.Actor,
            CompanyId = scope.Value.Company, OperationId = request.OperationId, RequestHash = hash, OrderId = id },
            response, 201, order.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, order.Version);
        return Results.Created($"/api/export-orders/{id}", response);
    }

    private static async Task<Dictionary<string, string[]>> CheckCatalogAsync(CatalogDbContext catalog,
        Guid marketId, Guid commodityId, string unit, IReadOnlyList<ExportLimitInput> limits,
        IReadOnlyList<ExportContainerInput> containers, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!await catalog.Markets.AsNoTracking().AnyAsync(x => x.Id == marketId, ct))
            errors["marketId"] = ["Select a configured market."];
        if (!await catalog.Commodities.AsNoTracking().AnyAsync(x => x.Id == commodityId, ct))
            errors["commodityId"] = ["Select an active commodity."];
        var requestedUnits = containers.Select(x => x.Unit).Append(unit).Distinct().ToArray();
        var configuredUnits = await catalog.MeasurementUnits.AsNoTracking()
            .Where(x => !x.IsDeleted && x.QuantityKind == "MASS" && requestedUnits.Contains(x.Code))
            .Select(x => x.Code).ToListAsync(ct);
        if (!configuredUnits.Contains(unit)) errors["unit"] = ["Choose a configured mass unit."];
        for (var i = 0; i < containers.Count; i++)
            if (!configuredUnits.Contains(containers[i].Unit!))
                errors[$"containers[{i}].unit"] = ["Choose a configured mass unit."];
        var attributeIds = limits.Select(x => x.AttributeId).Distinct().ToArray();
        var found = await catalog.QualityAttributes.AsNoTracking().Where(x => attributeIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(ct);
        for (var i = 0; i < limits.Count; i++)
            if (!found.Contains(limits[i].AttributeId))
                errors[$"specification.limits[{i}].attributeId"] = ["Select a configured quality attribute."];
        return errors;
    }

    private static async Task LockAsync(ExportDbContext db, Guid company, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"export:" + company.ToString("N")}, 0))", ct);

    private static async Task<string> CodeAsync(ExportDbContext db, Guid company, string kind,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO exports.code_counters(company_id,kind,next_value) VALUES ($1,$2,2)
            ON CONFLICT(company_id,kind) DO UPDATE SET next_value=exports.code_counters.next_value+1
            RETURNING next_value-1
            """, (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
        command.Parameters.AddWithValue(company);
        command.Parameters.AddWithValue(kind);
        var serial = (long)(await command.ExecuteScalarAsync(ct))!;
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        return $"{kind}-{local:yyyyMMdd}-{serial:D6}";
    }

    private static async Task AddSpecificationRowsAsync(ExportDbContext db, CatalogDbContext catalog,
        ExportOrder order, int version, IReadOnlyList<ExportLimitInput> input, Guid actor,
        DateTimeOffset now, CancellationToken ct)
    {
        var spec = new OrderSpecification { Id = Guid.NewGuid(), OrderId = order.Id, CompanyId = order.CompanyId,
            VersionNo = version, RecordedAt = now, RecordedBy = actor };
        db.Specifications.Add(spec);
        var ids = input.Select(x => x.AttributeId).ToArray();
        var attributes = await catalog.QualityAttributes.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        foreach (var item in input)
        {
            var attribute = attributes[item.AttributeId];
            ExportRules.TryBound(item.MinValue, out var min);
            ExportRules.TryBound(item.MaxValue, out var max);
            db.Limits.Add(new SpecificationLimit { Id = Guid.NewGuid(), SpecificationId = spec.Id,
                AttributeId = item.AttributeId, AttributeCode = attribute.Code,
                AttributeName = attribute.Name, Unit = attribute.Unit, MinValue = min, MaxValue = max });
        }
    }

    private static async Task<IReadOnlyList<ExportContainer>> AddContainerRowsAsync(ExportDbContext db,
        ExportOrder order, IReadOnlyList<ExportContainerInput> input, DateTimeOffset now, CancellationToken ct)
    {
        var rows = new List<ExportContainer>();
        foreach (var item in input)
        {
            ExportRules.TryMass(item.TargetMass, item.Unit, out var mass);
            var row = new ExportContainer { Id = Guid.NewGuid(), OrderId = order.Id, CompanyId = order.CompanyId,
                InternalCode = await CodeAsync(db, order.CompanyId, "CTN", now, ct), TargetMassKg = mass,
                OriginalMass = item.TargetMass!, OriginalUnit = item.Unit!, CreatedAt = now };
            db.Containers.Add(row);
            rows.Add(row);
        }
        return rows;
    }

    private static void Event(ExportDbContext db, ExportOrder order, Guid actor,
        string action, object snapshot, DateTimeOffset now) => db.Events.Add(new ExportEvent
        {
            Id = Guid.NewGuid(), OrderId = order.Id, CompanyId = order.CompanyId,
            ActorId = actor, Action = action, Snapshot = JsonSerializer.Serialize(snapshot), RecordedAt = now
        });

    private static async Task<IResult> SpecificationsAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.Orders.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct)) return Results.NotFound();
        if (!Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size))
            return Invalid(new() { ["query"] = ["Invalid pagination."] });
        var query = db.Specifications.AsNoTracking().Where(x => x.OrderId == id);
        var total = await query.CountAsync(ct);
        var specs = await query.OrderByDescending(x => x.VersionNo)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = specs.Select(x => x.Id).ToArray();
        var limits = await db.Limits.AsNoTracking().Where(x => ids.Contains(x.SpecificationId))
            .OrderBy(x => x.AttributeCode).ToListAsync(ct);
        return Results.Ok(new ExportPage<ExportSpecificationDto>(specs.Select(x =>
            new ExportSpecificationDto(x.VersionNo, x.RecordedAt,
                limits.Where(y => y.SpecificationId == x.Id).Select(ToDto).ToArray())).ToArray(),
            page, size, total, (long)page * size < total));
    }

    private static async Task<IResult> ContainersAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.Orders.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct)) return Results.NotFound();
        if (!Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size))
            return Invalid(new() { ["query"] = ["Invalid pagination."] });
        var query = db.Containers.AsNoTracking().Where(x => x.OrderId == id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new ExportPage<ExportContainerDto>(rows.Select(ToDto).ToArray(), page, size,
            total, (long)page * size < total));
    }

    private static async Task<IResult> ContainerAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        var row = await db.Containers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            x.CompanyId == scope.Value.Company, ct);
        if (row is null) return Results.NotFound();
        SetEtag(http, row.Version);
        return Results.Ok(ToDto(row));
    }

    private static async Task<IResult> HistoryAsync(Guid id, HttpContext http,
        IdentityDbContext identity, ICurrentAccess access, ExportDbContext db, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersRead, ct);
        if (scope is null) return Results.Forbid();
        if (!await db.Orders.AnyAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct)) return Results.NotFound();
        if (!Page(http.Request.Query["page"], http.Request.Query["pageSize"], out var page, out var size))
            return Invalid(new() { ["query"] = ["Invalid pagination."] });
        var query = db.Events.AsNoTracking().Where(x => x.OrderId == id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.RecordedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var actorIds = rows.Select(x => x.ActorId).Distinct().ToArray();
        var actorNames = await identity.Users.AsNoTracking().Where(x => actorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return Results.Ok(new ExportPage<ExportEventDto>(rows.Select(x => new ExportEventDto(x.Action,
            x.RecordedAt, x.Snapshot, actorNames.GetValueOrDefault(x.ActorId, "Unknown user")))
            .ToArray(), page, size, total, (long)page * size < total));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateExportOrderRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db,
        CatalogDbContext catalog, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersWrite, ct);
        if (scope is null) return Results.Forbid();
        var errors = ExportRules.ValidateHeader(request.MarketId, request.CommodityId, request.RequiredMass,
            request.Unit, request.ShippingFrom, request.ShippingTo, out var mass);
        if (request.OperationId == Guid.Empty) errors["operationId"] = ["Provide a stable operation ID."];
        if (errors.Count > 0) return Invalid(errors);
        var hash = ExportRules.Hash("update-order:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null)
            return Replay(prior, hash, http);
        var catalogErrors = await CheckCatalogAsync(catalog, request.MarketId, request.CommodityId,
            request.Unit!, [], [], ct);
        if (catalogErrors.Count > 0) return Invalid(catalogErrors);
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (order is null) return Results.NotFound();
        if (!Matches(http, order.Version)) return Problem(412, "EXPORT_VERSION_STALE", "Reload the order before editing.");
        var allocated = await db.Containers.Where(x => x.OrderId == id).SumAsync(x => (decimal?)x.TargetMassKg, ct) ?? 0m;
        if (mass < allocated) return Invalid(new() { ["requiredMass"] = ["Required mass is below allocated container targets."] });
        order.MarketId = request.MarketId; order.CommodityId = request.CommodityId;
        order.RequiredMassKg = mass; order.OriginalMass = request.RequiredMass!; order.OriginalUnit = request.Unit!;
        order.ShippingFrom = request.ShippingFrom; order.ShippingTo = request.ShippingTo; order.Version++;
        var now = clock.GetUtcNow();
        Event(db, order, scope.Value.Actor, "ORDER_UPDATED", request, now);
        await db.SaveChangesAsync(ct);
        var response = await DetailAsync(db, order, true, ct);
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), CompanyId = scope.Value.Company,
            ActorId = scope.Value.Actor, OperationId = request.OperationId, RequestHash = hash, OrderId = id },
            response, 200, order.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, order.Version);
        return Results.Ok(response);
    }

    private static async Task<IResult> AddSpecificationAsync(Guid id, AddExportSpecificationRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db,
        CatalogDbContext catalog, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersWrite, ct);
        if (scope is null) return Results.Forbid();
        var errors = ExportRules.ValidateLimits(request.Limits);
        if (request.OperationId == Guid.Empty) errors["operationId"] = ["Provide a stable operation ID."];
        if (errors.Count > 0) return Invalid(errors);
        var hash = ExportRules.Hash("add-specification:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null)
            return Replay(prior, hash, http);
        var ids = request.Limits!.Select(x => x.AttributeId).ToArray();
        var found = await catalog.QualityAttributes.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => x.Id).ToArrayAsync(ct);
        if (found.Length != ids.Length) return Invalid(new() { ["specification.limits"] = ["Select configured quality attributes."] });
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (order is null) return Results.NotFound();
        if (!Matches(http, order.Version)) return Problem(412, "EXPORT_VERSION_STALE", "Reload the order before changing specifications.");
        order.CurrentSpecificationVersion++; order.Version++;
        var now = clock.GetUtcNow();
        await AddSpecificationRowsAsync(db, catalog, order, order.CurrentSpecificationVersion,
            request.Limits!, scope.Value.Actor, now, ct);
        Event(db, order, scope.Value.Actor, "SPECIFICATION_VERSION_ADDED", request, now);
        await db.SaveChangesAsync(ct);
        var response = await DetailAsync(db, order, true, ct);
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), CompanyId = scope.Value.Company,
            ActorId = scope.Value.Actor, OperationId = request.OperationId, RequestHash = hash,
            OrderId = id, SpecificationVersion = order.CurrentSpecificationVersion },
            response, 200, order.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, order.Version);
        return Results.Ok(response);
    }

    private static async Task<IResult> AddContainersAsync(Guid id, AddExportContainersRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db,
        CatalogDbContext catalog, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersWrite, ct);
        if (scope is null) return Results.Forbid();
        var errors = ExportRules.ValidateContainers(request.Containers, out var mass);
        if (request.OperationId == Guid.Empty) errors["operationId"] = ["Provide a stable operation ID."];
        if (errors.Count > 0) return Invalid(errors);
        var hash = ExportRules.Hash("add-containers:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null)
            return Replay(prior, hash, http);
        var units = request.Containers!.Select(x => x.Unit).Distinct().ToArray();
        var configured = await catalog.MeasurementUnits.AsNoTracking().Where(x => !x.IsDeleted &&
            x.QuantityKind == "MASS" && units.Contains(x.Code)).Select(x => x.Code).ToListAsync(ct);
        if (configured.Count != units.Length) return Invalid(new() { ["containers"] = ["Choose configured mass units."] });
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (order is null) return Results.NotFound();
        if (!Matches(http, order.Version)) return Problem(412, "EXPORT_VERSION_STALE", "Reload the order before adding containers.");
        var allocated = await db.Containers.Where(x => x.OrderId == id).SumAsync(x => (decimal?)x.TargetMassKg, ct) ?? 0m;
        if (allocated + mass > order.RequiredMassKg)
            return Invalid(new() { ["containers"] = ["Container targets exceed the required mass."] });
        var now = clock.GetUtcNow();
        var rows = await AddContainerRowsAsync(db, order, request.Containers!, now, ct);
        order.Version++;
        Event(db, order, scope.Value.Actor, "CONTAINERS_ADDED", request, now);
        await db.SaveChangesAsync(ct);
        var response = await DetailAsync(db, order, true, ct);
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), CompanyId = scope.Value.Company,
            ActorId = scope.Value.Actor, OperationId = request.OperationId, RequestHash = hash,
            OrderId = id, ContainerId = rows.Count == 1 ? rows[0].Id : null },
            response, 200, order.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, order.Version);
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateContainerAsync(Guid id, UpdateExportContainerRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access, ExportDbContext db,
        CatalogDbContext catalog, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, access, Permissions.ExportOrdersWrite, ct);
        if (scope is null) return Results.Forbid();
        var errors = new Dictionary<string, string[]>();
        if (!ExportRules.TryMass(request.TargetMass, request.Unit, out var mass))
            errors["targetMass"] = ["Enter a positive mass in KG, G or T without rounding."];
        if (request.OperationId == Guid.Empty) errors["operationId"] = ["Provide a stable operation ID."];
        if (errors.Count > 0) return Invalid(errors);
        var hash = ExportRules.Hash("update-container:" + id, request);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, scope.Value.Company, ct);
        var prior = await db.Operations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == scope.Value.Company &&
            x.ActorId == scope.Value.Actor && x.OperationId == request.OperationId, ct);
        if (prior is not null)
            return Replay(prior, hash, http);
        if (!await catalog.MeasurementUnits.AsNoTracking().AnyAsync(x => x.Code == request.Unit &&
            x.QuantityKind == "MASS" && !x.IsDeleted, ct))
            return Invalid(new() { ["unit"] = ["Choose a configured mass unit."] });
        var row = await db.Containers.SingleOrDefaultAsync(x => x.Id == id && x.CompanyId == scope.Value.Company, ct);
        if (row is null) return Results.NotFound();
        if (!Matches(http, row.Version)) return Problem(412, "EXPORT_VERSION_STALE", "Reload the container before editing.");
        if (row.Status != "PLANNED") return Problem(409, "CONTAINER_IN_USE", "Only planned container targets may be edited.");
        var order = await db.Orders.SingleAsync(x => x.Id == row.OrderId, ct);
        var allocated = await db.Containers.Where(x => x.OrderId == row.OrderId).SumAsync(x => (decimal?)x.TargetMassKg, ct) ?? 0m;
        if (allocated - row.TargetMassKg + mass > order.RequiredMassKg)
            return Invalid(new() { ["targetMass"] = ["Container targets exceed the required mass."] });
        row.TargetMassKg = mass; row.OriginalMass = request.TargetMass!; row.OriginalUnit = request.Unit!;
        row.Version++; order.Version++;
        Event(db, order, scope.Value.Actor, "CONTAINER_UPDATED", new { id, request }, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var response = ToDto(row);
        await StoreResultAsync(db, new ExportOperation { Id = Guid.NewGuid(), CompanyId = scope.Value.Company,
            ActorId = scope.Value.Actor, OperationId = request.OperationId, RequestHash = hash,
            OrderId = row.OrderId, ContainerId = id }, response, 200, row.Version, ct);
        await transaction.CommitAsync(ct);
        SetEtag(http, row.Version);
        return Results.Ok(response);
    }
}
