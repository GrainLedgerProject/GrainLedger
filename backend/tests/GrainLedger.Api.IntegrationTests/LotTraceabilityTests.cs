using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LotTraceabilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<JsonElement> Trace(HttpClient client, Guid lotId, string direction = "forward", string view = "current") =>
        SendAsync(client, HttpMethod.Post, "/api/ledger/trace", new { lotId, direction, view });
    private static Guid[] Ids(JsonElement result) => result.GetProperty("lots").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();

    [Fact]
    public async Task Older_trace_routine_is_rejected_without_committing_a_misleading_success_audit()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("1"))[0];
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        async Task InstallAsync(string resource)
        {
            using var stream = typeof(GrainLedger.Modules.Ledger.AddLotTraceability).Assembly.GetManifestResourceStream(
                $"GrainLedger.Modules.Ledger.Sql.{resource}.sql")!;
            using var reader = new StreamReader(stream);
            await using var command = new NpgsqlCommand((await reader.ReadToEndAsync()).Replace(
                "CREATE FUNCTION ledger.trace_lot", "CREATE OR REPLACE FUNCTION ledger.trace_lot"), owner);
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            await InstallAsync("Traceability");
            using var response = await s.Customer.PostAsJsonAsync("/api/ledger/trace", new { lotId = source });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Contains("upgrade is not installed", await response.Content.ReadAsStringAsync());
            await using var audit = new NpgsqlCommand("SELECT count(*) FROM ledger.events WHERE lot_id=$1 AND kind='TRACEBACK'", owner);
            audit.Parameters.AddWithValue(source);
            Assert.Equal(0L, await audit.ExecuteScalarAsync());
        }
        finally { await InstallAsync("TraceEntryPoints"); }
        Assert.True((await Trace(s.Customer, source)).GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task Traversal_uses_actual_edges_and_retained_exact_source_facts_not_shared_root_or_stock_balance()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("6", "4"); var sources = Lots(receipt);
        var drying = await s.CommitAsync(await s.DraftAsync("DRYING", [(sources[0], "6"), (sources[1], "4")], [("DRIED_PADDY", "5"), ("DRIED_PADDY", "5")]));
        var milling = await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(drying), "5")], [("HEAD_RICE", "3"), ("BROKEN_RICE", "2")]));
        var forward = await Trace(s.Customer, Output(drying));
        Assert.Equal(new[] { Output(drying), Output(milling), Output(milling, 1) }.Order(), Ids(forward).Order());
        Assert.DoesNotContain(Output(drying, 1), Ids(forward));
        var backward = await Trace(s.Customer, Output(milling), "backward");
        Assert.Equal(new[] { sources[0], sources[1], Output(drying), Output(milling) }.Order(), Ids(backward).Order());
        var head = backward.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == Output(milling));
        var contributions = head.GetProperty("contributions").EnumerateArray().ToArray();
        Assert.Equal(3m, contributions.Sum(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture)));
        Assert.Equal("1.800000", contributions.Single(x => x.GetProperty("sourceLotId").GetGuid() == sources[0]).GetProperty("quantityKg").GetString());
        Assert.Equal("60.000000", contributions.Single(x => x.GetProperty("sourceLotId").GetGuid() == sources[0]).GetProperty("percentage").GetString());
        Assert.Equal(2, backward.GetProperty("sources").GetArrayLength());
        var provenance = backward.GetProperty("sources")[0].GetProperty("provenance");
        Assert.Equal(s.PlotSeasonId, provenance.GetProperty("plotSeasonId").GetGuid());
        Assert.Equal(s.FarmerId, provenance.GetProperty("farmerId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(provenance.GetProperty("currentReferenceLabels").GetProperty("plotCode").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(provenance.GetProperty("currentReferenceLabels").GetProperty("seasonName").GetString()));
        Assert.DoesNotContain("+840", backward.GetRawText());
        Assert.DoesNotContain("coordinates", backward.GetRawText());
        Assert.True(backward.GetProperty("complete").GetBoolean());
        Assert.Equal(0m, Quantity(await s.LotAsync(Output(drying))));
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString); await connection.OpenAsync();
        await using var audit = new NpgsqlCommand("SELECT kind FROM ledger.events WHERE id=$1 AND actor_id=$2 AND lot_id=$3", connection);
        audit.Parameters.AddWithValue(backward.GetProperty("traceId").GetGuid()); audit.Parameters.AddWithValue(s.Accounts["customer"].User.Id); audit.Parameters.AddWithValue(Output(milling));
        Assert.Equal("TRACEBACK", await audit.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Tiny_zero_allocations_remain_reachable_and_transfer_does_not_add_edges()
    {
        using var s = await CreateAsync(factory);
        var sources = Lots(await s.ReceiptAsync("0.000001", "0.000001", "0.000001"));
        var op = await s.CommitAsync(await s.DraftAsync("DRYING", sources.Select(x => (x, "0.000001")).ToArray(), [("DRIED_PADDY", "0.000001")], "0.000002"));
        var detail = await Trace(s.Customer, Output(op), "backward");
        var output = detail.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == Output(op));
        var zero = output.GetProperty("contributions").EnumerateArray().First(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture) == 0);
        var forward = await Trace(s.Customer, zero.GetProperty("sourceLotId").GetGuid());
        Assert.Contains(Output(op), Ids(forward));
        await s.TransferAsync(Output(op), "0.000001");
        var after = await Trace(s.Customer, zero.GetProperty("sourceLotId").GetGuid());
        Assert.Equal(forward.GetProperty("edges").GetRawText(), after.GetProperty("edges").GetRawText());
        Assert.Equal(2, after.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == Output(op)).GetProperty("positions").GetArrayLength());
    }

    [Fact]
    public async Task Current_excludes_superseded_operation_while_history_and_correction_link_remain()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1"));
        var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "8")], "2");
        var correction = await s.CorrectionAsync(original, replacement);
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/commit", new { replacementOperationId = replacement.GetProperty("id").GetGuid() });
        var current = await Trace(s.Customer, source);
        Assert.Contains(Output(replacement), Ids(current)); Assert.DoesNotContain(Output(original), Ids(current));
        var history = await Trace(s.Customer, source, view: "history");
        Assert.Contains(Output(original), Ids(history)); Assert.Contains(Output(replacement), Ids(history));
        Assert.Equal(correction, history.GetProperty("corrections")[0].GetProperty("id").GetGuid());
        Assert.Contains("SUPERSEDED", history.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == Output(original)).GetProperty("restrictions").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain(history.GetProperty("edges").EnumerateArray(), x => x.GetProperty("outputLotId").GetGuid() == source);
    }

    [Fact]
    public async Task Trace_enforces_persisted_action_and_owner_without_granting_admin_business_stock_access()
    {
        using var s = await CreateAsync(factory); var source = Lots(await s.ReceiptAsync("1"))[0];
        var (exportUser, exportSession) = await factory.SeedUserAsync(Roles.ExportStaff, s.OwnerId);
        using var export = factory.CreateRealCookieClient(exportUser, exportSession); await CsrfAsync(export);
        var result = await Trace(export, source);
        Assert.False(result.GetProperty("sourceContributionsVisible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("lots")[0].GetProperty("contributions").ValueKind);
        Assert.Empty(result.GetProperty("sources").EnumerateArray());
        foreach (var client in new[] { s.Operator, s.Manager, s.Purchaser, s.ForeignCustomer, s.Accounts["pending"].Client })
        {
            using var denied = await client.PostAsJsonAsync("/api/ledger/trace", new { lotId = source });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var denied = await export.PostAsJsonAsync("/api/ledger/trace", new { lotId = source, direction = "backward" }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(source, Ids(await Trace(s.Admin, source, "backward")));
        using (var stock = await s.Admin.GetAsync($"/api/ledger/lots/{source}")) Assert.Equal(HttpStatusCode.Forbidden, stock.StatusCode);
        using (var invalid = await s.Customer.PostAsJsonAsync("/api/ledger/trace", new { lotId = source, direction = "sideways" })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(x => x.Id == exportUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, Roles.PurchasingStaff));
        using (var denied = await export.PostAsJsonAsync("/api/ledger/trace", new { lotId = source })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var connection = new NpgsqlConnection(factory.RuntimeConnectionString); await connection.OpenAsync();
        await using var direct = new NpgsqlCommand("SELECT ledger.trace_lot($1,$2,'forward','current')", connection);
        direct.Parameters.AddWithValue(s.Accounts["operator"].User.Id); direct.Parameters.AddWithValue(source);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await direct.ExecuteScalarAsync())).SqlState);
    }

    [Fact]
    public async Task Full_trace_exceeds_list_page_size_and_missing_or_pending_identity_never_creates_stock()
    {
        using var s = await CreateAsync(factory); var source = Lots(await s.ReceiptAsync("110"))[0];
        var op = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "110")], Enumerable.Range(0, 110).Select(_ => ("DRIED_PADDY", "1")).ToArray()));
        var result = await Trace(s.Customer, source);
        Assert.Equal(111, result.GetProperty("lotCount").GetInt32());
        Assert.Equal(110, result.GetProperty("edgeCount").GetInt32());
        Assert.Contains(Output(op, 109), Ids(result));
        var pending = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["2"]));
        using (var denied = await s.Customer.PostAsJsonAsync("/api/ledger/trace", new { lotId = Lots(pending)[0] })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await s.Customer.PostAsJsonAsync("/api/ledger/trace", new { lotId = Guid.NewGuid() })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var absent = await s.Customer.GetAsync($"/api/ledger/lots/{Lots(pending)[0]}"); Assert.Equal(HttpStatusCode.Forbidden, absent.StatusCode);
    }

    [Fact]
    public async Task Immutable_source_snapshot_survives_archived_reference_and_legacy_is_explicit()
    {
        using var s = await CreateAsync(factory); var source = Lots(await s.ReceiptAsync("2"))[0];
        var before = (await Trace(s.Customer, source, "backward")).GetProperty("sources")[0].GetProperty("provenance");
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        // Simulate changed/archived reference labels in this disposable database only.
        await using (var labels = new NpgsqlCommand("""
            WITH farmer AS (UPDATE capture.farmers SET name='Current farmer label' WHERE id=$1 RETURNING id),
            plot AS (UPDATE capture.plots SET code='TRACE-CURRENT-PLOT',is_deleted=true WHERE id=$2 RETURNING id)
            UPDATE capture.crop_seasons SET name='Current season label',is_deleted=true WHERE id=$3;
            """, connection))
        {
            labels.Parameters.AddWithValue(s.FarmerId);
            labels.Parameters.AddWithValue(before.GetProperty("plotId").GetGuid());
            labels.Parameters.AddWithValue(s.SeasonId);
            await labels.ExecuteNonQueryAsync();
        }
        await using (var archive = new NpgsqlCommand("UPDATE capture.plot_seasons SET is_deleted=true WHERE id=$1", connection))
        { archive.Parameters.AddWithValue(s.PlotSeasonId); await archive.ExecuteNonQueryAsync(); }
        var result = await Trace(s.Customer, source, "backward");
        Assert.Equal("RECORDED", result.GetProperty("sources")[0].GetProperty("status").GetString());
        var after = result.GetProperty("sources")[0].GetProperty("provenance");
        foreach (var field in new[] { "farmerId", "plotId", "seasonId", "plotSeasonId", "riceVariety", "expectedKg", "originalValue", "originalUnit" })
            Assert.Equal(before.GetProperty(field).GetRawText(), after.GetProperty(field).GetRawText());
        Assert.Equal("Current farmer label", after.GetProperty("currentReferenceLabels").GetProperty("farmerName").GetString());
        Assert.Equal("TRACE-CURRENT-PLOT", after.GetProperty("currentReferenceLabels").GetProperty("plotCode").GetString());
        Assert.Equal("Current season label", after.GetProperty("currentReferenceLabels").GetProperty("seasonName").GetString());
        // Deliberately isolated legacy fixture: no official Capture record is fabricated.
        var legacy = Guid.NewGuid();
        await using (var create = new NpgsqlCommand("""
            WITH created AS (
                INSERT INTO ledger.lots(id,code,code_origin_id,owner_id,commodity_id,stage,product_identity)
                VALUES($1,$1::text,ledger.register_code($1::text,$1),$2,$3,'RAW_PADDY','RAW_PADDY') RETURNING id
            ) INSERT INTO ledger.contributions SELECT id,id,1 FROM created
            """, connection))
        { create.Parameters.AddWithValue(legacy); create.Parameters.AddWithValue(s.OwnerId); create.Parameters.AddWithValue(s.CommodityId); await create.ExecuteNonQueryAsync(); }
        result = await Trace(s.Customer, legacy, "backward");
        Assert.Equal("LEGACY_SOURCE_UNAVAILABLE", result.GetProperty("sources")[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("sources")[0].GetProperty("provenance").ValueKind);
    }

    [Fact]
    public async Task Branches_rejoin_without_duplicates_or_reintroducing_a_sibling_as_an_ancestor()
    {
        using var s = await CreateAsync(factory); var source = Lots(await s.ReceiptAsync("10"))[0];
        var split = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "5"), ("DRIED_PADDY", "5")]));
        var joined = await s.CommitAsync(await s.DraftAsync("MERGE", [(Output(split), "5"), (Output(split, 1), "5")], [("DRIED_PADDY", "10")]));
        var all = await Trace(s.Customer, source);
        Assert.Equal(4, Ids(all).Length); Assert.Equal(4, Ids(all).Distinct().Count());
        var branch = await Trace(s.Customer, Output(split));
        Assert.Equal(new[] { Output(split), Output(joined) }.Order(), Ids(branch).Order());
        var backward = await Trace(s.Customer, Output(joined), "backward");
        Assert.Equal(4, Ids(backward).Length);
        var composition = backward.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == Output(joined)).GetProperty("contributions");
        Assert.Single(composition.EnumerateArray()); Assert.Equal("10.000000", composition[0].GetProperty("quantityKg").GetString());
    }

    [Fact]
    public async Task Oversized_graph_fails_without_partial_result_or_success_audit()
    {
        using var s = await CreateAsync(factory); var source = Lots(await s.ReceiptAsync("5000"))[0];
        // Large synthetic graph fixture in the isolated owner database. Real posting paths are
        // exercised above; this fixture targets response completeness at the interactive guard.
        var draft = await s.DraftAsync("DRYING", [(source, "5000")], Enumerable.Range(0, 5000).Select(_ => ("DRIED_PADDY", "1")).ToArray());
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand("UPDATE ledger.operations SET status='COMMITTED' WHERE id=$1", connection, transaction))
        { update.Parameters.AddWithValue(draft.GetProperty("id").GetGuid()); await update.ExecuteNonQueryAsync(); }
        await using (var lots = new NpgsqlCommand("""
            INSERT INTO ledger.lots(id,code,code_origin_id,owner_id,commodity_id,stage,product_identity,operation_id)
            SELECT (o->>'id')::uuid,o->>'code',ledger.register_code(o->>'code',(o->>'id')::uuid),
                op.owner_id,(o->>'commodityId')::uuid,o->>'stage',o->>'productIdentity',op.id
            FROM ledger.operations op CROSS JOIN LATERAL jsonb_array_elements(op.content->'outputs') o WHERE op.id=$1
            """, connection, transaction))
        { lots.Parameters.AddWithValue(draft.GetProperty("id").GetGuid()); await lots.ExecuteNonQueryAsync(); }
        await transaction.CommitAsync();
        using var response = await s.Customer.PostAsJsonAsync("/api/ledger/trace", new { lotId = source });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("no complete result", await response.Content.ReadAsStringAsync());
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM ledger.events WHERE lot_id=$1 AND kind='TRACEBACK'", connection);
        audit.Parameters.AddWithValue(source); Assert.Equal(0L, await audit.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Concurrent_processing_never_combines_graph_and_stock_from_different_snapshots()
    {
        using var s = await CreateAsync(factory); var source = Lots(await s.ReceiptAsync("100"))[0];
        var drafts = new List<JsonElement>();
        for (var i = 0; i < 10; i++) drafts.Add(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var commits = Task.WhenAll(drafts.Select(draft => s.CommitAsync(draft)));
        var reads = Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Trace(s.Customer, source)));
        await Task.WhenAll(commits, reads);
        foreach (var result in (await reads).Append(await Trace(s.Customer, source)))
        {
            var lots = result.GetProperty("lots").EnumerateArray().ToArray();
            var edges = result.GetProperty("edges").EnumerateArray().ToArray();
            Assert.Equal(100m, lots.Sum(lot => lot.GetProperty("positions").EnumerateArray().Sum(p => decimal.Parse(p.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture))));
            Assert.Equal(lots.Length - 1, edges.Length);
            Assert.All(edges, edge => Assert.Contains(edge.GetProperty("outputLotId").GetGuid(), Ids(result)));
        }
    }
}
