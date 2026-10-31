using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Capture;

// A company-scoped read contract for downstream assessment. Confirmation means
// identity and transcription were checked; it is not a residue verdict.
public sealed record ConfirmedLabEvidence(Guid SourceLotId, Guid SampleId, Guid ReportId,
    Guid ConfirmationId, DateTimeOffset ConfirmedAt, Guid ResultSetId, Guid ResultId,
    Guid? SubstanceId, string ResultKind, string OriginalResultText, string? NumericValue,
    string? Unit, string? Lod, string? Loq, Guid? RelatedReportId,
    IReadOnlyList<Guid> LaterRelatedReportIds);

public sealed record ConfirmedLabEvidenceBatch(IReadOnlyList<ConfirmedLabEvidence> Rows,
    IReadOnlyList<Guid> SourcesWithoutConfirmedEvidence, string Revision);

public sealed class ConfirmedLabEvidenceRead(CaptureDbContext db)
{
    public async Task<ConfirmedLabEvidenceBatch> ReadAsync(Guid companyId,
        IReadOnlyCollection<Guid> sourceLotIds, CancellationToken ct = default)
    {
        if (companyId == Guid.Empty || sourceLotIds.Count > 10000 ||
            sourceLotIds.Any(x => x == Guid.Empty))
            throw new ArgumentException("A company and a bounded set of source lots are required.");
        var sourceIds = sourceLotIds.Distinct().Order().ToArray();
        if (sourceIds.Length == 0) return new([], [], Convert.ToHexStringLower(SHA256.HashData([])));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);

        var samples = await db.Samples.AsNoTracking()
            .Where(x => x.CompanyId == companyId && sourceIds.Contains(x.SourceLotId))
            .Select(x => new { x.Id, x.SourceLotId }).ToArrayAsync(ct);
        var sourceBySample = samples.ToDictionary(x => x.Id, x => x.SourceLotId);
        var sampleIds = sourceBySample.Keys.ToArray();
        var links = await db.LabConfirmedAssociations.AsNoTracking()
            .Where(x => x.CompanyId == companyId && sampleIds.Contains(x.SampleId))
            .Select(x => new { x.SampleId, x.ResultSetId, x.ConfirmationId }).ToArrayAsync(ct);
        var confirmationIds = links.Select(x => x.ConfirmationId).Distinct().ToArray();
        var confirmations = await db.LabReportConfirmations.AsNoTracking()
            .Where(x => x.CompanyId == companyId && confirmationIds.Contains(x.Id) &&
                x.IdentityChecked && x.TranscriptionChecked)
            .Select(x => new { x.Id, x.ReportId, x.ConfirmedAt })
            .ToDictionaryAsync(x => x.Id, ct);
        var setIds = links.Select(x => x.ResultSetId).Distinct().ToArray();
        var sets = await db.LabResultSets.AsNoTracking()
            .Where(x => x.CompanyId == companyId && setIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ReportId }).ToDictionaryAsync(x => x.Id, ct);
        var reportIds = confirmations.Values.Select(x => x.ReportId).Distinct().ToArray();
        var reports = await db.LabReports.AsNoTracking()
            .Where(x => x.CompanyId == companyId && reportIds.Contains(x.Id))
            .Select(x => new { x.Id, x.RelatedReportId }).ToDictionaryAsync(x => x.Id, ct);
        var related = await db.LabReports.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RelatedReportId != null &&
                reportIds.Contains(x.RelatedReportId.Value))
            .Select(x => new { x.Id, ParentId = x.RelatedReportId!.Value }).ToArrayAsync(ct);
        var relatedByParent = related.GroupBy(x => x.ParentId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<Guid>)x.Select(y => y.Id).Order().ToArray());
        var results = await db.LabResults.AsNoTracking()
            .Where(x => x.CompanyId == companyId && setIds.Contains(x.ResultSetId))
            .Select(x => new { x.Id, x.ResultSetId, x.SubstanceId, x.ResultKind,
                x.OriginalResultText, x.NumericValue, x.Unit, x.Lod, x.Loq }).ToArrayAsync(ct);
        var resultsBySet = results.ToLookup(x => x.ResultSetId);

        var rows = links.SelectMany(link =>
        {
            if (!confirmations.TryGetValue(link.ConfirmationId, out var confirmation) ||
                !sets.TryGetValue(link.ResultSetId, out var set) ||
                confirmation.ReportId != set.ReportId ||
                !reports.TryGetValue(confirmation.ReportId, out var report))
                return Enumerable.Empty<ConfirmedLabEvidence>();
            return resultsBySet[link.ResultSetId].Select(row => new ConfirmedLabEvidence(
                sourceBySample[link.SampleId], link.SampleId, report.Id, confirmation.Id,
                confirmation.ConfirmedAt, link.ResultSetId, row.Id, row.SubstanceId,
                row.ResultKind, row.OriginalResultText, row.NumericValue, row.Unit,
                row.Lod, row.Loq, report.RelatedReportId,
                relatedByParent.GetValueOrDefault(report.Id, [])));
        }).OrderBy(x => x.SourceLotId).ThenBy(x => x.ReportId).ThenBy(x => x.ResultSetId)
            .ThenBy(x => x.ResultId).ToArray();
        var missing = sourceIds.Except(rows.Select(x => x.SourceLotId)).ToArray();
        var canonical = JsonSerializer.Serialize(new { rows, missing });
        var revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        await transaction.CommitAsync(ct);
        return new(rows, missing, revision);
    }
}
