using System.Text.Json;
using Npgsql;

namespace GrainLedger.Api.Features.Capture;

// Capture owns this bulk read contract; no EF entities, farmer contacts or geometry cross it.
// Current reference labels are separate from immutable receipt facts. Caller owns the transaction.
public sealed record SourceReferenceLabels(string? FarmerCode, string? FarmerName, string? PlotCode, string? SeasonName);

public sealed record SourceProvenance(Guid LotId, Guid SourceRecordId, Guid ProcurementId,
    string ProcurementCode, Guid ProcurementItemId, Guid FarmerId, Guid PlotId, Guid SeasonId,
    Guid PlotSeasonId, string RiceVariety, string ExpectedKg, string OriginalValue, string OriginalUnit, SourceReferenceLabels CurrentReferenceLabels);

public sealed class SourceProvenanceReader
{
    public async Task<IReadOnlyList<SourceProvenance>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid ownerId, Guid[] lotIds, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT sl.lot_id,sl.id,p.id,p.code,i.id,e.payload->'result',line.value,
                   f.code,f.name,plot.code,season.name
            FROM grainledger.capture_source_lots sl
            JOIN grainledger.capture_procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
            JOIN grainledger.capture_procurements p ON p.id=i.procurement_id AND p.company_id=i.company_id
            JOIN grainledger.capture_business_events e ON e.id=p.event_id AND e.company_id=p.company_id
            CROSS JOIN LATERAL jsonb_array_elements(e.payload->'result'->'Items') line
            LEFT JOIN grainledger.capture_farmers f ON f.id=(e.payload->'result'->>'FarmerId')::uuid AND f.company_id=sl.company_id
            LEFT JOIN grainledger.capture_plots plot ON plot.id=(line.value->>'PlotId')::uuid AND plot.company_id=sl.company_id
            LEFT JOIN grainledger.capture_crop_seasons season ON season.id=(line.value->>'SeasonId')::uuid AND season.company_id=sl.company_id
            WHERE sl.lot_id=ANY($1) AND sl.company_id=$2 AND (line.value->>'Id')::uuid=i.id
              AND EXISTS(SELECT 1 FROM grainledger.identity_users u WHERE u."Id"=$3 AND u."IsActive"
                AND (u."Role"='Administrator' OR u."Role"='CustomerManager' AND u."OrganizationId"=$2))
            ORDER BY sl.lot_id
            """, connection, transaction);
        command.Parameters.AddWithValue(lotIds);
        command.Parameters.AddWithValue(ownerId);
        command.Parameters.AddWithValue(actorId);
        var result = new List<SourceProvenance>();
        await using var rows = await command.ExecuteReaderAsync(ct);
        while (await rows.ReadAsync(ct))
        {
            using var header = JsonDocument.Parse(rows.GetString(5));
            using var item = JsonDocument.Parse(rows.GetString(6));
            var h = header.RootElement; var l = item.RootElement;
            result.Add(new(rows.GetGuid(0), rows.GetGuid(1), rows.GetGuid(2), rows.GetString(3), rows.GetGuid(4),
                h.GetProperty("FarmerId").GetGuid(), l.GetProperty("PlotId").GetGuid(), l.GetProperty("SeasonId").GetGuid(),
                l.GetProperty("PlotSeasonId").GetGuid(), l.GetProperty("RiceVariety").GetString()!,
                l.GetProperty("NetMassKg").GetString()!, l.GetProperty("MeasuredValue").GetString()!, l.GetProperty("MeasuredUnit").GetString()!,
                new(rows.IsDBNull(7) ? null : rows.GetString(7), rows.IsDBNull(8) ? null : rows.GetString(8),
                    rows.IsDBNull(9) ? null : rows.GetString(9), rows.IsDBNull(10) ? null : rows.GetString(10))));
        }
        return result;
    }
}
