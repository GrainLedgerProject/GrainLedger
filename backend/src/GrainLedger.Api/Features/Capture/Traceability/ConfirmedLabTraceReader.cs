using Npgsql;

namespace GrainLedger.Api.Features.Capture;

public sealed record ConfirmedLabTraceOrigin(Guid OwnerId, Guid LotId, Guid ReportId, Guid ResultId, Guid SampleId, Guid? SubstanceId);
// Only confirmed sample associations can start lab tracing. Caller owns transaction;
// no report bytes, source master data or EF entities leave Capture.
public sealed class ConfirmedLabTraceReader
{
    public async Task<ConfirmedLabTraceOrigin?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid reportId, Guid resultId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT report.company_id,source.lot_id,report.id,result.id,sample.id,result.substance_id
            FROM grainledger.capture_lab_reports report
            JOIN grainledger.capture_lab_report_confirmations confirmation ON confirmation.report_id=report.id AND confirmation.company_id=report.company_id
            JOIN grainledger.capture_lab_result_sets panel ON panel.report_id=report.id AND panel.company_id=report.company_id
            JOIN grainledger.capture_lab_results result ON result.result_set_id=panel.id AND result.company_id=report.company_id
            JOIN grainledger.capture_lab_confirmed_associations association ON association.result_set_id=panel.id AND association.company_id=report.company_id
            JOIN grainledger.capture_samples sample ON sample.id=association.sample_id AND sample.company_id=report.company_id
            JOIN grainledger.capture_source_lots source ON source.id=sample.source_lot_id AND source.company_id=report.company_id
            WHERE report.id=$2 AND result.id=$3
              AND NOT EXISTS(SELECT 1 FROM grainledger.capture_lab_report_retirements retired WHERE retired.report_id=report.id AND retired.company_id=report.company_id)
              AND EXISTS(SELECT 1 FROM grainledger.identity_users u WHERE u."Id"=$1 AND u."IsActive"
                  AND u."Role" IN ('CustomerManager','ExportStaff') AND u."OrganizationId"=report.company_id)
            """, connection, transaction);
        command.Parameters.AddWithValue(actorId); command.Parameters.AddWithValue(reportId); command.Parameters.AddWithValue(resultId);
        await using var row = await command.ExecuteReaderAsync(ct);
        return await row.ReadAsync(ct) ? new(row.GetGuid(0),row.GetGuid(1),row.GetGuid(2),row.GetGuid(3),row.GetGuid(4),row.IsDBNull(5) ? null : row.GetGuid(5)) : null;
    }
}
