using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Catalogs;

public sealed record CreateMrlVersionRequest(
    Guid MarketId, Guid CommodityId, DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo, string? SourceRef);

public sealed record MrlVersionResponse(
    Guid Id, Guid MarketId, Guid CommodityId, int VersionNo, string Status,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    Guid? PublishedBy, DateTimeOffset? PublishedAt, string SourceRef);
public sealed record MrlVersionListResponse(
    IReadOnlyList<MrlVersionResponse> Items, int Page, int PageSize, bool HasMore);
public sealed record MrlLimitResponse(
    Guid Id, Guid MrlVersionId, Guid SubstanceId, decimal LimitValue, string Unit);
public sealed record MrlLimitListResponse(
    IReadOnlyList<MrlLimitResponse> Items, int Page, int PageSize, bool HasMore);
public sealed record UpsertMrlLimitRequest(decimal? LimitValue, string? Unit);

public static class MrlVersionEndpoints
{
    public static IEndpointRouteBuilder MapMrlVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/mrl-versions").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("MRL versions");
        group.MapPost("", CreateAsync).RequireAuthorization("platform.configure");
        group.MapPost("/import", MrlImportEndpoints.ImportAsync).RequireAuthorization("platform.configure");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization("platform.configure");
        group.MapGet("/{id:guid}/limits", ListLimitsAsync);
        group.MapPut("/{id:guid}/limits/{substanceId:guid}", UpsertLimitAsync)
            .RequireAuthorization("platform.configure");
        group.MapDelete("/{id:guid}/limits/{substanceId:guid}", DeleteLimitAsync)
            .RequireAuthorization("platform.configure");
        group.MapPost("/{id:guid}/publish", PublishAsync)
            .RequireAuthorization("platform.configure");
        group.MapGet("/resolve", ResolveAsync);
        return endpoints;
    }

    private static async Task<IResult> PublishAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the MRL version ETag in the If-Match header.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var versionLock = $"mrl-limit-version:{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({versionLock}, 0))", ct);
        var version = await db.MrlVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null) return Problem(404, "MRL_VERSION_NOT_FOUND", "MRL version not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(version.Version))
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before publishing.");
        if (version.Status != "DRAFT")
            return Problem(409, "MRL_VERSION_PUBLISHED", "The MRL version is already published.");

        var pairLock = $"mrl-version:{version.MarketId:N}:{version.CommodityId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({pairLock}, 0))", ct);
        var now = NormalizeTimestamp(clock.GetUtcNow());
        if (version.EffectiveFrom < now)
            return Problem(409, "MRL_BACKDATED_PUBLICATION", "Effective start cannot be before publication time.");
        if (version.EffectiveTo is not null && version.EffectiveTo <= version.EffectiveFrom)
            return Problem(409, "MRL_INVALID_INTERVAL", "Effective end must be after effective start.");
        if (string.IsNullOrWhiteSpace(version.SourceRef))
            return Problem(409, "MRL_SOURCE_REQUIRED", "A source reference is required before publication.");
        var limits = await db.MrlLimits.AsNoTracking()
            .Where(x => x.MrlVersionId == id).ToListAsync(ct);
        if (limits.Count == 0)
            return Problem(409, "MRL_LIMITS_REQUIRED", "Add at least one substance limit before publication.");
        if (limits.Any(x => x.LimitValue < 0 || x.Unit != "mg/kg") ||
            !await db.MeasurementUnits.AsNoTracking().AnyAsync(
                x => x.Code == "MG/KG" && x.QuantityKind == "RESIDUE_CONCENTRATION", ct))
            return Problem(409, "MRL_LIMITS_INVALID", "MRL limits must use a configured mg/kg residue unit.");

        var published = await db.MrlVersions
            .Where(x => x.MarketId == version.MarketId && x.CommodityId == version.CommodityId &&
                x.Status == "PUBLISHED")
            .OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        if (published.Any(x => x.VersionNo >= version.VersionNo || x.EffectiveFrom >= version.EffectiveFrom))
            return Problem(409, "MRL_PUBLICATION_ORDER", "A successor must have a later version and effective start.");
        for (var i = 0; i < published.Count; i++)
            for (var j = i + 1; j < published.Count; j++)
                if (IntervalsOverlap(published[i], published[j]))
                    return Problem(409, "MRL_INTERVAL_CONFLICT", "Published MRL intervals already overlap.");
        var openEnded = published.Where(x => x.EffectiveTo is null).ToList();
        if (openEnded.Count > 1)
            return Problem(409, "MRL_INTERVAL_CONFLICT", "More than one open-ended published version exists.");
        var predecessor = openEnded.SingleOrDefault();
        if (predecessor is not null && predecessor.EffectiveFrom >= version.EffectiveFrom)
            return Problem(409, "MRL_INTERVAL_CONFLICT", "A successor must start after its predecessor.");
        if (published.Where(x => x != predecessor).Any(x => IntervalsOverlap(x, version)))
            return Problem(409, "MRL_INTERVAL_CONFLICT", "The new MRL interval overlaps a published version.");

        var correlationId = Guid.NewGuid();
        if (predecessor is not null)
        {
            var previousState = JsonSerializer.Serialize(ToResponse(predecessor));
            predecessor.EffectiveTo = version.EffectiveFrom;
            db.CatalogEvents.Add(new CatalogEvent
            {
                ActorId = actor.Value.Id, EventType = "MRL_VERSION_CLOSED",
                ResourceType = "MRL_VERSION", ResourceId = predecessor.Id,
                OccurredAt = now, CorrelationId = correlationId,
                BeforeState = previousState,
                AfterState = JsonSerializer.Serialize(new
                {
                    version = ToResponse(predecessor), successorId = version.Id
                })
            });
        }
        var before = JsonSerializer.Serialize(ToResponse(version));
        version.Status = "PUBLISHED";
        version.PublishedBy = actor.Value.Id;
        version.PublishedAt = now;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actor.Value.Id, EventType = "MRL_VERSION_PUBLISHED",
            ResourceType = "MRL_VERSION", ResourceId = version.Id,
            OccurredAt = now, CorrelationId = correlationId,
            BeforeState = before, AfterState = JsonSerializer.Serialize(ToResponse(version))
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "MRL_VERSION_STALE", "A related MRL version changed. Read the versions again.");
        }
        http.Response.Headers.ETag = Etag(version.Version);
        return Results.Ok(ToResponse(version));
    }

    private static async Task<IResult> ResolveAsync(HttpContext http, IdentityDbContext identityDb,
        CatalogDbContext db, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        if (!actor.Value.Role.Permissions.Contains("platform.configure") &&
            !actor.Value.Role.Permissions.Contains("mrl.read")) return Results.Forbid();

        var query = http.Request.Query;
        var errors = new Dictionary<string, string[]>();
        var marketId = ParseRequiredGuid("marketId");
        var commodityId = ParseRequiredGuid("commodityId");
        DateTimeOffset? decisionAt = null;
        var rawDate = SingleValue("decisionAt");
        if (rawDate is null || !HasExplicitOffset(rawDate) ||
            !DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedDate))
            errors["decisionAt"] = ["Use an ISO 8601 date-time with Z or an explicit UTC offset."];
        else decisionAt = parsedDate.ToUniversalTime();
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var candidates = await db.MrlVersions.AsNoTracking()
            .Where(x => x.MarketId == marketId && x.CommodityId == commodityId &&
                x.Status == "PUBLISHED" && x.PublishedAt <= decisionAt &&
                x.EffectiveFrom <= decisionAt &&
                (x.EffectiveTo == null || decisionAt < x.EffectiveTo))
            .OrderByDescending(x => x.VersionNo).Take(2).ToListAsync(ct);
        if (candidates.Count == 0)
            return Problem(404, "MRL_VERSION_NOT_AVAILABLE", "No published MRL version applies at that decision time.");
        if (candidates.Count > 1)
            return Problem(409, "MRL_VERSION_AMBIGUOUS", "More than one published MRL version applies.");
        http.Response.Headers.ETag = Etag(candidates[0].Version);
        return Results.Ok(ToResponse(candidates[0]));

        string? SingleValue(string key) => query.TryGetValue(key, out var values) && values.Count == 1
            ? values[0] : null;
        Guid? ParseRequiredGuid(string key)
        {
            var value = SingleValue(key);
            if (value is not null && Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
                return parsed;
            errors[key] = ["A non-empty GUID is required."];
            return null;
        }
    }

    private static bool IntervalsOverlap(MrlVersion a, MrlVersion b) =>
        (b.EffectiveTo is null || a.EffectiveFrom < b.EffectiveTo) &&
        (a.EffectiveTo is null || b.EffectiveFrom < a.EffectiveTo);

    private static bool HasExplicitOffset(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith('Z') || trimmed.EndsWith('z')) return true;
        return trimmed.Length >= 6 && (trimmed[^6] is '+' or '-') &&
            char.IsDigit(trimmed[^5]) && char.IsDigit(trimmed[^4]) &&
            trimmed[^3] == ':' && char.IsDigit(trimmed[^2]) && char.IsDigit(trimmed[^1]);
    }

    private static async Task<IResult> DeleteLimitAsync(Guid id, Guid substanceId, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the MRL version ETag in the If-Match header.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"mrl-limit-version:{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var version = await db.MrlVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null) return Problem(404, "MRL_VERSION_NOT_FOUND", "MRL version not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(version.Version))
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before editing.");
        if (version.Status != "DRAFT")
            return Problem(409, "MRL_VERSION_PUBLISHED", "A published MRL version cannot be edited.");
        var limit = await db.MrlLimits.SingleOrDefaultAsync(
            x => x.MrlVersionId == id && x.SubstanceId == substanceId, ct);
        if (limit is null) return Problem(404, "MRL_LIMIT_NOT_FOUND", "MRL limit not found.");

        db.MrlLimits.Remove(limit);
        // A limit edit invalidates the ETag of the containing MRL version.
        db.Entry(version).Property(x => x.SourceRef).IsModified = true;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actor.Value.Id, EventType = "MRL_LIMIT_DELETED", ResourceType = "MRL_LIMIT",
            ResourceId = limit.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = JsonSerializer.Serialize(ToLimitResponse(limit))
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before editing.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "MRL_LIMIT_IN_USE", "The MRL limit is referenced and cannot be deleted.");
        }
        http.Response.Headers.ETag = Etag(version.Version);
        return Results.NoContent();
    }

    private static async Task<IResult> UpsertLimitAsync(Guid id, Guid substanceId,
        UpsertMrlLimitRequest request, HttpContext http, IdentityDbContext identityDb,
        CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the MRL version ETag in the If-Match header.");

        var errors = new Dictionary<string, string[]>();
        if (substanceId == Guid.Empty) errors["substanceId"] = ["Substance ID is required."];
        if (request.LimitValue is null || request.LimitValue < 0 ||
            request.LimitValue > 999999999999.999999999999m ||
            decimal.Round(request.LimitValue.Value, 12) != request.LimitValue.Value)
            errors["limitValue"] = ["Use a nonnegative value with at most 12 integer and 12 decimal digits."];
        if (!string.Equals(request.Unit?.Trim(), "mg/kg", StringComparison.OrdinalIgnoreCase))
            errors["unit"] = ["MRL limits must use mg/kg."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var lockKey = $"mrl-limit-version:{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var version = await db.MrlVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null) return Problem(404, "MRL_VERSION_NOT_FOUND", "MRL version not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(version.Version))
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before editing.");
        if (version.Status != "DRAFT")
            return Problem(409, "MRL_VERSION_PUBLISHED", "A published MRL version cannot be edited.");
        if (!await db.ActiveSubstances.AsNoTracking().AnyAsync(x => x.Id == substanceId, ct))
            return Problem(404, "SUBSTANCE_NOT_FOUND", "Active substance not found.");
        if (!await db.MeasurementUnits.AsNoTracking().AnyAsync(
            x => x.Code == "MG/KG" && x.QuantityKind == "RESIDUE_CONCENTRATION", ct))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["unit"] = ["Configure MG/KG as a residue-concentration measurement unit first."] });

        var limit = await db.MrlLimits.SingleOrDefaultAsync(
            x => x.MrlVersionId == id && x.SubstanceId == substanceId, ct);
        var created = limit is null;
        if (limit is not null && limit.LimitValue == request.LimitValue && limit.Unit == "mg/kg")
        {
            http.Response.Headers.ETag = Etag(version.Version);
            return Results.Ok(ToLimitResponse(limit));
        }
        var before = limit is null ? null : JsonSerializer.Serialize(ToLimitResponse(limit));
        if (limit is null)
        {
            limit = new MrlLimit
            {
                Id = Guid.NewGuid(), MrlVersionId = id, SubstanceId = substanceId,
                LimitValue = request.LimitValue!.Value, Unit = "mg/kg"
            };
            db.MrlLimits.Add(limit);
        }
        else
        {
            limit.LimitValue = request.LimitValue!.Value;
            limit.Unit = "mg/kg";
        }
        // Force an update of the parent row so its xmin/ETag changes with every limit edit.
        db.Entry(version).Property(x => x.SourceRef).IsModified = true;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actor.Value.Id,
            EventType = created ? "MRL_LIMIT_CREATED" : "MRL_LIMIT_UPDATED",
            ResourceType = "MRL_LIMIT", ResourceId = limit.Id,
            OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = JsonSerializer.Serialize(ToLimitResponse(limit))
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before editing.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "MRL_LIMIT_CONFLICT", "The version or substance changed. Read them again.");
        }
        http.Response.Headers.ETag = Etag(version.Version);
        var response = ToLimitResponse(limit);
        return created ? Results.Created($"/api/mrl-versions/{id}/limits?substanceId={substanceId}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> ListLimitsAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        var canConfigure = actor.Value.Role.Permissions.Contains("platform.configure");
        if (!canConfigure && !actor.Value.Role.Permissions.Contains("mrl.read")) return Results.Forbid();

        var query = http.Request.Query;
        var errors = new Dictionary<string, string[]>();
        Guid? substanceId = null;
        if (query.ContainsKey("substanceId"))
        {
            var raw = SingleValue("substanceId");
            if (raw is not null && Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty)
                substanceId = parsed;
            else errors["substanceId"] = ["A non-empty GUID is required."];
        }
        var page = ParsePage("page", 1, 1, int.MaxValue);
        var pageSize = ParsePage("pageSize", 20, 1, 100);
        if (page is not null && pageSize is not null && page.Value - 1 > int.MaxValue / pageSize.Value)
            errors["page"] = ["Page offset is too large."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var version = await db.MrlVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null || (version.Status != "PUBLISHED" && !canConfigure))
            return Problem(404, "MRL_VERSION_NOT_FOUND", "MRL version not found.");

        var limits = db.MrlLimits.AsNoTracking().Where(x => x.MrlVersionId == id);
        if (substanceId is not null) limits = limits.Where(x => x.SubstanceId == substanceId);
        var rows = await limits.OrderBy(x => x.SubstanceId).ThenBy(x => x.Id)
            .Skip((page!.Value - 1) * pageSize!.Value).Take(pageSize.Value + 1).ToListAsync(ct);
        http.Response.Headers.ETag = Etag(version.Version);
        return Results.Ok(new MrlLimitListResponse(rows.Take(pageSize.Value)
            .Select(x => new MrlLimitResponse(x.Id, x.MrlVersionId, x.SubstanceId, x.LimitValue, x.Unit)).ToList(),
            page.Value, pageSize.Value, rows.Count > pageSize.Value));

        string? SingleValue(string key) => query.TryGetValue(key, out var values) && values.Count == 1
            ? values[0] : null;

        int? ParsePage(string key, int fallback, int minimum, int maximum)
        {
            if (!query.ContainsKey(key)) return fallback;
            var value = SingleValue(key);
            if (value is not null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed >= minimum && parsed <= maximum) return parsed;
            errors[key] = [$"Value must be between {minimum} and {maximum}."];
            return null;
        }
    }

    private static async Task<IResult> UpdateAsync(Guid id, JsonElement patch, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        if (!http.Request.Headers.ContainsKey("If-Match"))
            return Problem(428, "IF_MATCH_REQUIRED", "Send the ETag from GET in the If-Match header.");

        var version = await db.MrlVersions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null) return Problem(404, "MRL_VERSION_NOT_FOUND", "MRL version not found.");
        if (http.Request.Headers.IfMatch.ToString() != Etag(version.Version))
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before editing.");
        if (version.Status != "DRAFT")
            return Problem(409, "MRL_VERSION_PUBLISHED", "A published MRL version cannot be edited.");

        var errors = new Dictionary<string, string[]>();
        DateTimeOffset? effectiveFrom = null;
        DateTimeOffset? effectiveTo = version.EffectiveTo;
        string? sourceRef = null;
        var hasEffectiveFrom = false;
        var hasEffectiveTo = false;
        var hasSourceRef = false;
        if (patch.ValueKind != JsonValueKind.Object)
            errors["body"] = ["A JSON object is required."];
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in patch.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    errors[property.Name] = ["Field may occur only once."];
                    continue;
                }
                switch (property.Name.ToLowerInvariant())
                {
                    case "effectivefrom":
                        hasEffectiveFrom = true;
                        if (property.Value.ValueKind == JsonValueKind.String &&
                            property.Value.TryGetDateTimeOffset(out var from) && from != default)
                            effectiveFrom = from.ToUniversalTime();
                        else errors["effectiveFrom"] = ["A valid effective start is required."];
                        break;
                    case "effectiveto":
                        hasEffectiveTo = true;
                        if (property.Value.ValueKind == JsonValueKind.Null) effectiveTo = null;
                        else if (property.Value.ValueKind == JsonValueKind.String &&
                            property.Value.TryGetDateTimeOffset(out var to)) effectiveTo = to.ToUniversalTime();
                        else errors["effectiveTo"] = ["A valid effective end or null is required."];
                        break;
                    case "sourceref":
                        hasSourceRef = true;
                        if (property.Value.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(property.Value.GetString()))
                            sourceRef = property.Value.GetString()!.Trim();
                        else errors["sourceRef"] = ["Source reference is required."];
                        break;
                    default:
                        errors[property.Name] = ["This field cannot be edited."];
                        break;
                }
            }
        }
        if (!hasEffectiveFrom && !hasEffectiveTo && !hasSourceRef && errors.Count == 0)
            errors["body"] = ["At least one editable field is required."];
        var newEffectiveFrom = effectiveFrom ?? version.EffectiveFrom;
        if (effectiveTo is not null &&
            NormalizeTimestamp(effectiveTo.Value) <= NormalizeTimestamp(newEffectiveFrom))
            errors["effectiveTo"] = ["Effective end must be after effective start."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (newEffectiveFrom == version.EffectiveFrom && effectiveTo == version.EffectiveTo &&
            (sourceRef is null || sourceRef == version.SourceRef))
        {
            http.Response.Headers.ETag = Etag(version.Version);
            return Results.Ok(ToResponse(version));
        }
        var before = JsonSerializer.Serialize(ToResponse(version));
        version.EffectiveFrom = NormalizeTimestamp(newEffectiveFrom);
        version.EffectiveTo = effectiveTo is null ? null : NormalizeTimestamp(effectiveTo.Value);
        if (sourceRef is not null) version.SourceRef = sourceRef;
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actor.Value.Id, EventType = "MRL_VERSION_UPDATED", ResourceType = "MRL_VERSION",
            ResourceId = version.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            BeforeState = before, AfterState = JsonSerializer.Serialize(ToResponse(version))
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(412, "MRL_VERSION_STALE", "The MRL version has changed. Read it again before editing.");
        }
        http.Response.Headers.ETag = Etag(version.Version);
        return Results.Ok(ToResponse(version));
    }

    private static async Task<IResult> ListAsync(HttpContext http, IdentityDbContext identityDb,
        CatalogDbContext db, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        var canConfigure = actor.Value.Role.Permissions.Contains("platform.configure");
        if (!canConfigure && !actor.Value.Role.Permissions.Contains("mrl.read")) return Results.Forbid();

        var queryParams = http.Request.Query;
        var errors = new Dictionary<string, string[]>();
        Guid? marketId = ParseOptionalGuid("marketId");
        Guid? commodityId = ParseOptionalGuid("commodityId");
        var status = SingleValue("status")?.Trim().ToUpperInvariant();
        if (queryParams.ContainsKey("status") && status is not ("DRAFT" or "PUBLISHED"))
            errors["status"] = ["Status must be DRAFT or PUBLISHED."];
        var page = ParsePage("page", 1, 1, int.MaxValue);
        var pageSize = ParsePage("pageSize", 20, 1, 100);
        if (page is not null && pageSize is not null && page.Value - 1 > int.MaxValue / pageSize.Value)
            errors["page"] = ["Page offset is too large."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var versions = db.MrlVersions.AsNoTracking();
        if (!canConfigure) versions = versions.Where(x => x.Status == "PUBLISHED");
        if (marketId is not null) versions = versions.Where(x => x.MarketId == marketId);
        if (commodityId is not null) versions = versions.Where(x => x.CommodityId == commodityId);
        if (status is not null) versions = versions.Where(x => x.Status == status);
        var rows = await versions.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.VersionNo).ThenBy(x => x.Id)
            .Skip((page!.Value - 1) * pageSize!.Value).Take(pageSize.Value + 1).ToListAsync(ct);
        return Results.Ok(new MrlVersionListResponse(rows.Take(pageSize.Value).Select(ToResponse).ToList(),
            page.Value, pageSize.Value, rows.Count > pageSize.Value));

        string? SingleValue(string key) => queryParams.TryGetValue(key, out var values) && values.Count == 1
            ? values[0] : null;

        Guid? ParseOptionalGuid(string key)
        {
            if (!queryParams.ContainsKey(key)) return null;
            var value = SingleValue(key);
            if (value is not null && Guid.TryParse(value, out var id) && id != Guid.Empty) return id;
            errors[key] = ["A non-empty GUID is required."];
            return null;
        }

        int? ParsePage(string key, int fallback, int minimum, int maximum)
        {
            if (!queryParams.ContainsKey(key)) return fallback;
            var value = SingleValue(key);
            if (value is not null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed >= minimum && parsed <= maximum) return parsed;
            errors[key] = [$"Value must be between {minimum} and {maximum}."];
            return null;
        }
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();
        var canConfigure = actor.Value.Role.Permissions.Contains("platform.configure");
        if (!canConfigure && !actor.Value.Role.Permissions.Contains("mrl.read")) return Results.Forbid();

        var version = await db.MrlVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (version is null || (version.Status != "PUBLISHED" && !canConfigure))
            return Problem(404, "MRL_VERSION_NOT_FOUND", "MRL version not found.");
        http.Response.Headers.ETag = $"\"{version.Version}\"";
        return Results.Ok(ToResponse(version));
    }

    private static async Task<IResult> CreateAsync(CreateMrlVersionRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actor = await GetActiveActorAsync(http.User, identityDb, ct);
        if (actor is null) return Results.Forbid();

        var errors = new Dictionary<string, string[]>();
        if (request.MarketId == Guid.Empty) errors["marketId"] = ["Market ID is required."];
        if (request.CommodityId == Guid.Empty) errors["commodityId"] = ["Commodity ID is required."];
        if (request.EffectiveFrom is null || request.EffectiveFrom == default)
            errors["effectiveFrom"] = ["Effective start is required."];
        var sourceRef = request.SourceRef?.Trim();
        if (string.IsNullOrWhiteSpace(sourceRef)) errors["sourceRef"] = ["Source reference is required."];
        if (request.EffectiveFrom is not null && request.EffectiveTo is not null &&
            NormalizeTimestamp(request.EffectiveTo.Value) <= NormalizeTimestamp(request.EffectiveFrom.Value))
            errors["effectiveTo"] = ["Effective end must be after effective start."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await db.Markets.AsNoTracking().AnyAsync(x => x.Id == request.MarketId, ct))
            return Problem(404, "MARKET_NOT_FOUND", "Market not found.");
        if (!await db.Commodities.AsNoTracking().AnyAsync(x => x.Id == request.CommodityId, ct))
            return Problem(404, "COMMODITY_NOT_FOUND", "Commodity not found.");
        // Serialize version number assignment for this market/commodity pair.
        var lockKey = $"mrl-version:{request.MarketId:N}:{request.CommodityId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var previousNo = await db.MrlVersions
            .Where(x => x.MarketId == request.MarketId && x.CommodityId == request.CommodityId)
            .Select(x => (int?)x.VersionNo).MaxAsync(ct) ?? 0;
        if (previousNo == int.MaxValue)
            return Problem(409, "MRL_VERSION_NUMBER_EXHAUSTED", "No further version number is available.");

        var version = new MrlVersion
        {
            Id = Guid.NewGuid(), MarketId = request.MarketId, CommodityId = request.CommodityId,
            VersionNo = previousNo + 1, Status = "DRAFT",
            EffectiveFrom = NormalizeTimestamp(request.EffectiveFrom!.Value),
            EffectiveTo = request.EffectiveTo is null ? null : NormalizeTimestamp(request.EffectiveTo.Value),
            SourceRef = sourceRef!
        };
        db.MrlVersions.Add(version);
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actor.Value.Id, EventType = "MRL_VERSION_CREATED", ResourceType = "MRL_VERSION",
            ResourceId = version.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            AfterState = JsonSerializer.Serialize(ToResponse(version))
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "MRL_VERSION_CONFLICT", "Market, commodity, or version number changed. Retry the request.");
        }
        http.Response.Headers.ETag = $"\"{version.Version}\"";
        return Results.Created($"/api/mrl-versions/{version.Id}", ToResponse(version));
    }

    private static async Task<(Guid Id, Roles.Definition Role)?> GetActiveActorAsync(
        ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (user?.Role is null || !Roles.All.TryGetValue(user.Role, out var role) ||
            user.Organization?.Kind != role.OrganizationKind) return null;
        return (id, role);
    }

    private static MrlVersionResponse ToResponse(MrlVersion x) => new(
        x.Id, x.MarketId, x.CommodityId, x.VersionNo, x.Status,
        x.EffectiveFrom, x.EffectiveTo, x.PublishedBy, x.PublishedAt, x.SourceRef);
    private static MrlLimitResponse ToLimitResponse(MrlLimit x) => new(
        x.Id, x.MrlVersionId, x.SubstanceId, x.LimitValue, x.Unit);
    private static string Etag(uint version) => $"\"{version}\"";
    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % 10));
    }
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
