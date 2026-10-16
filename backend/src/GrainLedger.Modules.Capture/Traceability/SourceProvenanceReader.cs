using System.Text.Json;
using Npgsql;

namespace GrainLedger.Modules.Capture;

// Capture owns this bulk read contract. It only exposes immutable receipt facts, no EF entities,
// farmer contacts, geometry or mutable master-data labels. Caller owns the transaction.
public sealed record SourceProvenance(Guid LotId, Guid SourceRecordId, Guid ProcurementId,
    string ProcurementCode, Guid ProcurementItemId, Guid FarmerId, Guid PlotId, Guid SeasonId,
    Guid PlotSeasonId, string RiceVariety, string ExpectedKg, string OriginalValue, string OriginalUnit);

public interface ISourceProvenanceReader
{
    Task<IReadOnlyList<SourceProvenance>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid ownerId, Guid[] lotIds, CancellationToken ct);
}

public sealed class SourceProvenanceReader : ISourceProvenanceReader
{
    public async Task<IReadOnlyList<SourceProvenance>> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actorId, Guid ownerId, Guid[] lotIds, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT sl.lot_id,sl.id,p.id,p.code,i.id,e.payload->'result',line.value
            FROM capture.source_lots sl
            JOIN capture.procurement_items i ON i.id=sl.procurement_item_id AND i.company_id=sl.company_id
            JOIN capture.procurements p ON p.id=i.procurement_id AND p.company_id=i.company_id
            JOIN capture.business_events e ON e.id=p.event_id AND e.company_id=p.company_id
            CROSS JOIN LATERAL jsonb_array_elements(e.payload->'result'->'Items') line
            WHERE sl.lot_id=ANY($1) AND sl.company_id=$2 AND (line.value->>'Id')::uuid=i.id
              AND EXISTS(SELECT 1 FROM identity.users u WHERE u."Id"=$3 AND u."IsActive"
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
                l.GetProperty("NetMassKg").GetString()!, l.GetProperty("MeasuredValue").GetString()!, l.GetProperty("MeasuredUnit").GetString()!));
        }
        return result;
    }
}
