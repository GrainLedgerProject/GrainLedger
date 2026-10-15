using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GrainLedger.Modules.Catalogs;

public sealed record CreateCommodityRequest(string? Code, string? Name);
public sealed record UpdateCommodityRequest(string? Name);
public sealed record CommodityResponse(Guid Id, string Code, string Name, Guid? CreatedBy, uint Version, bool CanEdit, bool CanDelete);
public sealed record CatalogPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, bool HasMore);

public static class CommodityEndpoints
{
    public static IEndpointRouteBuilder MapCommodityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commodities").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Commodities");
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization("platform.configure");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("platform.configure");
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateCommodityRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, ICommodityLedgerPort ledger, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40)
            errors["code"] = ["Code is required and must not exceed 40 characters."];
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["Name is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var commodity = new Commodity { Id = Guid.NewGuid(), Code = code!, Name = name!, CreatedBy = actorId.Value };
        if (await db.Commodities.AnyAsync(x => x.Code == commodity.Code, ct))
            return Problem(409, "COMMODITY_CODE_EXISTS", "Commodity code already exists.");
        db.Commodities.Add(commodity);
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "COMMODITY_CREATED", ResourceType = "COMMODITY",
            ResourceId = commodity.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            AfterState = State(commodity)
        });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await ledger.ProvisionAsync((NpgsqlConnection)db.Database.GetDbConnection(),
                (NpgsqlTransaction)transaction.GetDbTransaction(), actorId.Value, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Problem(409, "COMMODITY_CODE_EXISTS", "Commodity code already exists.");
        }
        SetEtag(http, commodity.Version);
        return Results.Created($"/api/commodities/{commodity.Id}", ToResponse(commodity, actorId.Value, true));
    }

    private static async Task<IResult> ListAsync(string? q, string? page, string? pageSize, string? sortBy, string? sortDirection,
        ClaimsPrincipal principal, IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(principal, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        if (!TryPage(page, pageSize, out var currentPage, out var size) || q?.Length > 100 ||
            sortBy is not (null or "code" or "name") || sortDirection is not (null or "asc" or "desc"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid search, sort, or pagination value."] });
        var canConfigure = await CanConfigureAsync(actorId.Value, identityDb, ct);
        var query = db.Commodities.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        }
        var totalCount = await query.CountAsync(ct);
        var descending = sortDirection == "desc";
        var rows = sortBy == "name"
            ? descending
                ? await query.OrderByDescending(x => x.Name).ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct)
                : await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct)
            : descending
                ? await query.OrderByDescending(x => x.Code).ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct)
                : await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((currentPage - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new CatalogPage<CommodityResponse>(rows.Select(x => ToResponse(x, actorId.Value, canConfigure)).ToList(),
            currentPage, size, totalCount, currentPage * (long)size < totalCount));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        var commodity = await db.Commodities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (commodity is null) return Problem(404, "COMMODITY_NOT_FOUND", "Commodity not found.");
        SetEtag(http, commodity.Version);
        return Results.Ok(ToResponse(commodity, actorId.Value, await CanConfigureAsync(actorId.Value, identityDb, ct)));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateCommodityRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, ICommodityLedgerPort ledger, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the ETag from GET in the If-Match header.");
        var commodity = await db.Commodities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (commodity is null) return Problem(404, "COMMODITY_NOT_FOUND", "Commodity not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(commodity.Version))
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        if (commodity.CreatedBy != actorId) return Results.Forbid();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var name = request.Name.Trim();
        if (name == commodity.Name)
        {
            await ledger.ProvisionAsync((NpgsqlConnection)db.Database.GetDbConnection(),
                (NpgsqlTransaction)transaction.GetDbTransaction(), actorId.Value, ct);
            await transaction.CommitAsync(ct);
            SetEtag(http, commodity.Version);
            return Results.Ok(ToResponse(commodity, actorId.Value, true));
        }
        var before = State(commodity);
        commodity.Name = name;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "COMMODITY_UPDATED", ResourceType = "COMMODITY",
            ResourceId = commodity.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = State(commodity)
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await ledger.ProvisionAsync((NpgsqlConnection)db.Database.GetDbConnection(),
                (NpgsqlTransaction)transaction.GetDbTransaction(), actorId.Value, ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        }
        SetEtag(http, commodity.Version);
        return Results.Ok(ToResponse(commodity, actorId.Value, true));
    }
    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, IdentityDbContext identityDb,
        CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the ETag from GET in the If-Match header.");
        var commodity = await db.Commodities.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (commodity is null) return Problem(404, "COMMODITY_NOT_FOUND", "Commodity not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(commodity.Version))
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        if (commodity.CreatedBy != actorId) return Results.Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (await IsCommodityInUseAsync(db, id, ct))
            return Problem(409, "COMMODITY_IN_USE", "Commodity is referenced by source, stock, compatibility, or policy history and cannot be removed.");
        var before = State(commodity);
        commodity.IsArchived = true;
        db.CatalogEvents.Add(new CatalogEvent { ActorId = actorId.Value, EventType = "COMMODITY_ARCHIVED",
            ResourceType = "COMMODITY", ResourceId = id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = State(commodity) });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "CATALOG_VERSION_STALE", "The catalog entry has changed. Read it again before editing.");
        }
        return Results.NoContent();
    }

    private static Task<bool> IsCommodityInUseAsync(CatalogDbContext db, Guid id, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT (
                EXISTS (
                    SELECT 1 FROM ledger.catalog_commodities m
                    WHERE m.catalog_commodity_id = {id} AND (
                        EXISTS (SELECT 1 FROM ledger.lots l WHERE l.commodity_id = m.ledger_commodity_id)
                        OR EXISTS (SELECT 1 FROM ledger.compatibilities c WHERE c.output_commodity_id = m.ledger_commodity_id
                            OR EXISTS (SELECT 1 FROM unnest(c.input_products) AS products(value)
                                WHERE translate(lower(split_part(products.value, ':', 1)), chr(45)||chr(123)||chr(125), '')
                                    = replace(m.ledger_commodity_id::text, '-', '')))
                        OR EXISTS (SELECT 1 FROM ledger.operations op
                            WHERE op.is_deleted = false AND (jsonb_typeof(op.content->'outputs') IS DISTINCT FROM 'array'
                               OR EXISTS (SELECT 1 FROM jsonb_array_elements(
                                   CASE WHEN jsonb_typeof(op.content->'outputs') = 'array'
                                        THEN op.content->'outputs' ELSE '[]'::jsonb END) o
                                   WHERE translate(lower(btrim(o->>'commodityId')), chr(45)||chr(123)||chr(125), '')
                                       = replace(m.ledger_commodity_id::text, '-', ''))))
                        OR EXISTS (SELECT 1 FROM ledger.corrections c
                            WHERE c.replacement_content IS NOT NULL AND (
                                jsonb_typeof(c.replacement_content->'outputs') IS DISTINCT FROM 'array'
                                OR EXISTS (SELECT 1 FROM jsonb_array_elements(
                                    CASE WHEN jsonb_typeof(c.replacement_content->'outputs') = 'array'
                                         THEN c.replacement_content->'outputs' ELSE '[]'::jsonb END) o
                                    WHERE translate(lower(btrim(o->>'commodityId')), chr(45)||chr(123)||chr(125), '')
                                        = replace(m.ledger_commodity_id::text, '-', ''))))
                        OR EXISTS (SELECT 1 FROM ledger.events ev
                            WHERE ev.kind = 'VARIANCE_CONFIRMATION'
                              AND (jsonb_typeof(ev.details->'reviewedContent'->'outputs') IS DISTINCT FROM 'array'
                                   OR EXISTS (SELECT 1 FROM jsonb_array_elements(
                                       CASE WHEN jsonb_typeof(ev.details->'reviewedContent'->'outputs') = 'array'
                                            THEN ev.details->'reviewedContent'->'outputs' ELSE '[]'::jsonb END) o
                                       WHERE translate(lower(btrim(o->>'commodityId')), chr(45)||chr(123)||chr(125), '')
                                           = replace(m.ledger_commodity_id::text, '-', ''))))
                    )
                )
                OR EXISTS (SELECT 1 FROM catalog.mrl_versions WHERE commodity_id = {id})
                OR EXISTS (SELECT 1 FROM capture.business_events
                    WHERE payload->'result'->>'CommodityId' = {id}::text)
                OR EXISTS (SELECT 1 FROM capture.offline_operations
                    WHERE payload::jsonb->'payload'->>'CommodityId' = {id}::text)
            ) AS "Value"
            """).SingleAsync(ct);

    private static async Task<bool> CanConfigureAsync(Guid actorId, IdentityDbContext db, CancellationToken ct)
    {
        var role = await db.Users.AsNoTracking().Where(x => x.Id == actorId && x.IsActive).Select(x => x.Role).SingleOrDefaultAsync(ct);
        return role is not null && Roles.All.TryGetValue(role, out var definition) && definition.Permissions.Contains("platform.configure");
    }


    private static async Task<Guid?> GetActiveActorAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (user?.Role is null || !Roles.All.TryGetValue(user.Role, out var role) ||
            user.Organization?.Kind != role.OrganizationKind) return null;
        return id;
    }

    private static bool TryPage(string? page, string? pageSize, out int currentPage, out int size)
    {
        currentPage = 1;
        size = 25;
        if (page is not null && (!int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out currentPage) || currentPage < 1)) return false;
        if (pageSize is not null && (!int.TryParse(pageSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 100)) return false;
        return currentPage - 1 <= int.MaxValue / size;
    }

    private static string State(Commodity commodity) => JsonSerializer.Serialize(new { commodity.Id, commodity.Code, commodity.Name, commodity.IsArchived });
    private static string Etag(uint version) => $"\"{version}\"";
    private static void SetEtag(HttpContext http, uint version) => http.Response.Headers.ETag = Etag(version);
    private static CommodityResponse ToResponse(Commodity x, Guid actorId, bool canConfigure) =>
        new(x.Id, x.Code, x.Name, x.CreatedBy, x.Version, canConfigure && x.CreatedBy == actorId, canConfigure && x.CreatedBy == actorId);
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
