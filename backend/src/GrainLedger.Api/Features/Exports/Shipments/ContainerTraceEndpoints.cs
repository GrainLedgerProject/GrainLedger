using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GrainLedger.Api.Features.Exports;

public static partial class ExportModule
{
    private static void MapContainerTrace(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/export-containers/{id:guid}/execution/trace", async (Guid id, ICurrentAccess access,
            LedgerService ledger, AppDbContext db, SourceProvenanceReader sources, NpgsqlDataSource dataSource, CancellationToken ct) =>
        {
            if (!await access.HasPermissionAsync(Permissions.TraceReadTenant, ct) && !await access.HasPermissionAsync(Permissions.TraceReadAll, ct)) return Results.Forbid();
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                var actor = await ledger.ActorAsync(ct);
                var trace = await ReadExecutionAsync(actor, id, "trace", db, ct);
                var root = JsonNode.Parse(trace.GetRawText())!.AsObject();
                var rows = root["sourceContributions"]!.AsArray();
                var sourceIds = rows.Select(x => x!["sourceLotId"]!.GetValue<Guid>()).ToArray();
                var provenance = sourceIds.Length == 0 ? [] : await sources.ReadAsync((NpgsqlConnection)db.Database.GetDbConnection(),
                    (NpgsqlTransaction)tx.GetDbTransaction(), actor, trace.GetProperty("companyId").GetGuid(), sourceIds, ct);
                var mapping = provenance.ToDictionary(x => x.LotId);
                var total = decimal.Parse(trace.GetProperty("loadedKg").GetString()!, CultureInfo.InvariantCulture);
                var complete = total > 0; var unavailable = new JsonArray();
                foreach (var row in rows)
                {
                    var source = row!.AsObject(); var sourceId = source["sourceLotId"]!.GetValue<Guid>();
                    var kg = decimal.Parse(source["quantityKg"]!.GetValue<string>(), CultureInfo.InvariantCulture);
                    mapping.TryGetValue(sourceId, out var facts);
                    source["percentage"] = total == 0 ? "0" : (kg * 100 / total).ToString("0.######", CultureInfo.InvariantCulture);
                    source["status"] = facts is null ? "SOURCE_PROVENANCE_UNAVAILABLE" : "RECORDED";
                    source["provenance"] = JsonSerializer.SerializeToNode(facts, ContainerJson);
                    if (facts is null) { complete = false; unavailable.Add(sourceId.ToString()); }
                }
                root["complete"] = complete; root["basis"] = "ACTUAL_LOADING";
                root["findings"] = total == 0 ? new JsonArray("NO_ACTUAL_LOADING") : unavailable.Count > 0 ? new JsonArray("SOURCE_PROVENANCE_UNAVAILABLE") : new JsonArray();
                await using var authority = await dataSource.OpenConnectionAsync(ct);
                await using var check = new NpgsqlCommand("SELECT grainledger.ledger_role($1) IN ('CustomerManager','Administrator') AND (grainledger.ledger_role($1)='Administrator' OR grainledger.ledger_own($1,$2))", authority);
                check.Parameters.AddWithValue(actor); check.Parameters.AddWithValue(trace.GetProperty("companyId").GetGuid());
                if (!Equals(await check.ExecuteScalarAsync(ct), true)) return Results.Forbid();
                await tx.CommitAsync(ct); return Results.Ok(root);
            }
            catch (PostgresException ex) { return ContainerDatabaseError(ex); }
        }).RequireAuthorization().AddEndpointFilter<NoStoreFilter>().AddEndpointFilter<MutationAntiforgeryFilter>();
    }
}
