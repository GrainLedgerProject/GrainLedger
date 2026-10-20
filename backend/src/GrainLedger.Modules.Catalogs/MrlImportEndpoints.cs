using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrainLedger.Modules.Catalogs;

public sealed record ImportMrlLimitRequest(string? SubstanceCode, string? LimitValue, string? Unit);
public sealed record ImportMrlVersionRequest(
    Guid MarketId, Guid CommodityId, DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo, string? SourceRef,
    IReadOnlyList<ImportMrlLimitRequest?>? Limits);

public static class MrlImportEndpoints
{
    public static async Task<IResult> ImportAsync(ImportMrlVersionRequest request, HttpContext http,
        IdentityDbContext identityDb, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var actorId = await GetActiveAdministratorAsync(http.User, identityDb, ct);
        if (actorId is null) return Results.Forbid();

        var errors = new Dictionary<string, string[]>();
        if (request.MarketId == Guid.Empty) errors["marketId"] = ["Market is required."];
        if (request.CommodityId == Guid.Empty) errors["commodityId"] = ["Commodity is required."];
        if (request.EffectiveFrom is null || request.EffectiveFrom == default)
            errors["effectiveFrom"] = ["Effective start is required."];
        if (request.EffectiveFrom is not null && request.EffectiveTo is not null &&
            NormalizeTimestamp(request.EffectiveTo.Value) <= NormalizeTimestamp(request.EffectiveFrom.Value))
            errors["effectiveTo"] = ["Effective end must be after effective start."];
        var sourceRef = request.SourceRef?.Trim();
        if (string.IsNullOrWhiteSpace(sourceRef)) errors["sourceRef"] = ["A verified regulatory source is required."];
        if (request.Limits is null || request.Limits.Count is < 1 or > 500)
            errors["limits"] = ["Import between 1 and 500 numeric substance limits in one draft."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var rows = new List<(string Code, decimal Value)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < request.Limits!.Count; i++)
        {
            var row = request.Limits[i];
            if (row is null)
            {
                errors[$"limits[{i}]"] = ["A substance limit row is required."];
                continue;
            }
            var code = row.SubstanceCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 60)
                errors[$"limits[{i}].substanceCode"] = ["A substance catalog code is required (maximum 60 characters)."];
            else if (!seen.Add(code))
                errors[$"limits[{i}].substanceCode"] = ["This substance occurs more than once in the import."];
            if (!string.Equals(row.Unit?.Trim(), "mg/kg", StringComparison.OrdinalIgnoreCase))
                errors[$"limits[{i}].unit"] = ["Only mg/kg limits can be imported."];
            if (!decimal.TryParse(row.LimitValue, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value) || value < 0 ||
                value > 999999999999.999999999999m || decimal.Round(value, 12) != value)
                errors[$"limits[{i}].limitValue"] = ["Use a nonnegative number with at most 12 integer and 12 decimal digits."];
            if (code is not null && !errors.ContainsKey($"limits[{i}].limitValue")) rows.Add((code, value));
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await db.Markets.AsNoTracking().AnyAsync(x => x.Id == request.MarketId, ct))
            return Problem(404, "MARKET_NOT_FOUND", "Market not found.");
        if (!await db.Commodities.AsNoTracking().AnyAsync(x => x.Id == request.CommodityId && !x.IsArchived, ct))
            return Problem(404, "COMMODITY_NOT_FOUND", "Active commodity not found.");
        if (!await db.MeasurementUnits.AsNoTracking().AnyAsync(
            x => x.Code == "MG/KG" && x.QuantityKind == "RESIDUE_CONCENTRATION", ct))
            return Problem(409, "MRL_UNIT_MISSING", "Configure MG/KG as a residue-concentration unit before importing.");

        var codes = rows.Select(x => x.Code).ToArray();
        var substances = await db.ActiveSubstances.AsNoTracking().Where(x => codes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, ct);
        var missing = codes.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => !substances.ContainsKey(code)).ToArray();
        if (missing.Length > 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["limits"] = [$"Create these active substances in Catalogs first: {string.Join(", ", missing)}."] });

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
        var limits = rows.Select(row => new MrlLimit
        {
            Id = Guid.NewGuid(), MrlVersionId = version.Id, SubstanceId = substances[row.Code].Id,
            LimitValue = row.Value, Unit = "mg/kg"
        }).ToArray();
        db.MrlLimits.AddRange(limits);
        db.CatalogEvents.Add(new CatalogEvent
        {
            ActorId = actorId.Value, EventType = "MRL_VERSION_IMPORTED", ResourceType = "MRL_VERSION",
            ResourceId = version.Id, OccurredAt = clock.GetUtcNow(), CorrelationId = Guid.NewGuid(),
            AfterState = JsonSerializer.Serialize(new
            {
                Version = ToResponse(version), Limits = limits.Select(x => new
                { x.SubstanceId, x.LimitValue, x.Unit })
            })
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            return Problem(409, "MRL_IMPORT_CONFLICT", "Catalogs or MRL versions changed. Review the import and retry.");
        }
        http.Response.Headers.ETag = $"\"{version.Version}\"";
        return Results.Created($"/api/mrl-versions/{version.Id}", ToResponse(version));
    }

    private static async Task<Guid?> GetActiveAdministratorAsync(ClaimsPrincipal principal,
        IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (user?.Role is null || !Roles.All.TryGetValue(user.Role, out var role) ||
            user.Organization?.Kind != role.OrganizationKind ||
            !role.Permissions.Contains("platform.configure")) return null;
        return id;
    }

    private static MrlVersionResponse ToResponse(MrlVersion x) => new(
        x.Id, x.MarketId, x.CommodityId, x.VersionNo, x.Status,
        x.EffectiveFrom, x.EffectiveTo, x.PublishedBy, x.PublishedAt, x.SourceRef);
    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % 10));
    }
    private static IResult Problem(int status, string code, string title) => Results.Problem(
        statusCode: status, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
