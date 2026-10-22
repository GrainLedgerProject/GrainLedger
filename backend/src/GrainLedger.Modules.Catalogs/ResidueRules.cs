using System.Globalization;
using System.Numerics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Catalogs;

public sealed record ResidueRuleVersion(Guid Id, string Code, int VersionNo, string Kind,
    Guid SubstanceId, string ResidueDefinition, string InputStage, string OutputStage,
    string InputBasis, string OutputBasis, string OutputFraction, string Process, string Conditions,
    string Unit, [property: JsonRequired] int DecimalPlaces, string? InterpretationKind, string? NdThresholdKind,
    string? FactorLower, string? FactorUpper, string? FactorSemantics, [property: JsonRequired] bool HomogeneousMixing,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid? SupersedesId,
    string EvidenceRef, string ApplicabilityStatement, string ReviewedBy, string ReviewAuthority,
    DateTimeOffset ReviewedAt, Guid? PublishedBy = null, DateTimeOffset? PublishedAt = null,
    Guid? CommodityId = null, string AnalyticalMethod = "");

public interface IResiduePolicyReader
{
    Task<IReadOnlyList<ResidueRuleVersion>> ReadAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, IReadOnlyCollection<Guid> substanceIds,
        DateTimeOffset snapshotAt, CancellationToken ct);
}

public sealed class ResiduePolicyReader : IResiduePolicyReader
{
    public async Task<IReadOnlyList<ResidueRuleVersion>> ReadAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, IReadOnlyCollection<Guid> substanceIds,
        DateTimeOffset snapshotAt, CancellationToken ct)
    {
        // Keep historical effective windows: each physical graph node has its own process time.
        await using var command = new NpgsqlCommand("""
            SELECT payload::text FROM catalog.residue_rule_versions
            WHERE substance_id=ANY($1) AND published_at <= $2 AND effective_from <= $2
            ORDER BY code,version_no
            """, connection, transaction);
        command.Parameters.AddWithValue(substanceIds.ToArray());
        command.Parameters.AddWithValue(snapshotAt);
        var result = new List<ResidueRuleVersion>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(JsonSerializer.Deserialize<ResidueRuleVersion>(reader.GetString(0))!);
        return result;
    }
}

public static class ResidueRuleEndpoints
{
    public static IEndpointRouteBuilder MapResidueRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/residue-rules").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Reviewed residue rules");
        group.MapPost("", PublishAsync).RequireAuthorization("residue.policy.manage");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> PublishAsync(ResidueRuleVersion request, HttpContext http,
        CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        var now = clock.GetUtcNow();
        var errors = ResidueReview.Validate(request.Id, request.EffectiveFrom, request.EffectiveTo,
            request.EvidenceRef, request.ApplicabilityStatement, request.ReviewedBy, request.ReviewAuthority,
            request.ReviewedAt, now);
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100 || request.VersionNo < 1)
            errors["code"] = ["Code (maximum 100 characters) and positive version are required."];
        if (request.SubstanceId == Guid.Empty || request.DecimalPlaces is < 0 or > 18 || request.Unit != "mg/kg")
            errors["unit"] = ["Explicit substance, mg/kg concentration unit and approved precision 0..18 required."];
        if (request.CommodityId is null || request.CommodityId == Guid.Empty)
            errors["commodityId"] = ["Explicit Catalog commodity applicability required; no universal default."];
        if (new[] { request.ResidueDefinition, request.InputStage, request.OutputStage, request.InputBasis, request.OutputBasis }.Any(string.IsNullOrWhiteSpace))
            errors["applicability"] = ["Explicit residue definition, stages and analytical bases required."];
        if (request.Kind == "INTERPRETATION")
        {
            if (request.InterpretationKind is not ("NUMERIC_ABSOLUTE_UNCERTAINTY" or "BELOW_LIMIT" or "ND") ||
                string.IsNullOrWhiteSpace(request.AnalyticalMethod) || request.InputBasis != request.OutputBasis ||
                request.InputStage != request.OutputStage || request.FactorLower is not null || request.FactorUpper is not null ||
                request.FactorSemantics is not null || request.HomogeneousMixing ||
                (request.InterpretationKind == "ND" ? request.NdThresholdKind is not ("REPORTING_THRESHOLD" or "LOD" or "LOQ") : request.NdThresholdKind is not null))
                errors["interpretationKind"] = ["Reviewed interpretation requires exact method, unchanged basis/stage and explicit ND threshold policy; no processing factors."];
        }
        else if (request.Kind == "PROCESS")
        {
            if (!ResidueReview.Interval(request.FactorLower, request.FactorUpper) ||
                request.FactorSemantics is not ("STEP_CONCENTRATION" or "CUMULATIVE_CONCENTRATION") ||
                string.IsNullOrWhiteSpace(request.Process) || string.IsNullOrWhiteSpace(request.OutputFraction) ||
                string.IsNullOrWhiteSpace(request.Conditions) || request.InterpretationKind is not null || request.NdThresholdKind is not null)
                errors["factor"] = ["Reviewed nonnegative concentration-factor interval, semantics, process, fraction and conditions required."];
        }
        else errors["kind"] = ["Use INTERPRETATION or PROCESS."];
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, "residue.policy.manage", null, ct)) return Results.Forbid();
        if (!await db.ActiveSubstances.AnyAsync(x => x.Id == request.SubstanceId, ct) ||
            request.CommodityId is { } commodity && !await db.Commodities.AnyAsync(x => x.Id == commodity, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["substanceId"] = ["Substance and commodity must exist."] });
        var versions = await ReadVersionsAsync(connection, transaction, ct);
        var prior = versions.Where(x => x.Code == request.Code).ToArray();
        if (versions.Any(x => x.Id == request.Id) || prior.Any(x => x.VersionNo >= request.VersionNo) ||
            prior.Any(x => x.SubstanceId != request.SubstanceId || x.Kind != request.Kind || x.CommodityId != request.CommodityId) ||
            request.SupersedesId is { } previous && !prior.Any(x => x.Id == previous && x.SubstanceId == request.SubstanceId && x.Kind == request.Kind) ||
            prior.Any(x => x.SupersedesId == request.SupersedesId && request.SupersedesId is not null))
            return Conflict("Rule ID/version or supersession conflicts with published history.");
        var rule = request with { PublishedBy = actor, PublishedAt = now };
        await using var insert = new NpgsqlCommand("SELECT catalog.publish_residue_rule($1,$2)", connection, transaction);
        insert.Parameters.AddWithValue(actor);
        insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(rule));
        try
        {
            await insert.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Rule ID/version or reviewed supersession conflicts with history.");
        }
        return Results.Created($"/api/residue-rules/{rule.Id}", rule);
    }

    private static async Task<IResult> ListAsync(HttpContext http, CatalogDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await CanReadAsync(http, connection, transaction, ct)) return Results.Forbid();
        return Results.Ok(new { items = await ReadVersionsAsync(connection, transaction, ct) });
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, CatalogDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await CanReadAsync(http, connection, transaction, ct)) return Results.Forbid();
        var item = (await ReadVersionsAsync(connection, transaction, ct)).SingleOrDefault(x => x.Id == id);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task<bool> CanReadAsync(HttpContext http, NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct) =>
        Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) &&
        (await ResidueReview.AuthorizeAsync(connection, tx, actor, "residue.policy.manage", null, ct) ||
         await ResidueReview.AuthorizeAsync(connection, tx, actor, "residue.evaluate", null, ct));

    private static async Task<List<ResidueRuleVersion>> ReadVersionsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT payload::text FROM catalog.residue_rule_versions ORDER BY code,version_no", connection, tx);
        var result = new List<ResidueRuleVersion>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(JsonSerializer.Deserialize<ResidueRuleVersion>(reader.GetString(0))!);
        return result;
    }

    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = "RESIDUE_PUBLICATION_CONFLICT" });
}

// Shared review validation is public so Capture applies the same scientific-publication gate.
public static class ResidueReview
{
    private static readonly Regex NumberPattern = new(@"\A[0-9]{1,24}(\.[0-9]{1,18})?\z", RegexOptions.CultureInvariant);
    public static bool Number(string? value) => value is not null && NumberPattern.IsMatch(value);
    public static bool PositiveNumber(string? value) => Number(value) && value!.Any(x => x is >= '1' and <= '9');
    public static bool Interval(string? lower, string? upper) => Number(lower) && Number(upper) &&
        ScaledValue(lower!) <= ScaledValue(upper!);

    private static BigInteger ScaledValue(string value)
    {
        var dot = value.IndexOf('.');
        if (dot < 0) return BigInteger.Parse(value, CultureInfo.InvariantCulture) * Scale;
        var whole = BigInteger.Parse(value.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture);
        var fraction = BigInteger.Parse(value.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture);
        return whole * Scale + fraction * BigInteger.Pow(10, 18 - (value.Length - dot - 1));
    }
    private static readonly BigInteger Scale = BigInteger.Pow(10, 18);

    public static Dictionary<string, string[]> Validate(Guid id, DateTimeOffset from, DateTimeOffset? to,
        string evidence, string applicability, string reviewer, string authority, DateTimeOffset reviewedAt, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (id == Guid.Empty) errors["id"] = ["Nonempty immutable version ID required."];
        if (from == default || to <= from) errors["effectiveFrom"] = ["Explicit effective interval required; end must follow start."];
        if (new[] { evidence, applicability, reviewer, authority }.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4000))
            errors["review"] = ["Evidence reference, applicability statement, reviewer and authority required (maximum 4000 characters each). Publication is not scientific approval by application role alone."];
        if (reviewedAt == default || reviewedAt > now) errors["reviewedAt"] = ["Completed domain review time required."];
        return errors;
    }

    public static async Task<bool> AuthorizeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actor, string permission, Guid? companyId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT u."Role",o."Kind"::text FROM identity.users u
            JOIN identity.organizations o ON o."Id"=u."OrganizationId"
            WHERE u."Id"=$1 AND u."IsActive" AND ($2::uuid IS NULL OR u."OrganizationId"=$2)
            """, connection, transaction);
        command.Parameters.AddWithValue(actor);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)companyId ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) && !reader.IsDBNull(0) && Roles.All.TryGetValue(reader.GetString(0), out var role) &&
            Enum.TryParse<OrganizationKind>(reader.GetString(1), out var kind) && role.OrganizationKind == kind && role.Permissions.Contains(permission);
    }
}
