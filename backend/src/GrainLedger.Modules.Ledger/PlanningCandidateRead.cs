using System.Data;
using System.Text.Json;
using Npgsql;

namespace GrainLedger.Modules.Ledger;

public sealed partial class LedgerService
{
    // One protected bulk read, in one read-only MVCC snapshot. This does not reserve stock.
    public async Task<JsonElement> ReadPlanningCandidatesAsync(Guid ownerId, Guid facilityId, CancellationToken ct = default)
    {
        var (candidates, _) = await ReadPlanningScopeAsync(ownerId, facilityId, false, ct);
        return candidates;
    }

    public Task<(JsonElement Candidates, JsonElement? Compatibilities)> ReadPlanningCandidatesWithCompatibilityAsync(
        Guid ownerId, Guid facilityId, CancellationToken ct = default) =>
        ReadPlanningScopeAsync(ownerId, facilityId, true, ct);

    private async Task<(JsonElement Candidates, JsonElement? Compatibilities)> ReadPlanningScopeAsync(
        Guid ownerId, Guid facilityId, bool withCompatibility, CancellationToken ct)
    {
        if (ownerId == Guid.Empty || facilityId == Guid.Empty)
            throw new LedgerException(400, "Owner and facility are required");
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync(ct);
        async Task<string> ReadAsync(string function)
        {
            await using var command = new NpgsqlCommand($"SELECT ledger.{function}($1,$2,$3)::text", connection, transaction);
            command.CommandTimeout = 30;
            command.Parameters.AddWithValue(actor);
            command.Parameters.AddWithValue(ownerId);
            command.Parameters.AddWithValue(facilityId);
            return (string)(await command.ExecuteScalarAsync(ct) ??
                throw new InvalidOperationException("Ledger returned no planning snapshot."));
        }
        string candidates;
        string? compatibilities = null;
        try
        {
            candidates = await ReadAsync("planning_candidates");
            if (withCompatibility) compatibilities = await ReadAsync("planning_compatibilities");
        }
        catch (PostgresException ex) when (ex.SqlState == "42501" && ex.MessageText == "Candidate access denied")
        {
            throw new LedgerException(403, "Candidate access denied");
        }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "42501")
        {
            throw new LedgerException(503, "CANDIDATE_SCHEMA_NOT_READY");
        }
        catch (PostgresException ex) when (ex.SqlState == "22023")
        {
            throw new LedgerException(400, ex.MessageText);
        }
        catch (PostgresException ex) when (ex.SqlState == "GL014")
        {
            throw new LedgerException(422, "CANDIDATE_SCOPE_LIMIT_EXCEEDED");
        }
        await transaction.CommitAsync(ct);
        return (JsonSerializer.Deserialize<JsonElement>(candidates), compatibilities is null
            ? null : JsonSerializer.Deserialize<JsonElement>(compatibilities));
    }
}
