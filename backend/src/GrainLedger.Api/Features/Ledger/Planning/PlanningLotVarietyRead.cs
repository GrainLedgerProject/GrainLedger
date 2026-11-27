using System.Data;
using Npgsql;

namespace GrainLedger.Api.Features.Ledger;

public sealed record PlanningLotVariety(Guid LotId, Guid? RiceVarietyId, string VarietyStatus, string ProductIdentity);

public sealed partial class LedgerService
{
    public async Task<IReadOnlyList<PlanningLotVariety>> ReadPlanningLotVarietiesAsync(Guid[] lotIds, CancellationToken ct)
    {
        var ids = lotIds.Distinct().ToArray();
        if (ids.Length is < 1 or > 100) throw new LedgerException(400, "Choose 1–100 distinct lots.");
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT id,rice_variety_id,variety_status,product_identity FROM grainledger.ledger_lots
            WHERE id=ANY($1) AND grainledger.ledger_can_read_lot($2,id)
            ORDER BY id
            """, connection, transaction);
        command.Parameters.AddWithValue(ids); command.Parameters.AddWithValue(actor);
        var result = new List<PlanningLotVariety>();
        await using (var rows = await command.ExecuteReaderAsync(ct))
            while (await rows.ReadAsync(ct)) result.Add(new(rows.GetGuid(0), rows.IsDBNull(1) ? null : rows.GetGuid(1), rows.GetString(2), rows.GetString(3)));
        if (result.Count != ids.Length) throw new LedgerException(403, "Lot variety access denied.");
        await transaction.CommitAsync(ct);
        return result;
    }
}
