using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Modules.Capture;

public static class ResidueEvidenceEndpoints
{
    public static IEndpointRouteBuilder MapResidueEvidenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var groups = endpoints.MapGroup("/api/residue-source-groups").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Reviewed source residue fallback");
        groups.MapPost("", PublishGroupAsync).RequireAuthorization("residue.evidence.manage");
        groups.MapGet("", ListGroupsAsync);
        groups.MapGet("/{id:guid}", (Guid id, HttpContext http, CaptureDbContext db, IdentityDbContext identityDb, TimeProvider clock, CancellationToken ct) =>
            ListAsync<ResidueSourceGroupVersion>(http, db, identityDb, clock, "residue_source_group_versions", ct, id));
        var selections = endpoints.MapGroup("/api/residue-report-selections").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>()
            .WithTags("Reviewed residue evidence selection");
        selections.MapPost("", PublishSelectionAsync).RequireAuthorization("residue.evidence.manage");
        selections.MapGet("", ListSelectionsAsync);
        selections.MapGet("/{id:guid}", (Guid id, HttpContext http, CaptureDbContext db, IdentityDbContext identityDb, TimeProvider clock, CancellationToken ct) =>
            ListAsync<ResidueReportSelection>(http, db, identityDb, clock, "residue_report_selections", ct, id));
        return endpoints;
    }

    private static async Task<IResult> PublishGroupAsync(ResidueSourceGroupVersion request, HttpContext http,
        CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var errors = Review(request.Id, request.EffectiveFrom, request.EffectiveTo, request.EvidenceRef,
            request.ApplicabilityStatement, request.ReviewedBy, request.ReviewAuthority, request.ReviewedAt, now);
        if (request.CompanyId == Guid.Empty || request.SubstanceId == Guid.Empty || request.VersionNo < 1 ||
            string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100)
            errors["code"] = ["Explicit company, substance, code and positive version required."];
        if (request.SourceIds is not { Length: > 0 and <= 1000 } || request.SourceIds.Any(x => x == Guid.Empty) ||
            request.SourceIds.Distinct().Count() != request.SourceIds.Length)
            errors["sourceIds"] = ["Supply 1..1000 distinct Capture source IDs, not Ledger lot IDs."];
        if (request.SupportingResultIds is null || request.SupportingResultIds.Length > 1000 ||
            request.SupportingResultIds.Any(x => x == Guid.Empty) || request.SupportingResultIds.Distinct().Count() != request.SupportingResultIds.Length)
            errors["supportingResultIds"] = ["Supply distinct confirmed supporting result IDs, or an empty array for external reviewed evidence."];
        if (request.ProductStage != "RAW_PADDY" || request.ProductIdentity != "RAW_PADDY" ||
            string.IsNullOrWhiteSpace(request.MaterialBasis) || string.IsNullOrWhiteSpace(request.ResidueDefinition))
            errors["materialBasis"] = ["Explicit procurement RAW_PADDY material, analytical basis and residue definition required."];
        if (!ResidueReview.Interval(request.Lower, request.Upper) || request.Unit != "mg/kg" || request.DecimalPlaces is < 0 or > 18)
            errors["interval"] = ["Reviewed nonnegative finite interval, mg/kg and approved precision 0..18 required. MRL or observed maximum alone does not establish a fallback bound."];
        if (request.Precedence is not ("DIRECT_FIRST" or "GROUP_FIRST" or "UNTESTED_ONLY"))
            errors["precedence"] = ["Explicit DIRECT_FIRST, GROUP_FIRST or UNTESTED_ONLY precedence required."];
        if (errors.Count != 0 || request.SourceIds is null || request.SupportingResultIds is null)
            return Results.ValidationProblem(errors);
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, "residue.evidence.manage", request.CompanyId, ct)) return Results.Forbid();
        if (!await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM catalog.active_substances WHERE id={request.SubstanceId}) AS \"Value\"").SingleAsync(ct))
            return Invalid("substanceId", "Substance must exist in Catalog.");
        var sources = await (from source in db.SourceLots.AsNoTracking()
                             join item in db.ProcurementItems on source.ProcurementItemId equals item.Id
                             join procurement in db.Procurements on item.ProcurementId equals procurement.Id
                             where source.CompanyId == request.CompanyId && item.CompanyId == request.CompanyId &&
                                 procurement.CompanyId == request.CompanyId && request.SourceIds.Contains(source.Id)
                             select new { source.Id, procurement.ReceivedAt }).ToListAsync(ct);
        if (sources.Count != request.SourceIds.Length || sources.Any(x => x.ReceivedAt < request.EffectiveFrom ||
            request.EffectiveTo is { } end && x.ReceivedAt >= end))
            return Invalid("sourceIds", "Every explicit source must belong to this company and fall within the reviewed material receipt-time window.");
        var members = request.SourceIds.ToHashSet();
        var support = (await ConfirmedResultsAsync(db, request.CompanyId, request.SubstanceId, ct))
            .Where(x => members.Contains(x.SourceId)).Select(x => x.ResultId).ToHashSet();
        if (request.SupportingResultIds.Any(id => !support.Contains(id)))
            return Invalid("supportingResultIds", "Support must reference confirmed same-substance results from explicit group members.");
        var history = await ResidueEvidenceReader.ReadPayloadsAsync<ResidueSourceGroupVersion>(connection, transaction,
            "residue_source_group_versions", request.CompanyId, now, ct);
        var prior = history.Where(x => x.Code == request.Code).ToArray();
        if (history.Any(x => x.Id == request.Id) || prior.Any(x => x.VersionNo >= request.VersionNo) ||
            prior.Any(x => x.SubstanceId != request.SubstanceId) ||
            request.SupersedesId is { } old && !prior.Any(x => x.Id == old && x.SubstanceId == request.SubstanceId) ||
            request.SupersedesId is not null && prior.Any(x => x.SupersedesId == request.SupersedesId))
            return Conflict("Group ID/version or reviewed supersession conflicts with history.");
        var itemToPublish = request with { PublishedBy = actor, PublishedAt = now,
            SourceIds = request.SourceIds.Order().ToArray(), SupportingResultIds = request.SupportingResultIds.Order().ToArray() };
        try
        {
            await PublishAsync(connection, transaction, "publish_residue_group", actor, itemToPublish, ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Group ID/version or reviewed supersession conflicts with history.");
        }
        return Results.Created($"/api/residue-source-groups/{request.Id}", itemToPublish);
    }

    private static async Task<IResult> PublishSelectionAsync(ResidueReportSelection request, HttpContext http,
        CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var errors = Review(request.Id, request.EffectiveFrom, request.EffectiveTo, request.EvidenceRef,
            request.ApplicabilityStatement, request.ReviewedBy, request.ReviewAuthority, request.ReviewedAt, now);
        if (request.CompanyId == Guid.Empty || request.SourceId == Guid.Empty || request.SubstanceId == Guid.Empty || request.SelectedResultId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.MaterialBasis) || string.IsNullOrWhiteSpace(request.ResidueDefinition))
            errors["selection"] = ["Explicit company/source/substance/result, material basis and residue definition required."];
        if (request.SupersededResultIds is null || request.SupersededResultIds.Length > 1000 ||
            request.SupersededResultIds.Contains(request.SelectedResultId) || request.SupersededResultIds.Any(x => x == Guid.Empty) ||
            request.SupersededResultIds.Distinct().Count() != request.SupersededResultIds.Length)
            errors["supersededResultIds"] = ["Supply distinct reviewed superseded result IDs, excluding selected result; empty is valid."];
        if (request.AbsoluteUncertainty is not null && !ResidueReview.Number(request.AbsoluteUncertainty))
            errors["absoluteUncertainty"] = ["Use nonnegative decimal text normalized by documented domain review."];
        if (request.ReportingThreshold is not null && !ResidueReview.PositiveNumber(request.ReportingThreshold))
            errors["reportingThreshold"] = ["Use positive reviewed reporting threshold in the report concentration unit."];
        if (errors.Count != 0 || request.SupersededResultIds is null)
            return Results.ValidationProblem(errors);
        if (!Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, "residue.evidence.manage", request.CompanyId, ct)) return Results.Forbid();
        var candidates = (await ConfirmedResultsAsync(db, request.CompanyId, request.SubstanceId, ct)).Where(x => x.SourceId == request.SourceId).ToArray();
        if (!candidates.Any(x => x.ResultId == request.SelectedResultId) || request.SupersededResultIds.Any(id => !candidates.Any(x => x.ResultId == id)))
            return Invalid("selectedResultId", "Selected and superseded results must have confirmed same-company/source/substance associations.");
        var selectedRaw = await db.LabResults.AsNoTracking().SingleAsync(x => x.Id == request.SelectedResultId, ct);
        if (request.AbsoluteUncertainty is not null && (selectedRaw.ResultKind != "NUMERIC" || string.IsNullOrWhiteSpace(selectedRaw.Uncertainty)))
            return Invalid("absoluteUncertainty", "Normalized uncertainty requires numeric result with retained raw uncertainty evidence.");
        if (request.ReportingThreshold is not null && selectedRaw.ResultKind != "NOT_DETECTED")
            return Invalid("reportingThreshold", "Explicit reporting threshold applies only to NOT_DETECTED result.");
        var history = await ResidueEvidenceReader.ReadPayloadsAsync<ResidueReportSelection>(connection, transaction,
            "residue_report_selections", request.CompanyId, now, ct);
        if (history.Any(x => x.Id == request.Id) || request.SupersedesId is { } old && !history.Any(x => x.Id == old &&
                x.SourceId == request.SourceId && x.SubstanceId == request.SubstanceId) ||
            request.SupersedesId is not null && history.Any(x => x.SupersedesId == request.SupersedesId))
            return Conflict("Selection ID or reviewed supersession conflicts with history.");
        var item = request with { PublishedBy = actor, PublishedAt = now, SupersededResultIds = request.SupersededResultIds.Order().ToArray() };
        try
        {
            await PublishAsync(connection, transaction, "publish_residue_selection", actor, item, ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("Selection ID or reviewed supersession conflicts with history.");
        }
        return Results.Created($"/api/residue-report-selections/{request.Id}", item);
    }

    private sealed record ConfirmedResult(Guid ResultId, Guid SourceId);
    private static Task<List<ConfirmedResult>> ConfirmedResultsAsync(CaptureDbContext db, Guid companyId, Guid substanceId, CancellationToken ct) =>
        (from result in db.LabResults.AsNoTracking()
         join set in db.LabResultSets on result.ResultSetId equals set.Id
         join association in db.LabConfirmedAssociations on set.Id equals association.ResultSetId
         join confirmation in db.LabReportConfirmations on association.ConfirmationId equals confirmation.Id
         join sample in db.Samples on association.SampleId equals sample.Id
         where result.CompanyId == companyId && set.CompanyId == companyId && association.CompanyId == companyId &&
             confirmation.CompanyId == companyId && sample.CompanyId == companyId && result.SubstanceId == substanceId &&
             confirmation.ReportId == set.ReportId && confirmation.IdentityChecked && confirmation.TranscriptionChecked
         select new ConfirmedResult(result.Id, sample.SourceLotId)).ToListAsync(ct);

    private static Task<IResult> ListGroupsAsync(HttpContext http, CaptureDbContext db, IdentityDbContext identityDb, TimeProvider clock, CancellationToken ct) =>
        ListAsync<ResidueSourceGroupVersion>(http, db, identityDb, clock, "residue_source_group_versions", ct);
    private static Task<IResult> ListSelectionsAsync(HttpContext http, CaptureDbContext db, IdentityDbContext identityDb, TimeProvider clock, CancellationToken ct) =>
        ListAsync<ResidueReportSelection>(http, db, identityDb, clock, "residue_report_selections", ct);
    private static async Task<IResult> ListAsync<T>(HttpContext http, CaptureDbContext db, IdentityDbContext identityDb,
        TimeProvider clock, string table, CancellationToken ct, Guid? resourceId = null) where T : class
    {
        var company = await CaptureActor.GetCustomerCompanyAsync(http.User, identityDb, ct);
        if (company is null || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Results.Forbid();
        if (http.Request.Query.TryGetValue("companyId", out var raw) && (raw.Count != 1 || !Guid.TryParse(raw[0], out var requested) || requested != company))
            return Results.Forbid();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)tx.GetDbTransaction();
        if (!await ResidueReview.AuthorizeAsync(connection, transaction, actor, "residue.evidence.manage", company, ct) &&
            !await ResidueReview.AuthorizeAsync(connection, transaction, actor, "residue.evaluate", company, ct)) return Results.Forbid();
        var items = await ResidueEvidenceReader.ReadPayloadsAsync<T>(connection, transaction, table, company.Value, clock.GetUtcNow(), ct);
        if (resourceId is null) return Results.Ok(new { items });
        var item = items.SingleOrDefault(x => x switch
        {
            ResidueSourceGroupVersion group => group.Id == resourceId,
            ResidueReportSelection selection => selection.Id == resourceId,
            _ => false
        });
        return item is null ? Results.NotFound() : Results.Ok(item);
    }

    private static async Task PublishAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string function, Guid actor, T item, CancellationToken ct)
    {
        if (function is not ("publish_residue_group" or "publish_residue_selection")) throw new ArgumentOutOfRangeException(nameof(function));
        await using var command = new NpgsqlCommand($"SELECT capture.{function}($1,$2)", connection, transaction);
        command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(item));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static Dictionary<string, string[]> Review(Guid id, DateTimeOffset from, DateTimeOffset? to,
        string evidence, string applicability, string reviewer, string authority, DateTimeOffset reviewed, DateTimeOffset now) =>
        ResidueReview.Validate(id, from, to, evidence, applicability, reviewer, authority, reviewed, now);
    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title,
        extensions: new Dictionary<string, object?> { ["code"] = "RESIDUE_PUBLICATION_CONFLICT" });
}
