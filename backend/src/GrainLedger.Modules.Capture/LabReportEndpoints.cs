using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record LabResultInput(string? TestLabel, string? SubstanceId, string? OriginalResultText,
    string? ResultKind, string? NumericValue = null, string? Unit = null, string? Lod = null,
    string? Loq = null, string? Method = null, string? Uncertainty = null, string? Reference = null);
public sealed record LabResultSetInput(Guid EntityId, Guid? SampleId, string? LaboratorySampleReference,
    string? StatedSampleCode, string? StatedSealCode, string? SectionReference, IReadOnlyList<LabResultInput>? Results);
public sealed record LabReportInput(Guid EntityId, string? LaboratoryName, string? ReportNumber, DateOnly IssuedOn,
    DateTimeOffset ReceivedAt, string? ReportKind, Guid? RelatedReportId, string? RelationshipReason,
    string? FileName, string? ContentType, string? ContentBase64, bool CompanyOnlyConfirmed,
    string? LaboratoryConclusion, string? StatedStandard, IReadOnlyList<LabResultSetInput>? ResultSets);
public sealed record CaptureLabReportRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion, LabReportInput? Payload);
public sealed record DeleteLabReportRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion);
public sealed record LabReportMatchInput(Guid ResultSetId, Guid SampleId, string? IdentityBasis, string? EvidenceRef = null);
public sealed record ConfirmLabReportInput(bool IdentityChecked, bool TranscriptionChecked, IReadOnlyList<LabReportMatchInput>? Matches);
public sealed record ConfirmLabReportRequest(string? DeviceId, Guid ClientOperationId, DateTimeOffset CapturedAt,
    int PayloadSchemaVersion, ConfirmLabReportInput? Payload);
public sealed record LabResultResponse(Guid Id, string TestLabel, Guid? SubstanceId, string OriginalResultText,
    string ResultKind, string? NumericValue, string? Unit, string? Lod, string? Loq, string? Method,
    string? Uncertainty, string? Reference);
public sealed record LabResultSetResponse(Guid Id, Guid? SampleId, Guid? ConfirmedSampleId, Guid? SourceLotId,
    string? LaboratorySampleReference, string? StatedSampleCode, string? StatedSealCode, string? SectionReference,
    string IdentityStatus, IReadOnlyList<LabResultResponse> Results);
public sealed record LabRelatedReportResponse(Guid Id, string ReportKind, string? RelationshipReason);
public sealed record LabReportResponse(Guid Id, Guid CompanyId, string LaboratoryName, string ReportNumber,
    DateOnly IssuedOn, DateTimeOffset ReceivedAt, string ReportKind, Guid? RelatedReportId, string? RelationshipReason,
    string? LaboratoryConclusion, string? StatedStandard, string FileName, string ContentType, int FileSize,
    string Sha256, Guid RecordedBy, DateTimeOffset CapturedAt, DateTimeOffset RecordedAt, string Status,
    Guid? ConfirmedBy, DateTimeOffset? ConfirmedAt, IReadOnlyList<LabResultSetResponse> ResultSets,
    IReadOnlyList<LabRelatedReportResponse> RelatedReports, string AssessmentStatus, string SuggestedMarketCode,
    IReadOnlyList<string> AssessmentReasons, IReadOnlyList<LabReportResidueBinding>? ResidueBindings = null,
    Guid? ReplacementReportId = null);
public sealed record LabReportListResponse(IReadOnlyList<LabReportResponse> Items, int Page, int PageSize, bool HasMore);
internal sealed record VnMrlLimitRow(Guid SubstanceId, string SubstanceCode, string CommodityCode, decimal LimitValue, string Unit);
internal sealed record SubstanceCodeRow(Guid Id, string Code);

public static class LabReportEndpoints
{
    private const int MaxFileBytes = 10 * 1024 * 1024;
    private static readonly Regex DecimalPattern = new(@"\A[0-9]{1,12}(\.[0-9]{1,12})?\z", RegexOptions.CultureInvariant);
    private static readonly string[] AssessmentReasons = ["LEGAL_DATA_NOT_APPROVED", "INTERPRETATION_POLICY_NOT_APPROVED", "COVERAGE_POLICY_NOT_APPROVED"];
    public static IEndpointRouteBuilder MapLabReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/lab-reports").RequireAuthorization()
            .AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>().WithTags("Laboratory reports");
        group.MapPost("", CreateAsync).RequireAuthorization("lab.write")
            .WithMetadata(new RequestSizeLimitAttribute(20 * 1024 * 1024));
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization("lab.write")
            .WithMetadata(new RequestSizeLimitAttribute(20 * 1024 * 1024));
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("lab.write");
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapGet("/{id:guid}/file", FileAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmAsync).RequireAuthorization("lab.write");
        return endpoints;
    }

    private static Task<IResult> CreateAsync(CaptureLabReportRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct) =>
        SaveAsync(null, request, principal, access, identityDb, db, clock, ct);

    private static Task<IResult> UpdateAsync(Guid id, CaptureLabReportRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct) =>
        SaveAsync(id, request, principal, access, identityDb, db, clock, ct);

    private static async Task<IResult> SaveAsync(Guid? editingId, CaptureLabReportRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var company = await CompanyAsync(principal, access, identityDb, ct);
        if (company is null) return Results.Forbid();
        var errors = Envelope(request.DeviceId, request.ClientOperationId, request.CapturedAt, request.PayloadSchemaVersion);
        var p = request.Payload;
        var originalPayload = p;
        LabReport? retainedFile = null;
        if (editingId is { } currentId && p?.ContentBase64 is null)
        {
            retainedFile = await db.LabReports.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == currentId, ct);
            if (retainedFile is null) return Problem(404, "LAB_REPORT_NOT_FOUND", "Report not found.");
            if (p is not null) p = p with { FileName = retainedFile.FileName, ContentType = retainedFile.ContentType };
        }
        byte[] bytes = [];
        if (p is null) errors["payload"] = ["Report data is required."];
        else
        {
            if (p.EntityId == Guid.Empty) errors["payload.entityId"] = ["Report ID is required."];
            if (editingId is { } routeId && p.EntityId != routeId) errors["payload.entityId"] = ["Report ID must match the route."];
            Required(errors, "laboratoryName", p.LaboratoryName, 200);
            Required(errors, "reportNumber", p.ReportNumber, 200);
            Required(errors, "fileName", p.FileName, 255);
            Required(errors, "contentType", p.ContentType, 200);
            if (p.FileName is { } name && (name.Any(char.IsControl) || name.Contains('/') || name.Contains('\\')))
                errors["payload.fileName"] = ["Use a file name without paths or control characters."];
            if (p.ContentType is { } type && (type.Any(char.IsControl) || !Regex.IsMatch(type, @"\A[A-Za-z0-9!#$&^_.+-]+/[A-Za-z0-9!#$&^_.+-]+\z")))
                errors["payload.contentType"] = ["A valid media type is required."];
            if (p.IssuedOn == default) errors["payload.issuedOn"] = ["Report issue date is required."];
            if (p.ReceivedAt == default || p.ReceivedAt > request.CapturedAt)
                errors["payload.receivedAt"] = ["Receipt time must exist and must not follow capture time."];
            if (p.ReportKind is not ("PARTIAL" or "FINAL" or "SUPPLEMENTARY" or "AMENDMENT" or "RETEST"))
                errors["payload.reportKind"] = ["Unsupported report kind."];
            if (!p.CompanyOnlyConfirmed) errors["payload.companyOnlyConfirmed"] = ["Confirm that the entire original contains this company's data only. Split or redact mixed-company reports before upload."];
            if (p.ReportKind is "SUPPLEMENTARY" or "AMENDMENT" or "RETEST" && p.RelatedReportId is null)
                errors["payload.relatedReportId"] = ["A related report is required for this report kind."];
            if (p.RelatedReportId is not null && string.IsNullOrWhiteSpace(p.RelationshipReason))
                errors["payload.relationshipReason"] = ["A relationship reason is required."];
            if (p.ContentBase64 is null && retainedFile is not null) bytes = retainedFile.Content;
            else if (p.ContentBase64 is null || p.ContentBase64.Length > ((MaxFileBytes + 2) / 3) * 4)
                errors["payload.contentBase64"] = ["An original file of 1 byte to 10 MiB is required."];
            else
            {
                try { bytes = Convert.FromBase64String(p.ContentBase64); }
                catch (FormatException) { errors["payload.contentBase64"] = ["Invalid base64 file."]; }
                if (bytes.Length is 0 or > MaxFileBytes) errors["payload.contentBase64"] = ["An original file of 1 byte to 10 MiB is required."];
            }
            if (p.ResultSets is not { Count: > 0 and <= 1000 }) errors["payload.resultSets"] = ["Supply 1 to 1000 sample-specific result sets."];
            else
            {
                var ids = new HashSet<Guid>();
                var count = 0;
                for (var i = 0; i < p.ResultSets.Count; i++)
                {
                    var set = p.ResultSets[i];
                    if (set is null) { errors[$"payload.resultSets[{i}]"] = ["Result set is required."]; continue; }
                    if (set.EntityId == Guid.Empty || !ids.Add(set.EntityId)) errors[$"payload.resultSets[{i}].entityId"] = ["Result set IDs must be nonempty and unique."];
                    if (set.SampleId == Guid.Empty) errors[$"payload.resultSets[{i}].sampleId"] = ["Sample ID must be nonempty when supplied."];
                    if (set.Results is not { Count: > 0 }) errors[$"payload.resultSets[{i}].results"] = ["At least one result is required."];
                    else for (var j = 0; j < set.Results.Count; j++)
                    {
                        count++;
                        var row = set.Results[j];
                        var key = $"payload.resultSets[{i}].results[{j}]";
                        if (row is null) { errors[key] = ["Result is required."]; continue; }
                        if (string.IsNullOrWhiteSpace(row.TestLabel) || string.IsNullOrWhiteSpace(row.OriginalResultText)) errors[key] = ["Test label and original result text are required."];
                        if (row.ResultKind is not ("NUMERIC" or "BELOW_LIMIT" or "NOT_DETECTED" or "QUALITATIVE" or "OTHER")) errors[key + ".resultKind"] = ["Unsupported result kind."];
                        if (row.ResultKind is "NUMERIC" or "BELOW_LIMIT" && row.NumericValue is null) errors[key + ".numericValue"] = ["Exact numeric value or reporting threshold is required."];
                        Decimal(errors, key + ".numericValue", row.NumericValue);
                        Decimal(errors, key + ".lod", row.Lod);
                        Decimal(errors, key + ".loq", row.Loq);
                    }
                }
                if (count > 10000) errors["payload.resultSets"] = ["At most 10000 result rows are supported per report."];
            }
        }
        if (errors.Count != 0 || p?.ResultSets is not { } resultSets) return Results.ValidationProblem(errors);
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var fileHash = retainedFile?.Sha256 ?? Convert.ToHexString(SHA256.HashData(bytes));
        var eventType = editingId is null ? "LAB_REPORT_RECEIVED" : "LAB_REPORT_REVISED";
        var canonical = editingId is null
            ? JsonSerializer.Serialize(new { operationType = eventType, request.CapturedAt, request.PayloadSchemaVersion,
                payload = originalPayload! with { ContentBase64 = null }, fileHash })
            : JsonSerializer.Serialize(new { operationType = eventType, reportId = editingId, request.CapturedAt, request.PayloadSchemaVersion,
                payload = originalPayload! with { ContentBase64 = null }, fileHash });
        var hash = Hash(canonical);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor, "lab.write", company.Value, ct: ct)) return Results.Forbid();
        await LockOperationAsync(db, company.Value, request.DeviceId!.Trim(), request.ClientOperationId, ct);
        var prior = await PriorAsync(db, company.Value, request.DeviceId.Trim(), request.ClientOperationId, ct);
        if (prior is not null) return Replay(prior, hash);
        if (editingId is { } oldId)
        {
            if (!await db.LabReports.AnyAsync(x => x.CompanyId == company && x.Id == oldId, ct)) return Problem(404, "LAB_REPORT_NOT_FOUND", "Report not found.");
            if (await db.LabReportConfirmations.AnyAsync(x => x.CompanyId == company && x.ReportId == oldId, ct))
                return Problem(409, "REPORT_ALREADY_CONFIRMED", "Confirmed evidence is immutable. Append a linked report to correct it.");
            if (await db.LabReportRetirements.AnyAsync(x => x.CompanyId == company && x.ReportId == oldId, ct))
                return Problem(409, "LAB_REPORT_RETIRED", "This draft was deleted or superseded. Reload the current report.");
            p = p! with { EntityId = Guid.NewGuid(), ResultSets = resultSets.Select(s => s with { EntityId = Guid.NewGuid() }).ToArray() };
            resultSets = p.ResultSets!;
        }
        var resultSetIds = resultSets.Select(s => s.EntityId).ToArray();
        if (await db.LabReports.AnyAsync(x => x.Id == p!.EntityId, ct) || await db.LabResultSets.AnyAsync(x => resultSetIds.Contains(x.Id), ct))
            return Problem(409, "ENTITY_ALREADY_EXISTS", "The report or result set ID already exists.");
        if (p!.RelatedReportId is { } related && (!await db.LabReports.AnyAsync(x => x.CompanyId == company && x.Id == related, ct) ||
            await db.LabReportRetirements.AnyAsync(x => x.CompanyId == company && x.ReportId == related, ct) || related == editingId))
            return Problem(409, "RELATED_REPORT_NOT_FOUND", "Related report is not active in this company.");
        var sampleIds = resultSets.Where(x => x.SampleId.HasValue).Select(x => x.SampleId!.Value).Distinct().ToArray();
        if (await db.Samples.CountAsync(x => x.CompanyId == company && sampleIds.Contains(x.Id), ct) != sampleIds.Length)
            return Problem(409, "SAMPLE_NOT_FOUND", "Every proposed sample must belong to this company.");
        var rawSubstances = resultSets.SelectMany(x => x.Results!)
            .Where(x => !string.IsNullOrWhiteSpace(x.SubstanceId))
            .Select(x => x.SubstanceId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolvedSubstances = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (rawSubstances.Length > 0)
        {
            var guids = rawSubstances.Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse).Distinct().ToArray();
            var codes = rawSubstances.Where(x => !Guid.TryParse(x, out _)).Select(x => x.ToUpperInvariant()).Distinct().ToArray();
            if (guids.Length > 0)
            {
                var byId = await db.Database.SqlQuery<SubstanceCodeRow>(
                    $"SELECT id AS \"Id\", code AS \"Code\" FROM catalog.active_substances WHERE id = ANY({guids})").ToListAsync(ct);
                foreach (var item in byId) resolvedSubstances[item.Id.ToString()] = item.Id;
            }
            if (codes.Length > 0)
            {
                var byCode = await db.Database.SqlQuery<SubstanceCodeRow>(
                    $"SELECT id AS \"Id\", code AS \"Code\" FROM catalog.active_substances WHERE UPPER(code) = ANY({codes})").ToListAsync(ct);
                foreach (var item in byCode) resolvedSubstances[item.Code] = item.Id;
            }
            if (rawSubstances.Any(raw => !resolvedSubstances.ContainsKey(raw)))
                return Problem(409, "SUBSTANCE_NOT_FOUND", "A supplied catalog substance does not exist.");
        }
        var now = clock.GetUtcNow();
        db.LabReports.Add(new LabReport { Id = p.EntityId, CompanyId = company.Value, LaboratoryName = p.LaboratoryName!, ReportNumber = p.ReportNumber!,
            IssuedOn = p.IssuedOn, ReceivedAt = p.ReceivedAt.ToUniversalTime(), ReportKind = p.ReportKind!, RelatedReportId = p.RelatedReportId,
            RelationshipReason = p.RelationshipReason, LaboratoryConclusion = p.LaboratoryConclusion, StatedStandard = p.StatedStandard,
            FileName = p.FileName!, ContentType = p.ContentType!, FileSize = bytes.Length, Sha256 = fileHash, Content = bytes,
            CompanyOnlyConfirmed = true, RecordedBy = actor, CapturedAt = request.CapturedAt.ToUniversalTime(), RecordedAt = now });
        for (var i = 0; i < resultSets.Count; i++)
        {
            var set = resultSets[i];
            db.LabResultSets.Add(new LabResultSet { Id = set.EntityId, CompanyId = company.Value, ReportId = p.EntityId, Position = i,
                SampleId = set.SampleId, LaboratorySampleReference = set.LaboratorySampleReference?.Trim(), StatedSampleCode = set.StatedSampleCode?.Trim(),
                StatedSealCode = set.StatedSealCode?.Trim(), SectionReference = set.SectionReference?.Trim() });
            for (var j = 0; j < set.Results!.Count; j++)
            {
                var row = set.Results[j];
                Guid? resolvedSubstanceId = !string.IsNullOrWhiteSpace(row.SubstanceId)
                    ? resolvedSubstances[row.SubstanceId.Trim()]
                    : null;
                db.LabResults.Add(new LabResult { Id = Guid.NewGuid(), CompanyId = company.Value, ResultSetId = set.EntityId, Position = j,
                    TestLabel = row.TestLabel!, SubstanceId = resolvedSubstanceId, OriginalResultText = row.OriginalResultText!, ResultKind = row.ResultKind!,
                    NumericValue = row.NumericValue, Unit = row.Unit, Lod = row.Lod, Loq = row.Loq, Method = row.Method, Uncertainty = row.Uncertainty, Reference = row.Reference });
            }
        }
        try
        {
            await db.SaveChangesAsync(ct);
            if (editingId is { } retiredId)
            {
                db.LabReportRetirements.Add(new LabReportRetirement { ReportId = retiredId, CompanyId = company.Value,
                    ReplacementReportId = p.EntityId, Status = "SUPERSEDED", RecordedBy = actor, RecordedAt = now });
                await db.SaveChangesAsync(ct);
            }
            var response = (await ResponsesAsync(db, company.Value, [p.EntityId], ct))[0];
            await AddOperationAsync(db, company.Value, actor, request.DeviceId.Trim(), request.ClientOperationId, request.CapturedAt, now, eventType, canonical, hash, response, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return editingId is null ? Results.Created($"/api/lab-reports/{p.EntityId}", response) : Results.Ok(response);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Problem(409, "ENTITY_ALREADY_EXISTS", "The report or result set ID already exists."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        { return Problem(409, "REFERENCE_CHANGED", "A referenced record changed. Reload before retrying."); }
    }

    private static async Task<IResult> DeleteAsync(Guid id, [FromBody] DeleteLabReportRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var company = await CompanyAsync(principal, access, identityDb, ct);
        if (company is null) return Results.Forbid();
        var errors = Envelope(request.DeviceId, request.ClientOperationId, request.CapturedAt, request.PayloadSchemaVersion);
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var canonical = JsonSerializer.Serialize(new { operationType = "LAB_REPORT_DELETED", reportId = id,
            request.CapturedAt, request.PayloadSchemaVersion });
        var hash = Hash(canonical);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor, "lab.write", company.Value, ct: ct)) return Results.Forbid();
        await LockOperationAsync(db, company.Value, request.DeviceId!.Trim(), request.ClientOperationId, ct);
        var prior = await PriorAsync(db, company.Value, request.DeviceId.Trim(), request.ClientOperationId, ct);
        if (prior is not null) return Replay(prior, hash);
        if (!await db.LabReports.AnyAsync(x => x.CompanyId == company && x.Id == id, ct)) return Problem(404, "LAB_REPORT_NOT_FOUND", "Report not found.");
        if (await db.LabReportConfirmations.AnyAsync(x => x.CompanyId == company && x.ReportId == id, ct))
            return Problem(409, "REPORT_ALREADY_CONFIRMED", "Confirmed evidence is immutable. Append a linked report to correct it.");
        if (await db.LabReportRetirements.AnyAsync(x => x.CompanyId == company && x.ReportId == id, ct))
            return Problem(409, "LAB_REPORT_RETIRED", "This draft was deleted or superseded. Reload the current report.");
        if (await db.LabReports.AnyAsync(x => x.CompanyId == company && x.RelatedReportId == id, ct))
            return Problem(409, "LAB_REPORT_REFERENCED", "Another report references this draft. Its source evidence must remain available.");
        var now = clock.GetUtcNow();
        db.LabReportRetirements.Add(new LabReportRetirement { ReportId = id, CompanyId = company.Value,
            Status = "DELETED", RecordedBy = actor, RecordedAt = now });
        await db.SaveChangesAsync(ct);
        var response = (await ResponsesAsync(db, company.Value, [id], ct))[0];
        await AddOperationAsync(db, company.Value, actor, request.DeviceId.Trim(), request.ClientOperationId,
            request.CapturedAt, now, "LAB_REPORT_DELETED", canonical, hash, response, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmAsync(Guid id, ConfirmLabReportRequest request, ClaimsPrincipal principal,
        ICurrentAccess access, IdentityDbContext identityDb, CaptureDbContext db, TimeProvider clock,
        IConfirmedLabReportResidueBinder residueBinder, CancellationToken ct)
    {
        var company = await CompanyAsync(principal, access, identityDb, ct);
        if (company is null) return Results.Forbid();
        var errors = Envelope(request.DeviceId, request.ClientOperationId, request.CapturedAt, request.PayloadSchemaVersion);
        var p = request.Payload;
        if (p is null || !p.IdentityChecked || !p.TranscriptionChecked) errors["payload"] = ["Identity and transcription must both be explicitly checked."];
        if (p?.Matches is not { Count: > 0 and <= 1000 } || p.Matches.Any(x => x is null || x.ResultSetId == Guid.Empty || x.SampleId == Guid.Empty || x.IdentityBasis is not ("SAMPLE_CODE" or "SEAL_CODE" or "LAB_REFERENCE")))
            errors["payload.matches"] = ["Supply a sample and supported identity basis for every result set."];
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        var actor = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var canonical = JsonSerializer.Serialize(new { operationType = "LAB_REPORT_CONFIRMED", reportId = id, request.CapturedAt, request.PayloadSchemaVersion, payload = p });
        var hash = Hash(canonical);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928352)", ct);
        if (!await CropSeasonEndpoints.HasPermissionInTransactionAsync(db, actor, "lab.write", company.Value, ct: ct)) return Results.Forbid();
        await LockOperationAsync(db, company.Value, request.DeviceId!.Trim(), request.ClientOperationId, ct);
        var prior = await PriorAsync(db, company.Value, request.DeviceId.Trim(), request.ClientOperationId, ct);
        if (prior is not null) return Replay(prior, hash);
        var report = await db.LabReports.AsNoTracking().Where(x => x.CompanyId == company && x.Id == id)
            .Select(x => new { x.LaboratoryName }).SingleOrDefaultAsync(ct);
        if (report is null) return Problem(404, "LAB_REPORT_NOT_FOUND", "Report not found.");
        if (await db.LabReportConfirmations.AnyAsync(x => x.CompanyId == company && x.ReportId == id, ct)) return Problem(409, "REPORT_ALREADY_CONFIRMED", "Confirmed evidence is immutable. Append a linked report to correct it.");
        if (await db.LabReportRetirements.AnyAsync(x => x.CompanyId == company && x.ReportId == id, ct))
            return Problem(409, "LAB_REPORT_RETIRED", "This draft was deleted or superseded. Reload the current report.");
        var sets = await db.LabResultSets.AsNoTracking().Where(x => x.CompanyId == company && x.ReportId == id).ToListAsync(ct);
        if (p!.Matches!.Count != sets.Count || p.Matches.Select(x => x.ResultSetId).Distinct().Count() != sets.Count || p.Matches.Any(x => sets.All(s => s.Id != x.ResultSetId)))
            return Problem(409, "REPORT_IDENTITY_UNRESOLVED", "Confirm every result set exactly once.");
        var ids = p.Matches.Select(x => x.SampleId).Distinct().ToArray();
        var samples = await db.Samples.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (samples.Count != ids.Length) return Problem(409, "SAMPLE_NOT_FOUND", "Every confirmed sample must belong to this company.");
        var handovers = await db.SampleHandovers.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.SampleId) &&
            (x.HandoverKind == "LAB_DISPATCH" || x.HandoverKind == "LAB_ACKNOWLEDGEMENT")).ToListAsync(ct);
        foreach (var match in p.Matches)
        {
            var set = sets.Single(x => x.Id == match.ResultSetId);
            var sample = samples[match.SampleId];
            if (Contradicts(set, sample)) return Problem(409, "REPORT_IDENTITY_UNRESOLVED", "Printed sample or seal contradicts selected sample. Preserve this evidence and append a corrected report.");
            var valid = match.IdentityBasis switch
            {
                "SAMPLE_CODE" => !string.IsNullOrWhiteSpace(set.StatedSampleCode) && set.StatedSampleCode.Trim() == sample.Code.Trim(),
                "SEAL_CODE" => !string.IsNullOrWhiteSpace(set.StatedSealCode) && set.StatedSealCode.Trim() == sample.SealCode.Trim(),
                "LAB_REFERENCE" => !string.IsNullOrWhiteSpace(set.LaboratorySampleReference) && !string.IsNullOrWhiteSpace(match.EvidenceRef) &&
                    handovers.Any(h => h.SampleId == sample.Id && h.LaboratoryName == report.LaboratoryName && h.LaboratorySampleReference?.Trim() == set.LaboratorySampleReference.Trim() &&
                        !string.IsNullOrWhiteSpace(h.EvidenceRef) && h.EvidenceRef.Trim() == match.EvidenceRef.Trim()),
                _ => false
            };
            if (!valid) return Problem(409, "REPORT_IDENTITY_UNRESOLVED", "Printed identity or independently evidenced laboratory reference must establish sample identity.");
        }
        // Confirmation and automatic residue binding share this transaction.
        // Use PostgreSQL time so the binding read cannot see the new
        // confirmation as a future event when API and DB clocks differ.
        var now = await db.Database.SqlQueryRaw<DateTimeOffset>(
            "SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var confirmation = new LabReportConfirmation { Id = Guid.NewGuid(), CompanyId = company.Value, ReportId = id, ConfirmedBy = actor, ConfirmedAt = now, IdentityChecked = true, TranscriptionChecked = true };
        db.LabReportConfirmations.Add(confirmation);
        foreach (var match in p.Matches)
            db.LabConfirmedAssociations.Add(new LabConfirmedAssociation { Id = Guid.NewGuid(), CompanyId = company.Value, ConfirmationId = confirmation.Id,
                ResultSetId = match.ResultSetId, SampleId = match.SampleId, IdentityBasis = match.IdentityBasis!, EvidenceRef = match.EvidenceRef });
        await db.SaveChangesAsync(ct);
        var bindings = await residueBinder.BindConfirmedReportAsync((NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction(), actor, company.Value, id, now, ct);
        var response = (await ResponsesAsync(db, company.Value, [id], ct))[0] with { ResidueBindings = bindings };
        await AddOperationAsync(db, company.Value, actor, request.DeviceId.Trim(), request.ClientOperationId, request.CapturedAt, now, "LAB_REPORT_CONFIRMED", canonical, hash, response, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> ListAsync(HttpContext http, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var company = await CompanyAsync(principal, access, identityDb, ct);
        if (company is null) return Results.Forbid();
        var page = 1; var size = 20;
        if (!Page(http, "page", ref page, 1000000) || !Page(http, "pageSize", ref size, 100)) return QueryError();
        Guid? sample = null; Guid? source = null;
        if (!Filter(http, "sampleId", ref sample) || !Filter(http, "sourceLotId", ref source)) return QueryError();
        var query = db.LabReports.AsNoTracking().Where(x => x.CompanyId == company &&
            !db.LabReportRetirements.Any(r => r.CompanyId == company && r.ReportId == x.Id));
        if (sample is not null) query = query.Where(r => db.LabResultSets.Any(s => s.CompanyId == company && s.ReportId == r.Id && (s.SampleId == sample || db.LabConfirmedAssociations.Any(a => a.CompanyId == company && a.ResultSetId == s.Id && a.SampleId == sample))));
        if (source is not null) query = query.Where(r => db.LabResultSets.Any(s => s.CompanyId == company && s.ReportId == r.Id && db.LabConfirmedAssociations.Any(a => a.CompanyId == company && a.ResultSetId == s.Id && db.Samples.Any(p => p.CompanyId == company && p.Id == a.SampleId && p.SourceLotId == source))));
        var ids = await query.OrderByDescending(x => x.RecordedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size + 1).Select(x => x.Id).ToArrayAsync(ct);
        return Results.Ok(new LabReportListResponse(await ResponsesAsync(db, company.Value, ids.Take(size).ToArray(), ct), page, size, ids.Length > size));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var company = await CompanyAsync(principal, access, identityDb, ct);
        if (company is null) return Results.Forbid();
        var responses = await ResponsesAsync(db, company.Value, [id], ct);
        return responses.Count == 0 ? Problem(404, "LAB_REPORT_NOT_FOUND", "Report not found.") : Results.Ok(responses[0]);
    }

    private static async Task<IResult> FileAsync(Guid id, HttpContext http, ClaimsPrincipal principal, ICurrentAccess access,
        IdentityDbContext identityDb, CaptureDbContext db, CancellationToken ct)
    {
        if (!await CanReadAsync(access, ct)) return Results.Forbid();
        var company = await CompanyAsync(principal, access, identityDb, ct);
        if (company is null) return Results.Forbid();
        var file = await db.LabReports.AsNoTracking().Where(x => x.CompanyId == company && x.Id == id)
            .Select(x => new { x.Content, x.FileName }).SingleOrDefaultAsync(ct);
        if (file is null) return Problem(404, "LAB_REPORT_NOT_FOUND", "Report not found.");
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        return Results.File(file.Content, "application/octet-stream", file.FileName);
    }

    private static async Task<IReadOnlyList<LabReportResponse>> ResponsesAsync(CaptureDbContext db, Guid company, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        // Never materialize report bytes for metadata/list requests.
        var reports = await db.LabReports.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.CompanyId, x.LaboratoryName, x.ReportNumber, x.IssuedOn, x.ReceivedAt, x.ReportKind, x.RelatedReportId,
                x.RelationshipReason, x.LaboratoryConclusion, x.StatedStandard, x.FileName, x.ContentType, x.FileSize, x.Sha256, x.RecordedBy, x.CapturedAt, x.RecordedAt }).ToDictionaryAsync(x => x.Id, ct);
        var sets = await db.LabResultSets.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.ReportId)).OrderBy(x => x.Position).ToListAsync(ct);
        var setIds = sets.Select(x => x.Id).ToArray();
        var rows = await db.LabResults.AsNoTracking().Where(x => x.CompanyId == company && setIds.Contains(x.ResultSetId)).OrderBy(x => x.Position).ToListAsync(ct);
        var confirmations = await db.LabReportConfirmations.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.ReportId)).ToDictionaryAsync(x => x.ReportId, ct);
        var retirements = await db.LabReportRetirements.AsNoTracking().Where(x => x.CompanyId == company && ids.Contains(x.ReportId)).ToDictionaryAsync(x => x.ReportId, ct);
        var associations = await db.LabConfirmedAssociations.AsNoTracking().Where(x => x.CompanyId == company && setIds.Contains(x.ResultSetId)).ToDictionaryAsync(x => x.ResultSetId, ct);
        var sampleIds = sets.Where(x => x.SampleId != null).Select(x => x.SampleId!.Value).Concat(associations.Values.Select(x => x.SampleId)).Distinct().ToArray();
        var samples = await db.Samples.AsNoTracking().Where(x => x.CompanyId == company && sampleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var related = await db.LabReports.AsNoTracking().Where(x => x.CompanyId == company && x.RelatedReportId != null && ids.Contains(x.RelatedReportId.Value) &&
            !db.LabReportRetirements.Any(r => r.CompanyId == company && r.ReportId == x.Id))
            .Select(x => new { ParentId = x.RelatedReportId!.Value, x.Id, x.ReportKind, x.RelationshipReason }).ToListAsync(ct);
        var mappedIds = rows.Where(x => x.SubstanceId.HasValue).Select(x => x.SubstanceId!.Value).Distinct().ToArray();
        var substanceCodes = mappedIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await db.Database.SqlQuery<SubstanceCodeRow>($"SELECT id AS \"Id\", code AS \"Code\" FROM catalog.active_substances WHERE id = ANY({mappedIds})").ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Code);
        var vnLimits = await db.Database.SqlQueryRaw<VnMrlLimitRow>(
            """
            SELECT DISTINCT ON (l.substance_id, c.code)
                l.substance_id AS "SubstanceId",
                s.code AS "SubstanceCode",
                c.code AS "CommodityCode",
                l.limit_value AS "LimitValue",
                l.unit AS "Unit"
            FROM catalog.mrl_limits l
            JOIN catalog.mrl_versions v ON v.id = l.mrl_version_id
            JOIN catalog.markets m ON m.id = v.market_id
            JOIN catalog.commodities c ON c.id = v.commodity_id
            JOIN catalog.active_substances s ON s.id = l.substance_id
            WHERE UPPER(m.code) = 'VN' AND v.status = 'PUBLISHED' AND NOT c.is_archived
            ORDER BY l.substance_id, c.code, v.version_no DESC
            """).ToListAsync(ct);
        var hasPublishedVnTable = vnLimits.Count > 0;
        var vnLimitsBySubstance = vnLimits.ToLookup(x => x.SubstanceId);
        var rowLookup = rows.ToLookup(x => x.ResultSetId);
        var setLookup = sets.ToLookup(x => x.ReportId);
        var relatedLookup = related.ToLookup(x => x.ParentId);
        return ids.Where(reports.ContainsKey).Select(id =>
        {
            var r = reports[id];
            confirmations.TryGetValue(id, out var confirmation);
            retirements.TryGetValue(id, out var retirement);
            var reportRows = setLookup[id].SelectMany(s => rowLookup[s.Id]).ToArray();
            var (assessmentStatus, assessmentReasons) = AssessVn(confirmation is not null, reportRows, substanceCodes, vnLimitsBySubstance, hasPublishedVnTable);
            var resultSets = setLookup[id].Select(s =>
            {
                associations.TryGetValue(s.Id, out var association);
                var matched = s.SampleId is { } proposed && samples.TryGetValue(proposed, out var sample) && PrintedMatch(s, sample);
                return new LabResultSetResponse(s.Id, s.SampleId, association?.SampleId,
                    association is not null && samples.TryGetValue(association.SampleId, out var bound) ? bound.SourceLotId : null,
                    s.LaboratorySampleReference, s.StatedSampleCode, s.StatedSealCode, s.SectionReference,
                    association is not null ? "CONFIRMED" : matched ? "MATCHED" : "UNRESOLVED",
                    rowLookup[s.Id].Select(x => new LabResultResponse(x.Id, x.TestLabel, x.SubstanceId, x.OriginalResultText, x.ResultKind,
                        x.NumericValue, x.Unit, x.Lod, x.Loq, x.Method, x.Uncertainty, x.Reference)).ToArray());
            }).ToArray();
            return new LabReportResponse(r.Id, r.CompanyId, r.LaboratoryName, r.ReportNumber, r.IssuedOn, r.ReceivedAt, r.ReportKind,
                r.RelatedReportId, r.RelationshipReason, r.LaboratoryConclusion, r.StatedStandard, r.FileName, r.ContentType, r.FileSize,
                r.Sha256, r.RecordedBy, r.CapturedAt, r.RecordedAt, retirement?.Status ?? (confirmation is not null ? "CONFIRMED" : resultSets.All(x => x.IdentityStatus == "MATCHED") ? "PENDING_REVIEW" : "UNRESOLVED"),
                confirmation?.ConfirmedBy, confirmation?.ConfirmedAt, resultSets, relatedLookup[id].Select(x => new LabRelatedReportResponse(x.Id, x.ReportKind, x.RelationshipReason)).ToArray(),
                assessmentStatus, "VN", assessmentReasons, ReplacementReportId: retirement?.ReplacementReportId);
        }).ToArray();
    }

    private static (string Status, IReadOnlyList<string> Reasons) AssessVn(
        bool confirmed,
        IReadOnlyList<LabResult> rows,
        IReadOnlyDictionary<Guid, string> substanceCodes,
        ILookup<Guid, VnMrlLimitRow> vnLimitsBySubstance,
        bool hasPublishedVnTable)
    {
        if (!confirmed || !hasPublishedVnTable || rows.All(x => x.SubstanceId is null))
            return ("NOT_ASSESSED", AssessmentReasons);
        var anyExceeds = false;
        var anyInsufficient = false;
        var anyWithin = false;
        var reasons = new List<string>();
        foreach (var row in rows)
        {
            if (row.SubstanceId is null)
            {
                anyInsufficient = true;
                reasons.Add($"{row.TestLabel}: UNMAPPED_SUBSTANCE");
                continue;
            }
            var subId = row.SubstanceId.Value;
            var code = substanceCodes.GetValueOrDefault(subId, subId.ToString());
            var limits = vnLimitsBySubstance[subId].ToList();
            if (limits.Count == 0)
            {
                reasons.Add($"{code}: NO_MRL_IN_VN (no numeric limit in VN table)");
                continue;
            }
            foreach (var lim in limits)
            {
                if (row.ResultKind == "NUMERIC" &&
                    string.Equals(row.Unit, "mg/kg", StringComparison.OrdinalIgnoreCase) &&
                    decimal.TryParse(row.NumericValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var val))
                {
                    if (val > lim.LimitValue)
                    {
                        anyExceeds = true;
                        reasons.Add($"{code}: EXCEEDS_LIMIT ({row.NumericValue} mg/kg > VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                    }
                    else
                    {
                        anyWithin = true;
                        reasons.Add($"{code}: WITHIN_LIMIT ({row.NumericValue} mg/kg <= VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                    }
                }
                else if (row.ResultKind == "BELOW_LIMIT" &&
                    string.Equals(row.Unit, "mg/kg", StringComparison.OrdinalIgnoreCase) &&
                    decimal.TryParse(row.NumericValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var threshold))
                {
                    if (threshold <= lim.LimitValue)
                    {
                        anyWithin = true;
                        reasons.Add($"{code}: WITHIN_LIMIT (<{row.NumericValue} mg/kg <= VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                    }
                    else
                    {
                        anyInsufficient = true;
                        reasons.Add($"{code}: INSUFFICIENT_EVIDENCE (reporting limit <{row.NumericValue} mg/kg > VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                    }
                }
                else if (row.ResultKind == "NOT_DETECTED")
                {
                    var boundText = !string.IsNullOrWhiteSpace(row.Loq) ? row.Loq : row.Lod;
                    if ((string.IsNullOrWhiteSpace(row.Unit) || string.Equals(row.Unit, "mg/kg", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrWhiteSpace(boundText) &&
                        decimal.TryParse(boundText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var bound))
                    {
                        if (bound <= lim.LimitValue)
                        {
                            anyWithin = true;
                            reasons.Add($"{code}: WITHIN_LIMIT (ND with LOQ/LOD {boundText} mg/kg <= VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                        }
                        else
                        {
                            anyInsufficient = true;
                            reasons.Add($"{code}: INSUFFICIENT_EVIDENCE (ND with LOQ/LOD {boundText} mg/kg > VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                        }
                    }
                    else
                    {
                        anyInsufficient = true;
                        reasons.Add($"{code}: INSUFFICIENT_EVIDENCE (ND requires LOQ/LOD in mg/kg <= VN {lim.CommodityCode} {lim.LimitValue} mg/kg)");
                    }
                }
                else
                {
                    anyInsufficient = true;
                    reasons.Add($"{code}: INSUFFICIENT_EVIDENCE (unsupported qualifier {row.ResultKind} or unit {row.Unit ?? "none"})");
                }
            }
        }
        var status = anyExceeds
            ? "EXCEEDS_LIMIT"
            : anyInsufficient
                ? "INSUFFICIENT_EVIDENCE"
                : anyWithin
                    ? "WITHIN_ASSESSED_LIMITS"
                    : "NO_MRL_IN_VN";
        return (status, reasons);
    }

    private static bool Contradicts(LabResultSet set, Sample sample) =>
        !string.IsNullOrWhiteSpace(set.StatedSampleCode) && set.StatedSampleCode.Trim() != sample.Code.Trim() ||
        !string.IsNullOrWhiteSpace(set.StatedSealCode) && set.StatedSealCode.Trim() != sample.SealCode.Trim();
    private static bool PrintedMatch(LabResultSet set, Sample sample) => !Contradicts(set, sample) &&
        (!string.IsNullOrWhiteSpace(set.StatedSampleCode) && set.StatedSampleCode.Trim() == sample.Code.Trim() || !string.IsNullOrWhiteSpace(set.StatedSealCode) && set.StatedSealCode.Trim() == sample.SealCode.Trim());
    private static Dictionary<string, string[]> Envelope(string? device, Guid operation, DateTimeOffset at, int version)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(device) || device.Trim().Length > 100) errors["deviceId"] = ["Device ID is required and must not exceed 100 characters."];
        if (operation == Guid.Empty) errors["clientOperationId"] = ["Operation ID is required."];
        if (at == default) errors["capturedAt"] = ["Capture time is required."];
        if (version != 1) errors["payloadSchemaVersion"] = ["Unsupported payload version."];
        return errors;
    }
    private static void Required(Dictionary<string, string[]> errors, string field, string? value, int max)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > max) errors["payload." + field] = [$"Required; maximum {max} characters."]; }
    private static async Task<Guid?> CompanyAsync(ClaimsPrincipal principal, ICurrentAccess access, IdentityDbContext db, CancellationToken ct)
    {
        var company = await CaptureActor.GetCustomerCompanyAsync(principal, db, ct);
        return company is not null && await access.CanAccessOrganizationAsync(company.Value, ct) ? company : null;
    }
    private static async Task<bool> CanReadAsync(ICurrentAccess access, CancellationToken ct) => await access.HasPermissionAsync("lab.read", ct) || await access.HasPermissionAsync("lab.write", ct);
    private static Task<int> LockOperationAsync(CaptureDbContext db, Guid company, string device, Guid operation, CancellationToken ct)
    {
        var key = $"{company:N}:{device}:{operation:N}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }
    private static Task<CaptureOperation?> PriorAsync(CaptureDbContext db, Guid company, string device, Guid operation, CancellationToken ct) =>
        db.OfflineOperations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.DeviceId == device && x.ClientOperationId == operation, ct);
    private static IResult Replay(CaptureOperation prior, string hash) => prior.PayloadHash != hash
        ? Problem(409, "OPERATION_CONFLICT", "The operation ID has already been used with different content.")
        : prior.Status == "APPLIED" && prior.ResultPayload is not null
            ? JsonSerializer.Deserialize<JsonElement>(prior.Payload).GetProperty("operationType").GetString() == "LAB_REPORT_DELETED"
                ? Results.NoContent() : Results.Ok(JsonSerializer.Deserialize<LabReportResponse>(prior.ResultPayload))
        : Problem(409, "OPERATION_NOT_APPLIED", "This operation has no accepted result.");
    private static async Task AddOperationAsync(CaptureDbContext db, Guid company, Guid actor, string device, Guid operation,
        DateTimeOffset captured, DateTimeOffset now, string eventType, string canonical, string hash, LabReportResponse response, CancellationToken ct)
    {
        var ev = new CaptureBusinessEvent { CompanyId = company, ActorId = actor, EventType = eventType, OccurredAt = captured.ToUniversalTime(), RecordedAt = now,
            CorrelationId = operation, Payload = JsonSerializer.Serialize(new { schemaVersion = 1, reportId = response.Id, response }) };
        db.BusinessEvents.Add(ev);
        await db.SaveChangesAsync(ct);
        db.OfflineOperations.Add(new CaptureOperation { CompanyId = company, SubmittedBy = actor, DeviceId = device, ClientOperationId = operation,
            Payload = canonical, PayloadHash = hash, CapturedAt = captured.ToUniversalTime(), ReceivedAt = now, Status = "APPLIED", AppliedEventId = ev.Id,
            ResultPayload = JsonSerializer.Serialize(response) });
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Decimal(Dictionary<string, string[]> errors, string key, string? value)
    {
        if (value is not null && !DecimalPattern.IsMatch(value))
            errors[key] = ["Use an exact nonnegative decimal string with 1–12 integer digits and at most 12 fractional digits; no signs, whitespace, exponents or rounding."];
    }
    private static bool Page(HttpContext http, string name, ref int value, int max)
    {
        if (!http.Request.Query.TryGetValue(name, out var text)) return true;
        return text.Count == 1 && int.TryParse(text[0], NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0 && value <= max;
    }
    private static bool Filter(HttpContext http, string name, ref Guid? value)
    {
        if (!http.Request.Query.TryGetValue(name, out var text)) return true;
        if (text.Count != 1 || !Guid.TryParse(text[0], out var id) || id == Guid.Empty) return false;
        value = id; return true;
    }
    private static IResult QueryError() => Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid pagination or filter."] });
    private static IResult Problem(int status, string code, string title) => Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
