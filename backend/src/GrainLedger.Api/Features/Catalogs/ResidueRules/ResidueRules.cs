using System.Globalization;
using System.Numerics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using GrainLedger.Api.Features.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.Features.Catalogs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResidueProfileVersion(Guid Id, string Code, int VersionNo, string Kind,
    [property: JsonRequired] int SchemaVersion, Guid SubstanceId, Guid? CommodityId,
    string ResidueDefinition, string Unit, [property: JsonRequired] int DecimalPlaces,
    [property: JsonRequired] IReadOnlyList<ResidueLaboratorySettings> Laboratory,
    [property: JsonRequired] IReadOnlyList<ResidueProcessStep> Steps,
    [property: JsonRequired] bool AsReceivedConfirmed,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid? SupersedesId,
    string EvidenceRef, string ApplicabilityStatement, string ReviewedBy, string ReviewAuthority,
    DateTimeOffset ReviewedAt, Guid? PublishedBy = null, DateTimeOffset? PublishedAt = null,
    Guid? CompanyId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResidueProfileRetirement? Retirement = null);

public sealed record ResidueProfileRetirement(Guid ProfileId, string Code, Guid? CompanyId, string Reason,
    Guid RetiredBy, DateTimeOffset RetiredAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RetireResidueProfileRequest(string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResidueLaboratorySettings(string AnalyticalMethod, string? NumericInterpretationKind,
    string? RelativeUncertaintyPercent, [property: JsonRequired] bool RelativeUncertaintyAtZero,
    [property: JsonRequired] bool BelowLimit, string? NdThresholdKind);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResidueProcessStep(Guid Id, string Process, string InputStage, string OutputStage,
    string OutputFraction, string Conditions, string FactorLower, string FactorUpper,
    string FactorSemantics, string? AnchorStage, [property: JsonRequired] bool HomogeneousMixing);

public sealed class ResiduePolicyReader
{
    public async Task<ResiduePolicySnapshot> ReadAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid companyId, IReadOnlyCollection<Guid> substanceIds,
        DateTimeOffset snapshotAt, CancellationToken ct)
    {
        // Keep historical effective windows: each physical graph node has its own process time.
        await using var command = new NpgsqlCommand("""
            SELECT r.payload::text, t.profile_id, t.reason, t.retired_by, t.retired_at
            FROM grainledger.catalog_residue_rule_versions r
            LEFT JOIN grainledger.catalog_residue_rule_retirements t ON t.code=r.code AND t.company_id IS NOT DISTINCT FROM r.company_id
            WHERE r.substance_id=ANY($1) AND r.published_at <= $2 AND r.effective_from <= $2
                AND (r.company_id IS NULL OR r.company_id=$3)
                AND r.payload->>'Kind' = 'FLOW' AND r.payload->>'SchemaVersion' = '2'
            ORDER BY r.code,r.company_id NULLS FIRST,r.version_no
            """, connection, transaction);
        command.Parameters.AddWithValue(substanceIds.ToArray());
        command.Parameters.AddWithValue(snapshotAt);
        command.Parameters.AddWithValue(companyId);
        var rules = new List<ResidueProfileVersion>();
        var settings = new List<ResiduePolicySettingVersion>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) rules.Add(ResidueRuleEndpoints.ReadRule(reader));
        }
        await using var settingsCommand = new NpgsqlCommand("""
            SELECT s.payload::text FROM grainledger.catalog_residue_policy_setting_versions s
            WHERE s.company_id=$1 AND s.published_at <= $2 AND s.effective_from <= $2
                AND EXISTS (SELECT 1 FROM grainledger.catalog_residue_rule_versions r
                    WHERE r.code=s.rule_code AND (r.company_id IS NULL OR r.company_id=s.company_id)
                    AND r.payload->>'Kind'='FLOW' AND r.payload->>'SchemaVersion'='2'
                    AND r.published_at <= $2)
            ORDER BY s.rule_code,s.version_no
            """, connection, transaction);
        settingsCommand.Parameters.AddWithValue(companyId);
        settingsCommand.Parameters.AddWithValue(snapshotAt);
        await using (var reader = await settingsCommand.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct)) settings.Add(JsonSerializer.Deserialize<ResiduePolicySettingVersion>(reader.GetString(0))!);
        }
        return new ResiduePolicySnapshot(rules, settings);
    }
}

public static class ResidueRuleEndpoints
{
    public static IEndpointRouteBuilder MapResidueRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/residue-rules").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Reviewed residue rules");
        group.MapPost("", PublishAsync);
        group.MapPost("/{id:guid}/retire", RetireAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> RetireAsync(Guid id, RetireResidueProfileRequest request, HttpContext http,
        AppDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason is required and must not exceed 1000 characters."] });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        var scope = await ResidueReview.ReadScopeAsync(http, connection, transaction, ct);
        if (!scope.Allowed) return Results.Forbid();
        var profile = (await ReadVersionsAsync(connection, transaction, scope.CompanyId, ct)).SingleOrDefault(x => x.Id == id);
        if (profile is null) return Results.NotFound();
        var permission = profile.CompanyId is null ? Permissions.ResiduePolicyManage : Permissions.ResidueCompanyPolicyManage;
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, permission, profile.CompanyId, ct)) return Results.Forbid();
        if (profile.Retirement is not null) return Conflict("Profile series is already retired.");
        try
        {
            await using var command = new NpgsqlCommand("SELECT grainledger.catalog_retire_residue_rule($1,$2,$3)", connection, transaction);
            command.Parameters.AddWithValue(actor);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(reason);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var retiredAt = reader.GetFieldValue<DateTimeOffset>(0);
            await reader.CloseAsync();
            await tx.CommitAsync(ct);
            return Results.Ok(new ResidueProfileRetirement(id, profile.Code, profile.CompanyId, reason, actor, retiredAt));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Profile series is already retired.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> PublishAsync(ResidueProfileVersion request, HttpContext http,
        AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        var now = clock.GetUtcNow();
        var errors = ValidateRule(request, now);
        if (request.Retirement is not null) errors["retirement"] = ["Retirement metadata is server-owned."];
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        var permission = request.CompanyId is null ? Permissions.ResiduePolicyManage : Permissions.ResidueCompanyPolicyManage;
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, permission, request.CompanyId, ct)) return Results.Forbid();
        if (!await db.ActiveSubstances.AnyAsync(x => x.Id == request.SubstanceId, ct) ||
            request.CommodityId is { } commodity && !await db.Commodities.AnyAsync(x => x.Id == commodity, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["substanceId"] = ["Substance and commodity must exist."] });
        var versions = await ReadVersionsAsync(connection, transaction, request.CompanyId, ct);
        var prior = versions.Where(x => x.Code == request.Code && x.CompanyId == request.CompanyId).ToArray();
        if (prior.Any(x => x.Retirement is not null)) return Conflict("Retired profile series cannot be republished.");
        if (versions.Any(x => x.Code == request.Code && x.CompanyId is null &&
            (x.SubstanceId != request.SubstanceId || x.Kind != request.Kind || x.CommodityId != request.CommodityId)))
            return Conflict("Company override must retain system profile substance, kind and commodity applicability.");
        if (versions.Any(x => x.Id == request.Id) || prior.Any(x => x.VersionNo >= request.VersionNo) ||
            prior.Any(x => x.SubstanceId != request.SubstanceId || x.Kind != request.Kind || x.CommodityId != request.CommodityId) ||
            request.SupersedesId is { } previous && !prior.Any(x => x.Id == previous && x.SubstanceId == request.SubstanceId && x.Kind == request.Kind) ||
            prior.Any(x => x.SupersedesId == request.SupersedesId && request.SupersedesId is not null))
            return Conflict("Rule ID/version or supersession conflicts with published history.");
        var predecessor = request.SupersedesId is { } predecessorId
            ? prior.SingleOrDefault(x => x.Id == predecessorId)
            : prior.OrderByDescending(x => x.VersionNo).FirstOrDefault();
        if (predecessor is not null && request.Steps.Any(step => predecessor.Steps.Any(old =>
                SamePath(old, step) && old.Id != step.Id)) ||
            request.Steps.Any(step => prior.Any(version => version.Steps.Any(old =>
                old.Id == step.Id && !SamePath(old, step)))))
            return Conflict("Unchanged processing paths must retain their step IDs; step IDs cannot be reused for different paths.");
        // The protected SQL publisher compares PublishedAt with its own clock.
        // Use that database clock so a small API/DB clock skew cannot reject a
        // newly reviewed rule as future-published.
        var databaseNow = await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var rule = request with { PublishedBy = actor, PublishedAt = databaseNow };
        await using var insert = new NpgsqlCommand("SELECT grainledger.catalog_publish_residue_rule($1,$2)", connection, transaction);
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
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return Results.Forbid();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["rule"] = ["Reviewed rule is incomplete or invalid."] });
        }
        return Results.Created($"/api/residue-rules/{rule.Id}", rule);
    }

    internal static Dictionary<string, string[]> ValidateRule(ResidueProfileVersion request, DateTimeOffset now)
    {
        var errors = ResidueReview.Validate(request.Id, request.EffectiveFrom, request.EffectiveTo,
            request.EvidenceRef, request.ApplicabilityStatement, request.ReviewedBy, request.ReviewAuthority,
            request.ReviewedAt, now);
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100 || request.VersionNo < 1)
            errors["code"] = ["Code (maximum 100 characters) and positive version are required."];
        if (request.Kind != "FLOW" || request.SchemaVersion != 2 || !request.AsReceivedConfirmed)
            errors["kind"] = ["Schema 2 FLOW profile with as-received confirmation required."];
        if (request.SubstanceId == Guid.Empty || request.CommodityId is null || request.CommodityId == Guid.Empty ||
            request.DecimalPlaces is < 0 or > 18 || request.Unit != "mg/kg" || string.IsNullOrWhiteSpace(request.ResidueDefinition))
            errors["applicability"] = ["Substance, commodity, residue definition, mg/kg and precision 0..18 required."];
        if (request.CompanyId == Guid.Empty)
            errors["companyId"] = ["Use null for system scope or an explicit owning company."];
        if (request.Laboratory is null || request.Steps is null)
        {
            errors["profile"] = ["Laboratory and steps arrays are required."];
            return errors;
        }
        if (request.Laboratory.Count == 0 && request.Steps.Count == 0)
            errors["profile"] = ["At least one laboratory entry or processing step required."];
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lab in request.Laboratory)
        {
            if (lab is null || string.IsNullOrWhiteSpace(lab.AnalyticalMethod) || !methods.Add(lab.AnalyticalMethod) ||
                lab.NumericInterpretationKind is not (null or "NUMERIC_ABSOLUTE_UNCERTAINTY" or "NUMERIC_RELATIVE_UNCERTAINTY") ||
                lab.NdThresholdKind is not (null or "REPORTING_THRESHOLD" or "LOD" or "LOQ") ||
                (lab.NumericInterpretationKind is null && !lab.BelowLimit && lab.NdThresholdKind is null) ||
                (lab.NumericInterpretationKind == "NUMERIC_RELATIVE_UNCERTAINTY"
                    ? !ResidueReview.Number(lab.RelativeUncertaintyPercent)
                    : lab.RelativeUncertaintyPercent is not null || lab.RelativeUncertaintyAtZero))
                errors["laboratory"] = ["Each exact method must be unique and enable valid reviewed result forms and uncertainty settings."];
        }
        var ids = new HashSet<Guid>();
        var paths = new Dictionary<(string, string, string, string), HashSet<string>>();
        foreach (var step in request.Steps)
        {
            if (step is null) { errors["steps"] = ["Processing steps cannot be null."]; continue; }
            if (step.Id == Guid.Empty || !ids.Add(step.Id) || !SupportedPath(step) ||
                string.IsNullOrWhiteSpace(step.Conditions) || !ResidueReview.Interval(step.FactorLower, step.FactorUpper) ||
                step.FactorSemantics is not ("STEP_CONCENTRATION" or "CUMULATIVE_CONCENTRATION") ||
                (step.FactorSemantics == "STEP_CONCENTRATION" ? step.AnchorStage is not null : !SupportedAnchor(step)))
                errors["steps"] = ["Each step requires a unique ID, supported path and anchor, reviewed conditions and valid exact factor interval."];
            var key = (step.Process, step.InputStage, step.OutputStage, step.OutputFraction);
            if (!paths.TryGetValue(key, out var conditions)) paths[key] = conditions = new(StringComparer.Ordinal);
            if (step.Conditions is null || !conditions.Add(step.Conditions) ||
                conditions.Count > 1 && conditions.Contains("UNCONDITIONAL"))
                errors["steps"] = ["Processing rows must not overlap for the same physical path."];
        }
        return errors;
    }

    private static bool SamePath(ResidueProcessStep a, ResidueProcessStep b) =>
        a.Process == b.Process && a.InputStage == b.InputStage && a.OutputStage == b.OutputStage &&
        a.OutputFraction == b.OutputFraction && a.Conditions == b.Conditions;

    private static bool SupportedPath(ResidueProcessStep step)
    {
        var rice = step.InputStage is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE";
        var material = step.InputStage is "RAW_PADDY" or "DRIED_PADDY" or "HEAD_RICE" or "BROKEN_RICE" or "BRAN" or "HUSK" or "BLENDED_RICE";
        return step.Process switch
        {
            "DRYING" => step.InputStage == "RAW_PADDY" && step.OutputStage == "DRIED_PADDY" && step.OutputFraction == "DRIED_PADDY",
            "MILLING" => step.InputStage == "DRIED_PADDY" && step.OutputStage == step.OutputFraction && step.OutputStage is "HEAD_RICE" or "BROKEN_RICE" or "BRAN" or "HUSK",
            "BLENDING" => rice && step.OutputStage == "BLENDED_RICE" && step.OutputFraction == "BLENDED_RICE",
            "BAGGING" => rice && step.OutputStage == "BAGGED_RICE" && step.OutputFraction == step.InputStage,
            "MERGE" or "CUSTODY_INGRESS" or "CUSTODY_WITHDRAW" => step.InputStage == step.OutputStage &&
                (material && step.OutputFraction == step.InputStage || step.InputStage == "BAGGED_RICE" &&
                    step.OutputFraction is "HEAD_RICE" or "BROKEN_RICE" or "BLENDED_RICE"),
            _ => false
        };
    }

    private static bool SupportedAnchor(ResidueProcessStep step) => step.AnchorStage switch
    {
        "RAW_PADDY" => true,
        "DRIED_PADDY" => step.InputStage is not "RAW_PADDY",
        "HEAD_RICE" or "BROKEN_RICE" => step.AnchorStage == step.InputStage || step.InputStage is "BLENDED_RICE" or "BAGGED_RICE",
        "BRAN" or "HUSK" => step.AnchorStage == step.InputStage,
        "BLENDED_RICE" => step.InputStage is "BLENDED_RICE" or "BAGGED_RICE",
        "BAGGED_RICE" => step.InputStage == "BAGGED_RICE",
        _ => false
    };

    private static async Task<IResult> ListAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        var scope = await ResidueReview.ReadScopeAsync(http, connection, transaction, ct);
        if (!scope.Allowed) return Results.Forbid();
        return Results.Ok(new { items = await ReadVersionsAsync(connection, transaction, scope.CompanyId, ct) });
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        var scope = await ResidueReview.ReadScopeAsync(http, connection, transaction, ct);
        if (!scope.Allowed) return Results.Forbid();
        var item = (await ReadVersionsAsync(connection, transaction, scope.CompanyId, ct)).SingleOrDefault(x => x.Id == id);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }


    internal static async Task<List<ResidueProfileVersion>> ReadVersionsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? companyId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.payload::text, t.profile_id, t.reason, t.retired_by, t.retired_at
            FROM grainledger.catalog_residue_rule_versions r
            LEFT JOIN grainledger.catalog_residue_rule_retirements t ON t.code=r.code AND t.company_id IS NOT DISTINCT FROM r.company_id
            WHERE (r.company_id IS NULL OR r.company_id=$1)
                AND r.payload->>'Kind'='FLOW' AND r.payload->>'SchemaVersion'='2'
            ORDER BY r.code,r.company_id NULLS FIRST,r.version_no
            """, connection, tx);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)companyId ?? DBNull.Value });
        var result = new List<ResidueProfileVersion>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadRule(reader));
        return result;
    }

    internal static ResidueProfileVersion ReadRule(NpgsqlDataReader reader)
    {
        var rule = JsonSerializer.Deserialize<ResidueProfileVersion>(reader.GetString(0))!;
        return reader.IsDBNull(1) ? rule : rule with
        {
            Retirement = new ResidueProfileRetirement(reader.GetGuid(1), rule.Code, rule.CompanyId,
                reader.GetString(2), reader.GetGuid(3), reader.GetFieldValue<DateTimeOffset>(4))
        };
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

    internal static async Task<(bool Allowed, Guid? CompanyId)> ReadScopeAsync(HttpContext http,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return (false, null);
        await using var command = new NpgsqlCommand("""
            SELECT u."Role",u."OrganizationId",o."Kind"::text FROM grainledger.identity_users u
            JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
            WHERE u."Id"=$1 AND u."IsActive"
            """, connection, transaction);
        command.Parameters.AddWithValue(actor);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) ||
            !Roles.All.TryGetValue(reader.GetString(0), out var role) ||
            !Enum.TryParse<OrganizationKind>(reader.GetString(2), out var kind) || role.OrganizationKind != kind ||
            !role.Permissions.Contains(Permissions.ResiduePolicyManage) && !role.Permissions.Contains(Permissions.ResidueEvaluate))
            return (false, null);
        Guid? companyId = kind == OrganizationKind.Customer ? reader.GetGuid(1) : null;
        if (http.Request.Query.TryGetValue("companyId", out var requested) &&
            (requested.Count != 1 || !Guid.TryParse(requested[0], out var supplied) || companyId != supplied))
            return (false, null);
        return (true, companyId);
    }

    public static async Task<bool> AuthorizeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actor, string permission, Guid? companyId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT u."Role",o."Kind"::text FROM grainledger.identity_users u
            JOIN grainledger.identity_organizations o ON o."Id"=u."OrganizationId"
            WHERE u."Id"=$1 AND u."IsActive" AND ($2::uuid IS NULL OR u."OrganizationId"=$2)
            """, connection, transaction);
        command.Parameters.AddWithValue(actor);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)companyId ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) && !reader.IsDBNull(0) && Roles.All.TryGetValue(reader.GetString(0), out var role) &&
            Enum.TryParse<OrganizationKind>(reader.GetString(1), out var kind) && role.OrganizationKind == kind && role.Permissions.Contains(permission);
    }
}
