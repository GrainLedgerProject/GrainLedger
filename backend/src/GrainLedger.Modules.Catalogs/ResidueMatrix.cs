using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Catalogs;

public sealed record ResidueMatrixRuleChange(Guid? BaseId, Guid SourceId, ResidueRuleVersion Rule);
public sealed record ResidueMatrixSettingChange(string RuleCode, Guid? BaseId, string Mode,
    string EvidenceRef, string ApplicabilityStatement);
public sealed record ResidueMatrixPublication(Guid? CompanyId,
    IReadOnlyList<ResidueMatrixRuleChange> Rules, IReadOnlyList<ResidueMatrixSettingChange> Settings,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, string ReviewedBy,
    string ReviewAuthority, DateTimeOffset ReviewedAt);

public static class ResidueMatrixEndpoints
{
    public static async Task<IResult> PublishAsync(ResidueMatrixPublication request, HttpContext http,
        CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        if (request.Rules is null || request.Settings is null || request.Rules.Any(x => x is null || x.Rule is null) ||
            request.Settings.Any(x => x is null) || request.Rules.Count + request.Settings.Count is < 1 or > 200 ||
            request.CompanyId == Guid.Empty || request.CompanyId is null && request.Settings.Count != 0 ||
            request.Rules.Select(x => x.Rule.Code).Distinct(StringComparer.Ordinal).Count() != request.Rules.Count ||
            request.Settings.Select(x => x.RuleCode).Distinct(StringComparer.Ordinal).Count() != request.Settings.Count)
            return Invalid("matrix", "Publish 1..200 distinct complete rule/setting changes in one scope; system publication cannot include company adoption.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        var permission = request.CompanyId is null ? Permissions.ResiduePolicyManage : Permissions.ResidueCompanyPolicyManage;
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, permission, request.CompanyId, ct))
            return Results.Forbid();
        // Protected publishers compare the review and publication instants
        // against PostgreSQL time, so derive both validations from that clock.
        var now = await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var history = await ResidueRuleEndpoints.ReadVersionsAsync(connection, transaction, request.CompanyId, ct);
        var rules = new List<ResidueRuleVersion>();
        foreach (var change in request.Rules)
        {
            if (change.Rule.CompanyId != request.CompanyId) return Results.Forbid();
            var series = history.Where(x => x.Code == change.Rule.Code && x.CompanyId == request.CompanyId).ToArray();
            var tips = series.Where(x => !series.Any(next => next.SupersedesId == x.Id)).ToArray();
            if (tips.Length > 1 || tips.SingleOrDefault()?.Id != change.BaseId)
                return Conflict("Rule predecessor changed or series is conflicted. Draft was not rebased.");
            var source = history.SingleOrDefault(x => x.Id == change.SourceId && x.Code == change.Rule.Code);
            if (source is null || source.CompanyId != request.CompanyId && !(request.CompanyId is not null && source.CompanyId is null))
                return Conflict("Reviewed source is no longer available in this scope.");
            var sourceSeries = history.Where(x => x.Code == source.Code && x.CompanyId == source.CompanyId).ToArray();
            var sourceTips = sourceSeries.Where(x => !sourceSeries.Any(next => next.SupersedesId == x.Id)).ToArray();
            if (sourceTips.Length != 1 || sourceTips[0].Id != source.Id || change.BaseId is not null && source.Id != change.BaseId)
                return Conflict("Reviewed source changed. Keep draft and explicitly review current source.");
            if (source.SubstanceId != change.Rule.SubstanceId || source.Kind != change.Rule.Kind || source.CommodityId != change.Rule.CommodityId)
                return Invalid("rule", "Matrix revisions retain series substance, kind and commodity. Use expert setup for a new series.");
            var rule = change.Rule with
            {
                Id = Guid.NewGuid(), CompanyId = request.CompanyId, VersionNo = series.Length == 0 ? 1 : series.Max(x => x.VersionNo) + 1,
                SupersedesId = change.BaseId, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
                ReviewedBy = request.ReviewedBy, ReviewAuthority = request.ReviewAuthority, ReviewedAt = request.ReviewedAt,
                PublishedBy = actor, PublishedAt = now
            };
            var errors = ResidueRuleEndpoints.ValidateRule(rule, now);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            if (!await db.ActiveSubstances.AnyAsync(x => x.Id == rule.SubstanceId, ct) ||
                !await db.Commodities.AnyAsync(x => x.Id == rule.CommodityId, ct))
                return Invalid("rule", "Substance and commodity must exist.");
            // Same scientific record is not republished merely to repeat shared review metadata.
            if (change.BaseId is not null && SameScience(source, rule)) continue;
            rules.Add(rule);
        }
        var settingHistory = new List<ResiduePolicySettingVersion>();
        if (request.CompanyId is { } companyId)
        {
            await using var read = new NpgsqlCommand("SELECT payload::text FROM catalog.residue_policy_setting_versions WHERE company_id=$1", connection, transaction);
            read.Parameters.AddWithValue(companyId);
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) settingHistory.Add(JsonSerializer.Deserialize<ResiduePolicySettingVersion>(reader.GetString(0))!);
        }
        var settings = new List<ResiduePolicySettingVersion>();
        foreach (var change in request.Settings)
        {
            var series = settingHistory.Where(x => x.RuleCode == change.RuleCode).ToArray();
            var tips = series.Where(x => !series.Any(next => next.SupersedesId == x.Id)).ToArray();
            if (tips.Length > 1 || tips.SingleOrDefault()?.Id != change.BaseId)
                return Conflict("Company adoption predecessor changed. Draft was not rebased.");
            var setting = new ResiduePolicySettingVersion(Guid.NewGuid(), request.CompanyId!.Value, change.RuleCode,
                series.Length == 0 ? 1 : series.Max(x => x.VersionNo) + 1, change.Mode,
                request.EffectiveFrom, request.EffectiveTo, change.BaseId, change.EvidenceRef, change.ApplicabilityStatement,
                request.ReviewedBy, request.ReviewAuthority, request.ReviewedAt, actor, now);
            var errors = ResidueReview.Validate(setting.Id, setting.EffectiveFrom, setting.EffectiveTo,
                setting.EvidenceRef, setting.ApplicabilityStatement, setting.ReviewedBy, setting.ReviewAuthority, setting.ReviewedAt, now);
            if (string.IsNullOrWhiteSpace(setting.RuleCode) || setting.RuleCode.Length > 100 ||
                setting.Mode is not ("SYSTEM_DEFAULT" or "COMPANY_OVERRIDE"))
                errors["setting"] = ["Explicit series code and SYSTEM_DEFAULT or COMPANY_OVERRIDE required."];
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            settings.Add(setting);
        }
        try
        {
            foreach (var rule in rules)
                await PublishAsync("catalog.publish_residue_rule", rule, actor, connection, transaction, ct);
            foreach (var setting in settings)
                await PublishAsync("catalog.publish_residue_policy_setting", setting, actor, connection, transaction, ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Rule or setting history changed. Nothing published; draft remains unchanged.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return Results.Forbid();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            return Invalid("matrix", "Reviewed publication is invalid or selected scoped rule series does not exist. Nothing published.");
        }
        return Results.Ok(new { rules, settings });
    }

    private static bool SameScience(ResidueRuleVersion source, ResidueRuleVersion rule) =>
        (source with { Id = rule.Id, VersionNo = rule.VersionNo, SupersedesId = rule.SupersedesId,
            EffectiveFrom = rule.EffectiveFrom, EffectiveTo = rule.EffectiveTo, ReviewedBy = rule.ReviewedBy,
            ReviewAuthority = rule.ReviewAuthority, ReviewedAt = rule.ReviewedAt,
            PublishedBy = rule.PublishedBy, PublishedAt = rule.PublishedAt }) == rule;

    private static async Task PublishAsync<T>(string command, T item, Guid actor,
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var publish = new NpgsqlCommand($"SELECT {command}($1,$2)", connection, transaction);
        publish.Parameters.AddWithValue(actor);
        publish.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(item));
        await publish.ExecuteNonQueryAsync(ct);
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = "RESIDUE_PUBLICATION_CONFLICT" });
}
