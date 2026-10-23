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

public sealed record ResiduePolicySettingVersion(Guid Id, Guid CompanyId, string RuleCode, int VersionNo,
    string Mode, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid? SupersedesId,
    string EvidenceRef, string ApplicabilityStatement, string ReviewedBy, string ReviewAuthority,
    DateTimeOffset ReviewedAt, Guid? PublishedBy = null, DateTimeOffset? PublishedAt = null);

public sealed record ResiduePolicySnapshot(IReadOnlyList<ResidueRuleVersion> Rules,
    IReadOnlyList<ResiduePolicySettingVersion> Settings);

public static class ResiduePolicySettingEndpoints
{
    public static IEndpointRouteBuilder MapResiduePolicySettingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/residue-policy-settings").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Reviewed company residue policy settings");
        group.MapPost("", PublishAsync).RequireAuthorization(Permissions.ResidueCompanyPolicyManage);
        group.MapGet("", (HttpContext http, CatalogDbContext db, CancellationToken ct) => ListAsync(http, db, ct));
        group.MapGet("/{id:guid}", (Guid id, HttpContext http, CatalogDbContext db, CancellationToken ct) => ListAsync(http, db, ct, id));
        return endpoints;
    }

    private static async Task<IResult> PublishAsync(ResiduePolicySettingVersion request, HttpContext http,
        CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        var now = clock.GetUtcNow();
        var errors = ResidueReview.Validate(request.Id, request.EffectiveFrom, request.EffectiveTo,
            request.EvidenceRef, request.ApplicabilityStatement, request.ReviewedBy, request.ReviewAuthority,
            request.ReviewedAt, now);
        if (request.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(request.RuleCode) || request.RuleCode.Length > 100 || request.VersionNo < 1)
            errors["ruleCode"] = ["Explicit owning company, profile code (maximum 100 characters) and positive version required."];
        if (request.Mode is not ("SYSTEM_DEFAULT" or "COMPANY_OVERRIDE"))
            errors["mode"] = ["Explicit SYSTEM_DEFAULT adoption/return or COMPANY_OVERRIDE required."];
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, Permissions.ResidueCompanyPolicyManage, request.CompanyId, ct))
            return Results.Forbid();
        var item = request with { PublishedBy = actor, PublishedAt = now };
        await using var command = new NpgsqlCommand("SELECT catalog.publish_residue_policy_setting($1,$2)", connection, transaction);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(item));
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Problem(statusCode: 409, title: "Setting ID/version or reviewed supersession conflicts with history.",
                extensions: new Dictionary<string, object?> { ["code"] = "RESIDUE_PUBLICATION_CONFLICT" });
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return Results.Forbid();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidParameterValue)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["setting"] = ["Reviewed setting is incomplete or selected scoped rule series does not exist."] });
        }
        return Results.Created($"/api/residue-policy-settings/{item.Id}", item);
    }

    private static async Task<IResult> ListAsync(HttpContext http, CatalogDbContext db, CancellationToken ct, Guid? id = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        var scope = await ResidueReview.ReadScopeAsync(http, connection, transaction, ct);
        if (!scope.Allowed || scope.CompanyId is null) return Results.Forbid();
        await using var command = new NpgsqlCommand("""
            SELECT payload::text FROM catalog.residue_policy_setting_versions
            WHERE company_id=$1 AND ($2::uuid IS NULL OR id=$2) ORDER BY rule_code,version_no
            """, connection, transaction);
        command.Parameters.AddWithValue(scope.CompanyId.Value);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)id ?? DBNull.Value });
        var items = new List<ResiduePolicySettingVersion>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(JsonSerializer.Deserialize<ResiduePolicySettingVersion>(reader.GetString(0))!);
        if (id is null) return Results.Ok(new { items });
        return items.Count == 0 ? Results.NotFound() : Results.Ok(items[0]);
    }
}
