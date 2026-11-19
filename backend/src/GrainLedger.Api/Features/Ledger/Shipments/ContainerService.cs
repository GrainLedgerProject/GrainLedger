using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace GrainLedger.Api.Features.Ledger;

public sealed partial class LedgerService
{
    public async Task<JsonElement> ContainerAsync(string action, JsonObject payload, CancellationToken ct = default)
    {
        var actor = await ActorAsync(ct);
        await using var connection = await OpenAsync(ct);
        try
        {
            var value = await ExecuteAsync(connection, null, "SELECT grainledger.ledger_container_action($1,$2,$3)::text", ct, actor, action, payload);
            return JsonSerializer.Deserialize<JsonElement>((string)value!);
        }
        catch (PostgresException ex) when (ex.SqlState == "42883")
        { throw new LedgerException(503, "Container preparation is not available on this deployment."); }
    }
}
