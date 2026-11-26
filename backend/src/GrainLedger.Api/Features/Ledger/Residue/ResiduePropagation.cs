using System.Data;
using System.Text.Json;
using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Catalogs;
using Npgsql;
using NpgsqlTypes;

namespace GrainLedger.Api.Features.Ledger;

internal sealed record ResidueWorkSnapshot(JsonElement Snapshot, string Revision);

// No HTTP actor or session: the protected job capability supplies the scope.
public sealed class ResiduePropagationRunner(NpgsqlDataSource dataSource, ResiduePolicyReader policies,
    ResidueEvidenceReader evidence, GrainLedger.Api.Features.Exports.ExportResiduePropagation export, ILogger<ResiduePropagationRunner> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<int> RunOnceAsync(CancellationToken ct = default, int batchSize = 25)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var token = Guid.NewGuid();
        JsonElement[] claimed;
        // Claim commits and releases its connection before any business lock or calculation.
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        {
            await using var command = new NpgsqlCommand("SELECT grainledger.ledger_residue_change_claim($1,1,60)::text", connection);
            command.Parameters.AddWithValue(token);
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
            claimed = document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
        }
        foreach (var job in claimed)
            await ProcessAsync(job.GetProperty("id").GetGuid(), token, job.GetProperty("generation").GetInt32(), batchSize, ct);
        return claimed.Length;
    }

    internal async Task ProcessAsync(Guid job, Guid token, int generation, int batchSize, CancellationToken ct)
    {
        try
        {
            await CallAsync(job, token, generation, "DISCOVER", null, new { batchSize }, ct);
            var next = await CallAsync(job, token, generation, "NEXT", null, new { batchSize }, ct);
            foreach (var id in next.EnumerateArray())
            {
                var target = id.GetGuid();
                var prepared = await PrepareAsync(job, token, generation, target, ct);
                await AppendAsync(job, token, generation, target, prepared, ct);
            }
            await export.ProcessAsync(job, token, generation, batchSize, ct);
            var result = await CallAsync(job, token, generation, "FINISH", null, new { }, ct);
            if (!result.GetProperty("completed").GetBoolean())
                await CallAsync(job, token, generation, "YIELD", null, new { }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PostgresException ex) when (ex.SqlState == "55000")
        {
            logger.LogInformation("Residue job {JobId} lost its lease; another claim may resume it", job);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Residue job {JobId} needs retry", job);
            try { await CallAsync(job, token, generation, "RETRY", null, new { }, ct); }
            catch (PostgresException lost) when (lost.SqlState == "55000") { }
        }
    }

    internal async Task<ResidueWorkSnapshot> PrepareAsync(Guid job, Guid token, int generation, Guid target, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx))
            await readOnly.ExecuteNonQueryAsync(ct);
        var input = await InvokeAsync(connection, tx, job, token, generation, "READ", target, new { }, ct);
        var lot = input.GetProperty("lotId").GetGuid();
        var company = input.GetProperty("companyId").GetGuid();
        var at = input.GetProperty("snapshotAt").GetDateTimeOffset();
        var substances = input.GetProperty("substanceIds").Deserialize<Guid[]>(Json)!;
        var nodes = input.GetProperty("graph").Deserialize<ResidueNode[]>(Json)!;
        var policy = await policies.ReadAsync(connection, tx, company, substances, at, ct);
        var facts = await evidence.ReadAsync(connection, tx, company,
            nodes.Where(x => x.Kind == "SOURCE").Select(x => x.LotId).ToArray(), substances, at, ct);
        await tx.CommitAsync(ct);
        // Calculation occurs after releasing the read transaction. APPEND checks
        // the basis again under the Ledger lock before any persisted change.
        var results = ResidueEvaluationService.CalculateResults(lot, substances, nodes, facts, policy, at);
        var snapshot = new ResidueEvaluation(Guid.Empty, lot, company, at, null, ResidueCalculator.EngineVersion, results);
        return new(JsonSerializer.SerializeToElement(snapshot, Json), input.GetProperty("revision").GetString()!);
    }

    internal async Task<Guid> AppendAsync(Guid job, Guid token, int generation, Guid target, ResidueWorkSnapshot prepared, CancellationToken ct = default)
    {
        var result = await CallAsync(job, token, generation, "APPEND", target,
            new { revision = prepared.Revision, snapshot = prepared.Snapshot }, ct);
        return result.GetProperty("evaluationId").GetGuid();
    }

    private async Task<JsonElement> CallAsync(Guid job, Guid token, int generation, string action, Guid? target, object payload, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var result = await InvokeAsync(connection, tx, job, token, generation, action, target, payload, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task<JsonElement> InvokeAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid job, Guid token, int generation, string action, Guid? target, object payload, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT grainledger.ledger_residue_job($1,$2,$3,$4,$5,$6)::text", connection, tx);
        command.Parameters.AddWithValue(job); command.Parameters.AddWithValue(token); command.Parameters.AddWithValue(generation);
        command.Parameters.AddWithValue(action);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)target ?? DBNull.Value });
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload, Json));
        using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(ct))!);
        return document.RootElement.Clone();
    }
}
