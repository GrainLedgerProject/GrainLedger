using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GrainLedger.Modules.Ledger;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResidueReportReviewRow(Guid Id, Guid ResultId, Guid SourceId,
    string? AbsoluteUncertainty = null, string? ReportingThreshold = null,
    Guid[]? SupersededResultIds = null, Guid? SupersedesId = null, string? Note = null,
    string? PreviewFingerprint = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResidueReportReviewRequest(Guid CompanyId, DateTimeOffset? EffectiveFrom,
    ResidueReportReviewRow[] Rows);
public sealed record ResidueReportResultChoice(Guid ResultId, string Label, Guid? SelectionId = null);
public sealed record ResidueReportReviewItem(ResidueReportReviewRow Input, string Label, string? RawUncertainty,
    ResidueRuleVersion? Profile, ResiduePolicySettingVersion? Setting, ResidueSourceBound? Assessment,
    IReadOnlyList<string> Reasons, IReadOnlyList<ResidueReportResultChoice> ConflictingResults,
    IReadOnlyList<ResidueReportResultChoice> ExistingSelections, bool Published = false);
public sealed record ResidueReportReviewResponse(Guid ReportId, DateTimeOffset EffectiveFrom,
    IReadOnlyList<ResidueReportReviewItem> Rows);

public sealed partial class ResidueEvaluationService : IConfirmedLabReportResidueBinder
{
    private sealed record ReportRow(Guid ResultId, Guid? SubstanceId, Guid SourceId, Guid LotId, string Label,
        string? Uncertainty);

    public async Task<ResidueReportReviewResponse> ReviewReportAsync(Guid reportId, ResidueReportReviewRequest request,
        bool publish, CancellationToken ct)
    {
        if (reportId == Guid.Empty || request.CompanyId == Guid.Empty || request.Rows is null || request.Rows.Length > 10000
            || publish && (request.Rows.Length == 0 || request.EffectiveFrom is null)
            || request.Rows.Any(x => x is null || x.Id == Guid.Empty || x.ResultId == Guid.Empty || x.SourceId == Guid.Empty
                || x.AbsoluteUncertainty is not null && !ResidueReview.Number(x.AbsoluteUncertainty)
                || x.ReportingThreshold is not null && !ResidueReview.PositiveNumber(x.ReportingThreshold)
                || x.SupersededResultIds is { } ids && (ids.Length > 1000 || ids.Contains(Guid.Empty)
                    || ids.Contains(x.ResultId) || ids.Distinct().Count() != ids.Length))
            || request.Rows.Select(x => x.Id).Distinct().Count() != request.Rows.Length
            || request.Rows.Select(x => (x.ResultId, x.SourceId)).Distinct().Count() != request.Rows.Length)
            throw new LedgerException(400, "Report review requires distinct valid row UUIDs, source/result bindings and exact decimal inputs");
        var actor = await ledger.ActorAsync(ct);
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("GrainLedger"));
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(publish ? IsolationLevel.ReadCommitted : IsolationLevel.RepeatableRead, ct);
        if (publish) await ExecuteAsync(connection, tx, "SELECT pg_advisory_xact_lock(714928352)", ct);
        else await ExecuteAsync(connection, tx, "SET TRANSACTION READ ONLY", ct);
        if (!await ResidueReview.AuthorizeAsync(connection, tx, actor, "residue.evidence.manage", request.CompanyId, ct))
            throw new LedgerException(403, "Residue evidence company access denied");
        var response = await ReviewReportInTransactionAsync(connection, tx, actor, reportId, request, publish, false, true, ct);
        await tx.CommitAsync(ct);
        return response;
    }

    public async Task<IReadOnlyList<LabReportResidueBinding>> BindConfirmedReportAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid actorId, Guid companyId, Guid reportId, DateTimeOffset confirmedAt,
        CancellationToken ct)
    {
        var authorized = await ResidueReview.AuthorizeAsync(connection, transaction, actorId,
            "residue.evidence.manage", companyId, ct);
        var response = await ReviewReportInTransactionAsync(connection, transaction, actorId, reportId,
            new(companyId, confirmedAt, []), true, true, authorized, ct);
        return response.Rows.Select(row => new LabReportResidueBinding(row.Input.ResultId, row.Input.SourceId,
            row.Published, row.Reasons)).ToArray();
    }

    private async Task<ResidueReportReviewResponse> ReviewReportInTransactionAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid actor, Guid reportId, ResidueReportReviewRequest request, bool publish,
        bool automatic, bool authorized, CancellationToken ct)
    {
        var requestedByKey = request.Rows.ToDictionary(x => (x.ResultId, x.SourceId));
        var now = new DateTimeOffset((DateTime)(await ExecuteAsync(connection, tx, "SELECT clock_timestamp()", ct))!);
        // Automatic binding is part of the Capture confirmation transaction.
        // The API clock and PostgreSQL clock may differ by milliseconds; use
        // the database instant that owns the binding instead of rejecting the
        // newly confirmed report as a future-effective manual review.
        var effectiveFrom = automatic ? now : request.EffectiveFrom ?? now;
        if (effectiveFrom > now) throw new LedgerException(400, "Report binding cannot become effective in the future");
        if (await ExecuteAsync(connection, tx, "SELECT id FROM capture.lab_reports WHERE id=$1 AND company_id=$2", ct,
            reportId, request.CompanyId) is not Guid)
            throw new LedgerException(404, "Own-company laboratory report not found");
        if (await ExecuteAsync(connection, tx, "SELECT report_id FROM capture.lab_report_retirements WHERE report_id=$1 AND company_id=$2", ct,
            reportId, request.CompanyId) is Guid)
            throw new LedgerException(409, "Deleted or superseded laboratory drafts cannot be bound as residue evidence");
        var reportRows = new List<ReportRow>();
        await using (var command = new NpgsqlCommand("""
            SELECT r.id,r.substance_id,COALESCE(sl.id,'00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE(sl.lot_id,'00000000-0000-0000-0000-000000000000'::uuid),
                report.report_number || ' · section ' || rs.position || ' · ' || r.test_label || ' · ' || r.original_result_text,r.uncertainty
            FROM capture.lab_reports report
            JOIN capture.lab_result_sets rs ON rs.report_id=report.id AND rs.company_id=report.company_id
            JOIN capture.lab_results r ON r.result_set_id=rs.id AND r.company_id=rs.company_id
            LEFT JOIN capture.lab_report_confirmations c ON c.report_id=report.id AND c.company_id=report.company_id
                AND c.identity_checked AND c.transcription_checked
            LEFT JOIN capture.lab_confirmed_associations a ON a.confirmation_id=c.id AND a.result_set_id=rs.id AND a.company_id=c.company_id
            LEFT JOIN capture.samples s ON s.id=a.sample_id AND s.company_id=a.company_id
            LEFT JOIN capture.source_lots sl ON sl.id=s.source_lot_id AND sl.company_id=s.company_id
            WHERE report.id=$1 AND report.company_id=$2 ORDER BY rs.position,r.position,r.id
            """, connection, tx))
        {
            command.Parameters.AddWithValue(reportId); command.Parameters.AddWithValue(request.CompanyId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) reportRows.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        if (!authorized)
            return new(reportId, effectiveFrom, reportRows.Select(row => new ResidueReportReviewItem(
                new(Guid.NewGuid(), row.ResultId, row.SourceId), row.Label, row.Uncertainty, null, null, null,
                ["Residue evidence permission required; identity and transcription remain confirmed"], [], [])).ToArray());
        var reportByKey = reportRows.ToDictionary(x => (x.ResultId, x.SourceId));
        if (requestedByKey.Keys.Any(key => !reportByKey.ContainsKey(key)))
            throw new LedgerException(400, "Every requested row must belong to this report and its confirmed source association");
        var lotIds = reportRows.Where(x => x.LotId != Guid.Empty).Select(x => x.LotId).Distinct().ToArray();
        var substances = reportRows.Where(x => x.SubstanceId is not null).Select(x => x.SubstanceId!.Value).Distinct().ToArray();
        var evidence = await evidenceReader.ReadAsync(connection, tx, request.CompanyId, lotIds, substances, now, ct);
        var policy = await policyReader.ReadAsync(connection, tx, request.CompanyId, substances, now, ct);
        var graphs = (string)(await ExecuteAsync(connection, tx, """
            SELECT COALESCE(jsonb_agg(ledger.residue_graph($1,source.lot_id)), '[]'::jsonb)::text
            FROM capture.source_lots source
            JOIN ledger.lots lot ON lot.id=source.lot_id AND lot.owner_id=source.company_id
                AND lot.receipt_id IS NOT NULL AND lot.operation_id IS NULL
            WHERE source.company_id=$2 AND source.lot_id=ANY($3)
            """, ct, actor, request.CompanyId, lotIds))!;
        var nodes = JsonSerializer.Deserialize<ResidueNode[][]>(graphs, Json)!.SelectMany(x => x)
            .Where(x => x.Kind == "SOURCE").DistinctBy(x => x.LotId).ToDictionary(x => x.LotId);
        var labels = new Dictionary<Guid, string>();
        await using (var command = new NpgsqlCommand("""
            SELECT r.id,report.report_number || ' · section ' || rs.position || ' · ' || r.test_label || ' · ' || r.original_result_text
            FROM capture.lab_results r JOIN capture.lab_result_sets rs ON rs.id=r.result_set_id AND rs.company_id=r.company_id
            JOIN capture.lab_reports report ON report.id=rs.report_id AND report.company_id=rs.company_id
            WHERE r.company_id=$1 AND r.id=ANY($2)
            """, connection, tx))
        {
            command.Parameters.AddWithValue(request.CompanyId); command.Parameters.AddWithValue(evidence.Results.Select(x => x.ResultId).Distinct().ToArray());
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) labels[reader.GetGuid(0)] = reader.GetString(1);
        }
        var sourcesById = evidence.Sources.ToDictionary(x => x.SourceId);
        var resultsByKey = evidence.Results.DistinctBy(x => (x.SourceId, x.ResultId)).ToDictionary(x => (x.SourceId, x.ResultId));
        var candidatesByScope = evidence.Results.GroupBy(x => (x.SourceId, x.SubstanceId))
            .ToDictionary(g => g.Key, g => g.DistinctBy(x => x.ResultId).ToArray());
        var historyByScope = evidence.Selections.GroupBy(x => (x.SourceId, x.SubstanceId)).ToDictionary(g => g.Key, g => g.ToArray());
        var activeByScope = historyByScope.ToDictionary(pair => pair.Key,
            pair => ResidueEvidenceResolver.ActiveSelections(pair.Value, now));
        var candidateIdsByScope = candidatesByScope.ToDictionary(pair => pair.Key, pair => pair.Value.Select(x => x.ResultId).ToHashSet());
        var selectionsById = evidence.Selections.ToDictionary(x => x.Id);
        var rulesById = policy.Rules.ToDictionary(x => x.Id);
        var settingsById = policy.Settings.ToDictionary(x => x.Id);
        var settingsByCode = policy.Settings.GroupBy(x => x.RuleCode).ToDictionary(g => g.Key, g => g.ToArray());
        var policyBySubstance = policy.Rules.GroupBy(x => x.SubstanceId).ToDictionary(g => g.Key,
            g => new ResiduePolicySnapshot(g.ToArray(), g.Select(x => x.Code).Distinct()
                .SelectMany(code => settingsByCode.GetValueOrDefault(code) ?? []).ToArray()));
        var emptyPolicy = new ResiduePolicySnapshot([], []);
        var groupHistories = evidence.Groups.GroupBy(x => (x.SubstanceId, x.Code)).ToDictionary(g => g.Key, g => g.ToArray());
        var groupsByScope = evidence.Groups.SelectMany(g => g.SourceIds.Select(sourceId => (SourceId: sourceId, g.SubstanceId, g.Code)))
            .GroupBy(x => (x.SourceId, x.SubstanceId)).ToDictionary(g => g.Key,
                g => g.Select(x => (x.SubstanceId, x.Code)).Distinct().SelectMany(code => groupHistories[code]).ToArray());
        var evidenceByScope = candidatesByScope.ToDictionary(pair => pair.Key, pair =>
            new ResidueEvidenceSnapshot([sourcesById[pair.Key.SourceId]], pair.Value,
                groupsByScope.GetValueOrDefault(pair.Key) ?? [], historyByScope.GetValueOrDefault(pair.Key) ?? []));
        var output = new List<ResidueReportReviewItem>();
        var append = new List<ResidueReportSelection>();
        var replayCount = 0;
        foreach (var row in reportRows.Where(row => automatic || !publish || requestedByKey.ContainsKey((row.ResultId, row.SourceId))))
        {
            var requestedInput = requestedByKey.GetValueOrDefault((row.ResultId, row.SourceId));
            var input = requestedInput ?? new ResidueReportReviewRow(Guid.NewGuid(), row.ResultId, row.SourceId);
            var canonical = JsonSerializer.Serialize(new { reportId, request.CompanyId, effectiveFrom,
                row = input with { PreviewFingerprint = null, SupersededResultIds = (input.SupersededResultIds ?? []).Order().ToArray() } }, Json);
            var prior = selectionsById.GetValueOrDefault(input.Id);
            if (prior is not null)
            {
                if (prior.BindingRequest != canonical || prior.BindingFingerprint != input.PreviewFingerprint || prior.PublishedBy != actor)
                    throw new LedgerException(409, "Selection UUID already used for another report binding");
                replayCount++;
                output.Add(new(input, row.Label, row.Uncertainty, prior.ProfileRuleId is { } ruleId ? rulesById.GetValueOrDefault(ruleId) : null,
                    prior.ProfileSettingId is { } settingId ? settingsById.GetValueOrDefault(settingId) : null, null, [], [], [], true));
                continue;
            }
            var reasons = new List<string>();
            ResidueRuleVersion? rule = null; ResiduePolicySettingVersion? setting = null; ResidueSourceBound? assessment = null;
            var conflicts = new List<ResidueReportResultChoice>();
            var existing = new List<ResidueReportResultChoice>();
            if (row.SubstanceId is null || row.SourceId == Guid.Empty)
                reasons.Add("Confirmed source association and mapped substance required");
            else
            {
                if (!sourcesById.TryGetValue(row.SourceId, out var source)
                    || !nodes.TryGetValue(row.LotId, out var node) || node.CompanyId != request.CompanyId)
                {
                    output.Add(new(input, row.Label, row.Uncertainty, null, null, null,
                        ["Original owned source lot required"], [], []));
                    continue;
                }
                var selected = resultsByKey.GetValueOrDefault((row.SourceId, row.ResultId));
                if (selected is null || selected.ReportId != reportId) reasons.Add("Confirmed report/result/source association required");
                else
                {
                    var scope = (row.SourceId, row.SubstanceId.Value);
                    var candidates = candidatesByScope[scope];
                    var candidateIds = candidateIdsByScope[scope];
                    var scopedEvidence = evidenceByScope[scope];
                    var history = scopedEvidence.Selections;
                    var scopedGroups = scopedEvidence.Groups;
                    var scopedPolicy = policyBySubstance.GetValueOrDefault(row.SubstanceId.Value) ?? emptyPolicy;
                    var resolution = ResidueEvidenceResolver.SelectPolicy(scopedPolicy, request.CompanyId, source.ReceivedAt, x =>
                        x.Kind == "INTERPRETATION" && x.SubstanceId == row.SubstanceId && x.CommodityId == node.CommodityId
                        && x.InputStage == source.ProductStage && x.OutputStage == source.ProductStage
                        && x.InputBasis == x.OutputBasis && x.Unit == ResidueConcentration.CanonicalUnit(selected.Unit) && x.AnalyticalMethod == selected.Method
                        && (selected.ResultKind == "NUMERIC" ? x.InterpretationKind is "NUMERIC_ABSOLUTE_UNCERTAINTY" or "NUMERIC_RELATIVE_UNCERTAINTY"
                            : x.InterpretationKind == (selected.ResultKind == "BELOW_LIMIT" ? "BELOW_LIMIT" : selected.ResultKind == "NOT_DETECTED" ? "ND" : "UNSUPPORTED")));
                    if (resolution.Reason is not null || resolution.Rules.Count != 1)
                        reasons.Add(resolution.Reason ?? (resolution.Rules.Count == 0
                            ? $"No compatible adopted interpretation profile: method {selected.Method ?? "(not reported)"}, unit {selected.Unit ?? "(not reported)"}, commodity {node.CommodityId}, stage {source.ProductStage}, source received {source.ReceivedAt:O}"
                            : "Multiple compatible adopted profiles; expert applicability review required"));
                    else (rule, setting) = resolution.Rules[0];
                    conflicts.AddRange(candidates.Where(x => x.ResultId != row.ResultId).Select(x => new ResidueReportResultChoice(x.ResultId, labels[x.ResultId])));
                    var active = activeByScope.GetValueOrDefault(scope) ?? [];
                    existing.AddRange(active.Select(x => new ResidueReportResultChoice(x.SelectedResultId, labels.GetValueOrDefault(x.SelectedResultId, "Historical selected report result"), x.Id)));
                    if (!publish && active is [var bound] && bound.SelectedResultId == row.ResultId && input.SupersedesId is null
                        && (requestedInput is null || input.AbsoluteUncertainty == bound.AbsoluteUncertainty
                            && input.ReportingThreshold == bound.ReportingThreshold && input.Note == bound.BindingNote
                            && (input.SupersededResultIds ?? []).Order().SequenceEqual(bound.SupersededResultIds.Order())))
                    {
                        input = input with { AbsoluteUncertainty = bound.AbsoluteUncertainty, ReportingThreshold = bound.ReportingThreshold,
                            SupersededResultIds = bound.SupersededResultIds, Note = bound.BindingNote };
                        assessment = ResidueEvidenceResolver.Resolve([node], [row.SubstanceId.Value], scopedEvidence, scopedPolicy, now).Single();
                        reasons.AddRange(assessment.Reasons);
                        output.Add(new(input, row.Label, row.Uncertainty, rule, setting, assessment, reasons.Distinct().ToArray(),
                            conflicts, existing, true));
                        continue;
                    }
                    if (input.SupersededResultIds?.Any(id => !candidateIds.Contains(id)) == true)
                        reasons.Add("Superseded results must share confirmed source and substance");
                    if (active.Length != 0 && (active.Length != 1 || input.SupersedesId != active[0].Id))
                        reasons.Add("Existing report selection requires explicit reviewed supersession");
                    if (input.SupersedesId is not null && !active.Any(x => x.Id == input.SupersedesId))
                        reasons.Add("Superseded selection must be current for this source and substance");
                    if (input.AbsoluteUncertainty is not null && (selected.ResultKind != "NUMERIC" || string.IsNullOrWhiteSpace(selected.Uncertainty)))
                        reasons.Add("Normalized uncertainty requires numeric result with retained raw uncertainty evidence");
                    if (input.ReportingThreshold is not null && selected.ResultKind != "NOT_DETECTED")
                        reasons.Add("Reporting threshold applies only to NOT_DETECTED results");
                    if ((input.AbsoluteUncertainty is not null || input.ReportingThreshold is not null || input.SupersedesId is not null || input.SupersededResultIds is { Length: > 0 })
                        && string.IsNullOrWhiteSpace(input.Note)) reasons.Add("Document normalization, threshold or supersession review in binding note");
                    if (rule is not null && setting is not null)
                    {
                        var selection = new ResidueReportSelection(input.Id, request.CompanyId, row.SourceId, row.SubstanceId.Value,
                            row.ResultId, (input.SupersededResultIds ?? []).Order().ToArray(), rule.InputBasis, rule.ResidueDefinition,
                            input.AbsoluteUncertainty, input.ReportingThreshold, effectiveFrom, null, input.SupersedesId,
                            $"{rule.EvidenceRef}; company adoption: {setting.EvidenceRef}",
                            $"{rule.ApplicabilityStatement}; company adoption: {setting.ApplicabilityStatement}",
                            rule.ReviewedBy, rule.ReviewAuthority, rule.ReviewedAt, actor, now,
                            reportId, rule.Id, setting.Id, canonical, BindingNote: input.Note);
                        var proposed = scopedEvidence with { Selections = history.Append(selection).ToArray() };
                        assessment = ResidueEvidenceResolver.Resolve([node], [row.SubstanceId.Value], proposed, scopedPolicy, now).Single();
                        reasons.AddRange(assessment.Reasons);
                        if (!assessment.Provenance.Any(x => x.SelectionId == input.Id))
                            reasons.Add("Direct report binding is not authoritative under current fallback precedence");
                        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                        {
                            canonical, rule, setting, selected, candidates, history, groups = scopedGroups,
                            assessment
                        }, Json))));
                        if (publish && !automatic && input.PreviewFingerprint != fingerprint)
                            throw new LedgerException(409, "Report review changed since preview; prepare and review again");
                        input = input with { PreviewFingerprint = fingerprint };
                        if (reasons.Count == 0) append.Add(selection with { BindingFingerprint = fingerprint });
                    }
                }
            }
            output.Add(new(input, row.Label, row.Uncertainty, rule, setting, assessment, reasons.Distinct().ToArray(), conflicts, existing));
        }
        if (publish)
        {
            if (!automatic && (output.Count != request.Rows.Length || output.Any(x => x.Reasons.Count != 0) || replayCount != 0 && replayCount != output.Count
                || request.Rows.GroupBy(x => (x.SourceId, Substance: reportByKey[(x.ResultId, x.SourceId)].SubstanceId)).Any(x => x.Count() != 1)))
                throw new LedgerException(409, "Every requested row must be compatible and explicitly reviewed; no selections published");
            foreach (var selection in append)
                await ExecuteAsync(connection, tx, "SELECT capture.publish_residue_selection($1,$2)", ct, actor,
                    JsonSerializer.SerializeToElement(selection));
            var appendedIds = append.Select(x => x.Id).ToHashSet();
            output = output.Select(x => x with { Published = x.Published || appendedIds.Contains(x.Input.Id) }).ToList();
        }
        return new(reportId, effectiveFrom, output);
    }
}
