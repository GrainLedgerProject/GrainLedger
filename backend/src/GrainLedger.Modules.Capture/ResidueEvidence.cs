using System.Text.Json;
using Npgsql;

namespace GrainLedger.Modules.Capture;

public sealed record ResidueSource(Guid SourceId, Guid LotId, Guid CompanyId, DateTimeOffset ReceivedAt,
    string ProductStage, string ProductIdentity);
public sealed record ResidueLabEvidence(Guid SourceId, Guid LotId, Guid ReportId, Guid ResultSetId,
    Guid ResultId, Guid AssociationId, Guid SubstanceId, string ResultKind, string? NumericValue,
    string? Unit, string? Lod, string? Loq, string? Method, string? Uncertainty, DateTimeOffset ConfirmedAt,
    Guid? SelectionId, string? MaterialBasis, string? ResidueDefinition, string? AbsoluteUncertainty,
    string? ReportingThreshold, bool Selected);
public sealed record ResidueSourceGroupVersion(Guid Id, Guid CompanyId, string Code, int VersionNo,
    Guid SubstanceId, Guid[] SourceIds, string ProductStage, string ProductIdentity, string MaterialBasis,
    string ResidueDefinition, string Lower, string Upper, string Unit, string Precedence,
    Guid[] SupportingResultIds, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid? SupersedesId,
    string EvidenceRef, string ApplicabilityStatement, string ReviewedBy, string ReviewAuthority,
    DateTimeOffset ReviewedAt, Guid? PublishedBy = null, DateTimeOffset? PublishedAt = null, int DecimalPlaces = -1);
public sealed record ResidueReportSelection(Guid Id, Guid CompanyId, Guid SourceId, Guid SubstanceId,
    Guid SelectedResultId, Guid[] SupersededResultIds, string MaterialBasis, string ResidueDefinition,
    string? AbsoluteUncertainty, string? ReportingThreshold, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, Guid? SupersedesId, string EvidenceRef, string ApplicabilityStatement,
    string ReviewedBy, string ReviewAuthority, DateTimeOffset ReviewedAt,
    Guid? PublishedBy = null, DateTimeOffset? PublishedAt = null,
    Guid? BindingReportId = null, Guid? ProfileRuleId = null, Guid? ProfileSettingId = null,
    string? BindingRequest = null, string? BindingFingerprint = null, string? BindingNote = null);
public sealed record ResidueEvidenceSnapshot(IReadOnlyList<ResidueSource> Sources,
    IReadOnlyList<ResidueLabEvidence> Results, IReadOnlyList<ResidueSourceGroupVersion> Groups,
    IReadOnlyList<ResidueReportSelection> Selections);

public interface IResidueEvidenceReader
{
    Task<ResidueEvidenceSnapshot> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, IReadOnlyCollection<Guid> lotIds, IReadOnlyCollection<Guid> substanceIds,
        DateTimeOffset snapshotAt, CancellationToken ct);
}

public sealed class ResidueEvidenceReader : IResidueEvidenceReader
{
    public async Task<ResidueEvidenceSnapshot> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid companyId, IReadOnlyCollection<Guid> lotIds, IReadOnlyCollection<Guid> substanceIds,
        DateTimeOffset snapshotAt, CancellationToken ct)
    {
        var sources = new List<ResidueSource>();
        // Official procurement contract registers only raw paddy; transformed identities belong to Ledger.
        await using (var command = new NpgsqlCommand("""
            SELECT sl.id,sl.lot_id,p.received_at FROM capture.source_lots sl
            JOIN capture.procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
            JOIN capture.procurements p ON p.id=i.procurement_id AND p.company_id=i.company_id
            WHERE sl.company_id=$1 AND sl.lot_id=ANY($2)
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(companyId); command.Parameters.AddWithValue(lotIds.ToArray());
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) sources.Add(new(reader.GetGuid(0), reader.GetGuid(1), companyId,
                reader.GetFieldValue<DateTimeOffset>(2), "RAW_PADDY", "RAW_PADDY"));
        }
        var sourceIds = sources.Select(x => x.SourceId).ToArray();
        var groups = await ReadPayloadsAsync<ResidueSourceGroupVersion>(connection, transaction,
            "residue_source_group_versions", companyId, snapshotAt, ct, sourceIds, substanceIds.ToArray());
        var selections = await ReadPayloadsAsync<ResidueReportSelection>(connection, transaction,
            "residue_report_selections", companyId, snapshotAt, ct, sourceIds, substanceIds.ToArray());
        var effectiveSelections = selections.Where(x => x.EffectiveFrom <= snapshotAt &&
            (x.EffectiveTo is null || snapshotAt < x.EffectiveTo)).ToArray();
        var selectionHistory = selections.ToDictionary(x => x.Id);
        var supersededSelections = new HashSet<Guid>();
        foreach (var selection in effectiveSelections)
        {
            var previous = selection.SupersedesId;
            while (previous is { } id && supersededSelections.Add(id) && selectionHistory.TryGetValue(id, out var ancestor))
                previous = ancestor.SupersedesId;
        }
        var activeSelections = effectiveSelections.Where(x => !supersededSelections.Contains(x.Id))
            .GroupBy(x => (x.SourceId, x.SubstanceId)).ToDictionary(x => x.Key, x => x.ToArray());
        var results = new List<ResidueLabEvidence>();
        await using (var command = new NpgsqlCommand("""
            SELECT sl.id,sl.lot_id,rs.report_id,rs.id,r.id,a.id,r.substance_id,r.result_kind,
                r.numeric_value,r.unit,r.lod,r.loq,r.method,r.uncertainty,c.confirmed_at
            FROM capture.source_lots sl
            JOIN capture.samples s ON s.source_lot_id=sl.id AND s.company_id=sl.company_id
            JOIN capture.lab_confirmed_associations a ON a.sample_id=s.id AND a.company_id=s.company_id
            JOIN capture.lab_report_confirmations c ON c.id=a.confirmation_id AND c.company_id=a.company_id
            JOIN capture.lab_result_sets rs ON rs.id=a.result_set_id AND rs.company_id=a.company_id AND rs.report_id=c.report_id
            JOIN capture.lab_results r ON r.result_set_id=rs.id AND r.company_id=rs.company_id
            WHERE sl.company_id=$1 AND sl.lot_id=ANY($2) AND r.substance_id=ANY($3)
                AND c.confirmed_at <= $4 AND c.identity_checked AND c.transcription_checked
            ORDER BY sl.id,r.id
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(companyId); command.Parameters.AddWithValue(lotIds.ToArray());
            command.Parameters.AddWithValue(substanceIds.ToArray()); command.Parameters.AddWithValue(snapshotAt);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var source = reader.GetGuid(0); var substance = reader.GetGuid(6); var resultId = reader.GetGuid(4);
                var selected = activeSelections.TryGetValue((source, substance), out var candidates) && candidates.Length == 1
                    ? candidates[0] : null;
                results.Add(new(source, reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), resultId,
                    reader.GetGuid(5), substance, reader.GetString(7), Text(8), Text(9), Text(10), Text(11),
                    Text(12), Text(13), reader.GetFieldValue<DateTimeOffset>(14), selected?.Id,
                    selected?.MaterialBasis, selected?.ResidueDefinition, selected?.AbsoluteUncertainty,
                    selected?.ReportingThreshold, selected?.SelectedResultId == resultId));
                string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
            }
        }
        return new(sources, results, groups, selections);
    }

    internal static async Task<List<T>> ReadPayloadsAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string table, Guid companyId, DateTimeOffset snapshotAt, CancellationToken ct,
        Guid[]? sourceIds = null, Guid[]? substanceIds = null)
    {
        // Table names are module-owned constants, never request input.
        if (table is not ("residue_source_group_versions" or "residue_report_selections")) throw new ArgumentOutOfRangeException(nameof(table));
        var scope = sourceIds is null ? "" : table == "residue_report_selections"
            ? " AND substance_id=ANY($3) AND source_id=ANY($4)"
            : """
               AND substance_id=ANY($3) AND code IN (
                   SELECT code FROM capture.residue_source_group_versions
                   WHERE company_id=$1 AND published_at <= $2 AND substance_id=ANY($3) AND source_ids && $4)
              """;
        // Return full group version chains: supersession can explicitly remove membership.
        await using var command = new NpgsqlCommand($"SELECT payload::text FROM capture.{table} WHERE company_id=$1 AND published_at <= $2{scope} ORDER BY published_at,id", connection, transaction);
        command.Parameters.AddWithValue(companyId); command.Parameters.AddWithValue(snapshotAt);
        if (sourceIds is not null)
        {
            command.Parameters.AddWithValue(substanceIds!);
            command.Parameters.AddWithValue(sourceIds);
        }
        var items = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
        return items;
    }
}
