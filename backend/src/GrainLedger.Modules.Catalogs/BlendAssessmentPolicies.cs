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
using NpgsqlTypes;

namespace GrainLedger.Modules.Catalogs;

public sealed record PublishedQualityPropagationRule(Guid AttributeId, string Unit, string Calculation);
public sealed record PublishedBlendAssessmentPolicy(Guid Id, Guid CompanyId, Guid MarketId,
    Guid CommodityId, int VersionNo, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    Guid[] RequiredSubstanceIds, string MrlMarginFraction, string ResidueBasis,
    PublishedQualityPropagationRule[] QualityRules, string EvidenceReference,
    string ReviewedBy, string ReviewAuthority, DateTimeOffset ReviewedAt,
    Guid PublishedBy, DateTimeOffset PublishedAt);
public sealed record PublishBlendAssessmentPolicyRequest(Guid Id, Guid MarketId, Guid CommodityId,
    int VersionNo, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    Guid[] RequiredSubstanceIds, string MrlMarginFraction, string ResidueBasis,
    PublishedQualityPropagationRule[] QualityRules, string EvidenceReference,
    string ReviewedBy, string ReviewAuthority, DateTimeOffset ReviewedAt);
public sealed class BlendAssessmentPolicyUnavailableException : Exception;

public sealed class BlendAssessmentPolicyRead(CatalogDbContext db)
{
    public async Task<PublishedBlendAssessmentPolicy?> ResolveAsync(Guid companyId,
        Guid marketId, Guid commodityId, DateTimeOffset? at, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT id,company_id,market_id,commodity_id,version_no,effective_from,effective_to,
                       required_substance_ids,margin_fraction,residue_basis,quality_rules::text,
                       evidence_reference,reviewed_by,review_authority,reviewed_at,published_by,published_at
                FROM catalog.blend_assessment_policies
                WHERE company_id=$1 AND market_id=$2 AND commodity_id=$3
                  AND published_at <= COALESCE($4, statement_timestamp())
                  AND effective_from <= COALESCE($4, statement_timestamp())
                  AND (effective_to IS NULL OR COALESCE($4, statement_timestamp()) < effective_to)
                ORDER BY version_no DESC,id LIMIT 1
                """, connection);
            command.Parameters.AddWithValue(companyId);
            command.Parameters.AddWithValue(marketId);
            command.Parameters.AddWithValue(commodityId);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz,
                Value = at?.ToUniversalTime() ?? (object)DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new PublishedBlendAssessmentPolicy(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetGuid(2), reader.GetGuid(3), reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null :
                    reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<Guid[]>(7),
                reader.GetDecimal(8).ToString("0.############", CultureInfo.InvariantCulture), reader.GetString(9),
                JsonSerializer.Deserialize<PublishedQualityPropagationRule[]>(reader.GetString(10),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [],
                reader.GetString(11), reader.GetString(12), reader.GetString(13),
                reader.GetFieldValue<DateTimeOffset>(14), reader.GetGuid(15),
                reader.GetFieldValue<DateTimeOffset>(16));
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or
            PostgresErrorCodes.InsufficientPrivilege)
        { throw new BlendAssessmentPolicyUnavailableException(); }
        finally { if (close) await connection.CloseAsync(); }
    }
}

public static class BlendAssessmentPolicyEndpoints
{
    public static IEndpointRouteBuilder MapBlendAssessmentPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/blend-assessment-policies").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Blend assessment policy");
        group.MapGet("/applicable", ApplicableAsync).RequireAuthorization(Permissions.ExportOrdersRead);
        group.MapPost("", PublishAsync).RequireAuthorization(Permissions.BlendAssessmentPolicyManage);
        return endpoints;
    }

    private static async Task<IResult> ApplicableAsync(Guid marketId, Guid commodityId,
        DateTimeOffset? at, HttpContext http, IdentityDbContext identity,
        BlendAssessmentPolicyRead reader, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, ct);
        if (scope is null) return Results.Forbid();
        if (marketId == Guid.Empty || commodityId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Choose a market and commodity."] });
        PublishedBlendAssessmentPolicy? policy;
        try { policy = await reader.ResolveAsync(scope.Value.Company, marketId, commodityId,
            at, ct); }
        catch (BlendAssessmentPolicyUnavailableException)
        { return Results.Problem(statusCode: 503, title: "Blend assessment policy schema or runtime grant is not ready."); }
        return policy is null ? Results.NotFound() : Results.Ok(policy);
    }

    private static async Task<IResult> PublishAsync(PublishBlendAssessmentPolicyRequest request,
        HttpContext http, IdentityDbContext identity, ICurrentAccess access,
        CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await ScopeAsync(http, identity, ct);
        if (scope is null || scope.Value.Role != Roles.CustomerManager ||
            !await access.HasPermissionAsync(Permissions.BlendAssessmentPolicyManage, ct))
            return Results.Forbid();
        var errors = new Dictionary<string, string[]>();
        var now = clock.GetUtcNow();
        if (request.Id == Guid.Empty || request.MarketId == Guid.Empty ||
            request.CommodityId == Guid.Empty || request.VersionNo < 1)
            errors["identity"] = ["Provide a stable policy ID, market, commodity and positive version."];
        if (request.EffectiveFrom == default ||
            request.EffectiveTo is { } end && end <= request.EffectiveFrom)
            errors["effectivePeriod"] = ["Provide a current start and a later optional end."];
        var requiredSubstances = request.RequiredSubstanceIds ?? [];
        if (requiredSubstances.Length is < 1 or > 200 ||
            requiredSubstances.Contains(Guid.Empty) ||
            requiredSubstances.Distinct().Count() != requiredSubstances.Length)
            errors["requiredSubstanceIds"] = ["Choose 1–200 distinct active substances."];
        if (!decimal.TryParse(request.MrlMarginFraction, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var margin) || margin < 0 || margin >= 1 ||
            decimal.Round(margin, 12) != margin)
            errors["mrlMarginFraction"] = ["Use an explicit fraction from 0 (inclusive) to 1 (exclusive), with at most 12 decimal places."];
        if (string.IsNullOrWhiteSpace(request.ResidueBasis) || request.ResidueBasis.Length > 100)
            errors["residueBasis"] = ["State the comparable residue material basis."];
        var qualityRules = request.QualityRules ?? [];
        if (request.QualityRules is null || qualityRules.Length > 100 ||
            qualityRules.Any(x => x is null || x.AttributeId == Guid.Empty ||
                string.IsNullOrWhiteSpace(x.Unit) || x.Unit.Length > 50 ||
                x.Calculation != "MASS_WEIGHTED_MEAN") ||
            qualityRules.Select(x => x.AttributeId).Distinct().Count() != qualityRules.Length)
            errors["qualityRules"] = ["Use distinct quality attributes with supported calculation and unit."];
        if (string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 1000 ||
            string.IsNullOrWhiteSpace(request.ReviewedBy) || string.IsNullOrWhiteSpace(request.ReviewAuthority) ||
            request.ReviewedAt == default || request.ReviewedAt > now)
            errors["review"] = ["Record approved source, reviewer, authority and review time."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!await db.Markets.AnyAsync(x => x.Id == request.MarketId, ct) ||
            !await db.Commodities.AnyAsync(x => x.Id == request.CommodityId, ct) ||
            await db.ActiveSubstances.CountAsync(x => requiredSubstances.Contains(x.Id), ct) != requiredSubstances.Length ||
            await db.QualityAttributes.CountAsync(x => qualityRules.Select(r => r.AttributeId).Contains(x.Id), ct) != qualityRules.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["catalog"] = ["All referenced market, commodity, substances and quality attributes must exist."] });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT catalog.publish_blend_assessment_policy($1,$2)", connection, transaction);
            command.Parameters.AddWithValue(scope.Value.Actor);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb,
                Value = JsonSerializer.Serialize(new
                {
                    request.Id, companyId = scope.Value.Company, request.MarketId,
                    request.CommodityId, request.VersionNo, request.EffectiveFrom,
                    request.EffectiveTo, requiredSubstanceIds = requiredSubstances,
                    mrlMarginFraction = margin.ToString("0.############", CultureInfo.InvariantCulture),
                    request.ResidueBasis, qualityRules, request.EvidenceReference,
                    request.ReviewedBy, request.ReviewAuthority, request.ReviewedAt
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
            await command.ExecuteScalarAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/blend-assessment-policies/{request.Id}",
                new { request.Id, companyId = scope.Value.Company, request.VersionNo, publishedAt = now });
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or
            PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation)
        {
            return Results.Problem(statusCode: 409, title: "Policy version conflicts with published history or catalog references.",
                extensions: new Dictionary<string, object?> { ["code"] = "BLEND_ASSESSMENT_POLICY_CONFLICT" });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidParameterValue)
        { return Results.ValidationProblem(new Dictionary<string, string[]> { ["policy"] = [ex.MessageText] }); }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or
            PostgresErrorCodes.InsufficientPrivilege)
        { return Results.Problem(statusCode: 503, title: "Blend assessment policy schema or runtime grant is not ready."); }
    }

    private static async Task<(Guid Actor, Guid Company, string Role)?> ScopeAsync(HttpContext http,
        IdentityDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return null;
        var user = await db.Users.AsNoTracking().Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == actor && x.IsActive, ct);
        if (user?.OrganizationId is not Guid company || user.Organization?.Kind != OrganizationKind.Customer ||
            user.Role is not (Roles.CustomerManager or Roles.ExportStaff)) return null;
        return (actor, company, user.Role);
    }
}
