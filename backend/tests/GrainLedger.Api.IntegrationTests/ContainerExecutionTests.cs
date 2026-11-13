using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ContainerExecutionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Direct_approved_stock_loads_partially_closes_once_and_retains_an_immutable_manifest()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var initial = await f.Detail();
        Assert.Equal("PASS", initial.GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        var first = f.Load(1, "4"); var loaded = await f.Post("loads", first);
        Assert.Equal("4.000000", loaded.GetProperty("loadedKg").GetString());
        Assert.Equal("LOADING", loaded.GetProperty("status").GetString());
        Assert.Equal(loaded.GetRawText(), (await f.Post("loads", first)).GetRawText());
        Assert.Equal(6m, Quantity(await f.Stock.LotAsync(f.LotId)));
        var second = await f.Post("loads", f.Load(loaded.GetProperty("version").GetInt32(), "6"));
        Assert.Equal("10.000000", second.GetProperty("loadedKg").GetString());
        Assert.Equal(0m, Quantity(await f.Stock.LotAsync(f.LotId)));
        var metadata = await f.Post("metadata", new ContainerMetadataRequest(Guid.NewGuid(), second.GetProperty("version").GetInt32(), "SYNTHETIC-C1", "SEAL-1"));
        var closing = new ContainerCloseRequest(Guid.NewGuid(), metadata.GetProperty("version").GetInt32());
        var closed = await f.Post("close", closing);
        Assert.Equal("CLOSED", closed.GetProperty("status").GetString());
        Assert.Equal(closed.GetRawText(), (await f.Post("close", closing)).GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(closed.GetProperty("version").GetInt32(), "1"))).StatusCode);
        var exportDossier = await GetAsync(f.Export, f.Route + "/dossier");
        Assert.True(exportDossier.GetProperty("redacted").GetBoolean());
        Assert.False(exportDossier.TryGetProperty("sourceContributions", out _));
        Assert.False(exportDossier.GetProperty("assessment").TryGetProperty("evidence", out _));
        var dossier = await GetAsync(f.Stock.Customer, f.Route + "/dossier");
        Assert.Equal("10.000000", Assert.Single(dossier.GetProperty("sourceContributions").EnumerateArray()).GetProperty("quantityKg").GetString());
        var decision = await GetAsync(f.Export, $"/api/blend-plans/{f.PlanId}/decision");
        Assert.Equal("CONSUMED", decision.GetProperty("reservation").GetProperty("status").GetString());
        var held = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/holds", new ContainerHoldRequest(Guid.NewGuid(), closed.GetProperty("version").GetInt32(), "Late synthetic finding"));
        Assert.Single(held.GetProperty("holds").EnumerateArray());
        Assert.Equal(dossier.GetRawText(), (await GetAsync(f.Stock.Customer, f.Route + "/dossier")).GetRawText());
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var alter = new NpgsqlCommand("UPDATE grainledger.ledger_container_load_lines SET quantity_kg=1", owner);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => alter.ExecuteNonQueryAsync()); Assert.Equal("23514", immutable.SqlState);
    }
    [Fact]
    public async Task Source_and_container_tracing_use_actual_mass_and_deny_foreign_or_export_backward_reads()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var empty = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.False(empty.GetProperty("complete").GetBoolean());
        Assert.Contains(empty.GetProperty("findings").EnumerateArray(), x => x.GetString() == "NO_ACTUAL_LOADING");
        await f.Post("loads", f.Load(1, "4"));
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.True(trace.GetProperty("complete").GetBoolean());
        var source = Assert.Single(trace.GetProperty("sourceContributions").EnumerateArray());
        Assert.Equal(f.SourceId, source.GetProperty("sourceLotId").GetGuid());
        Assert.Equal("4.000000", source.GetProperty("quantityKg").GetString());
        Assert.Equal("100", source.GetProperty("percentage").GetString());
        Assert.Equal(f.Stock.PlotSeasonId, source.GetProperty("provenance").GetProperty("plotSeasonId").GetGuid());
        Assert.NotEqual(Guid.Empty, source.GetProperty("provenance").GetProperty("plotId").GetGuid());
        foreach (var client in new[] { f.Export, f.Stock.ForeignCustomer, f.Stock.Purchaser, f.Stock.Operator })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(f.Route + "/trace", new { })).StatusCode);
        var forward = await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.SourceId, direction = "forward" });
        var target = Assert.Single(forward.GetProperty("actualContainers").EnumerateArray());
        Assert.Equal(f.ContainerId, target.GetProperty("containerId").GetGuid());
        Assert.Equal("4.000000", target.GetProperty("linkedLoadedKg").GetString());
        var admin = await SendAsync(f.Stock.Admin, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.SourceId, direction = "forward" });
        Assert.Single(admin.GetProperty("actualContainers").EnumerateArray());
        var export = await SendAsync(f.Export, HttpMethod.Post, "/api/ledger/trace", new { lotId = f.SourceId, direction = "forward" });
        Assert.Single(export.GetProperty("actualContainers").EnumerateArray());
        Assert.Empty(export.GetProperty("sources").EnumerateArray());
        Assert.Equal(6m, Quantity(await f.Stock.LotAsync(f.LotId)));
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Stock.ForeignCustomer.GetAsync(f.Route)).StatusCode);
    }

    [Fact]
    public async Task Container_hold_and_current_lot_or_quality_changes_block_without_consuming_stock()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Export.PostAsJsonAsync(f.Route + "/holds", new ContainerHoldRequest(Guid.NewGuid(), 1, "Not a manager"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Export.PostAsJsonAsync(f.Route + "/close", new ContainerCloseRequest(Guid.NewGuid(), 1))).StatusCode);
        await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/planning-quality-observations", new
        {
            id = Guid.NewGuid(),
            ownerId = f.Stock.OwnerId,
            facilityId = f.Stock.FacilityId,
            lotId = f.LotId,
            attributeId = f.QualityAttributeId,
            versionNo = 2,
            measuredValue = "15",
            unit = "%",
            measuredAt = DateTimeOffset.UtcNow,
            evidenceId = f.Stock.EvidenceId,
            reportReference = "Synthetic later QC",
            reviewedBy = "Reviewer",
            reviewAuthority = "Authority",
            reviewedAt = DateTimeOffset.UtcNow,
            subjectConfirmed = true
        });
        var blocked = await f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(1, "4"));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("readiness").GetProperty("findings").EnumerateArray(), x => x.GetString() == "QUALITY_LIMIT");
        var held = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/holds", new ContainerHoldRequest(Guid.NewGuid(), 1, "Synthetic blocked container"));
        var version = held.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(version, "4"))).StatusCode);
        Assert.Equal(10m, Quantity(await f.Stock.LotAsync(f.LotId)));
        var decision = await GetAsync(f.Export, $"/api/blend-plans/{f.PlanId}/decision");
        Assert.Equal("0", decision.GetProperty("reservation").GetProperty("consumedKg").GetString());
        var history = await GetAsync(f.Stock.Customer, f.Route + "/history?pageSize=1");
        Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Export.GetAsync(f.Route + "/history?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_loads_and_changed_replay_cannot_overspend_and_failure_after_consume_rolls_back()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var request = f.Load(1, "4");
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using (var inject = new NpgsqlCommand("CREATE FUNCTION grainledger.ledger_test_container_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF EXISTS(SELECT 1 FROM grainledger.ledger_events WHERE id=NEW.event_id AND kind='CONTAINER_LOADING') THEN RAISE EXCEPTION 'Synthetic injected posting failure' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER ledger_test_container_fail BEFORE INSERT ON grainledger.ledger_postings FOR EACH ROW EXECUTE FUNCTION grainledger.ledger_test_container_fail();", owner)) await inject.ExecuteNonQueryAsync();
        try { Assert.Equal(HttpStatusCode.Conflict, (await f.Export.PostAsJsonAsync(f.Route + "/loads", request)).StatusCode); }
        finally { await using var clear = new NpgsqlCommand("DROP TRIGGER ledger_test_container_fail ON grainledger.ledger_postings; DROP FUNCTION grainledger.ledger_test_container_fail()", owner); await clear.ExecuteNonQueryAsync(); }
        var before = await f.Detail(); Assert.Equal("0.000000", before.GetProperty("container").GetProperty("loadedKg").GetString());
        Assert.Equal("PLANNED", before.GetProperty("container").GetProperty("status").GetString());
        Assert.Equal(10m, Quantity(await f.Stock.LotAsync(f.LotId)));
        var responses = await Task.WhenAll(f.Export.PostAsJsonAsync(f.Route + "/loads", request), f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(1, "7")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var winner = await responses.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, (await f.Export.PostAsJsonAsync(f.Route + "/loads", request with { Lines = [new(f.LotId, f.Stock.LocationId, "11")] })).StatusCode);
        var decision = await GetAsync(f.Export, $"/api/blend-plans/{f.PlanId}/decision");
        Assert.Equal(decimal.Parse(winner.GetProperty("loadedKg").GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(decision.GetProperty("reservation").GetProperty("consumedKg").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task Permission_revoked_while_waiting_for_loading_lock_is_rechecked_before_write()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var tx = await owner.BeginTransactionAsync();
        await using (var locked = new NpgsqlCommand("SELECT grainledger.ledger_lock_writes()", owner, tx)) await locked.ExecuteNonQueryAsync();
        var pending = f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(1, "4"));
        await using var observer = new NpgsqlConnection(factory.OwnerConnectionString); await observer.OpenAsync();
        var waiting = false;
        for (var i = 0; i < 200; i++)
        {
            await using var probe = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE '%pg_advisory_xact_lock(714928352)%' AND wait_event='advisory')", observer);
            if (Equals(await probe.ExecuteScalarAsync(), true)) { waiting = true; break; }
            await Task.Delay(20);
        }
        Assert.True(waiting);
        await using (var revoke = new NpgsqlCommand("UPDATE grainledger.identity_users SET \"Role\"='CustomerManager' WHERE \"Id\"=$1", observer))
        { revoke.Parameters.AddWithValue(f.ActorId); await revoke.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await pending).StatusCode);
        Assert.Equal(10m, Quantity(await f.Stock.LotAsync(f.LotId)));
    }

    [Fact]
    public async Task UT14_exact_source_allocation_conserves_25000kg_and_private_helper_has_no_runtime_grant()
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        var one = Guid.Parse("00000000-0000-4000-8000-000000000001"); var two = Guid.Parse("00000000-0000-4000-8000-000000000002");
        await using var calc = new NpgsqlCommand("SELECT source_lot_id,quantity_kg FROM grainledger.ledger_container_allocate_sources(25000,70000,$1) ORDER BY source_lot_id", owner);
        calc.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new[] { new { sourceLotId = one, quantityKg = "11600" }, new { sourceLotId = two, quantityKg = "58400" } }));
        var results = new Dictionary<Guid, decimal>(); await using (var rows = await calc.ExecuteReaderAsync()) { while (await rows.ReadAsync()) results[rows.GetGuid(0)] = rows.GetDecimal(1); }
        Assert.Equal(25000m, results.Values.Sum()); Assert.Equal(4142.857143m, results[one]); Assert.Equal(20857.142857m, results[two]);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        await using var denied = new NpgsqlCommand("SELECT grainledger.ledger_container_allocate_sources(1,1,'[]'::jsonb)", runtime);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteScalarAsync())).SqlState);
    }

    [Fact]
    public async Task Preproduced_multisource_direct_stock_keeps_exact_complement_across_partial_loads()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, preBlended: true);
        var first = await f.Post("loads", f.Load(1, "4.000001"));
        var second = await f.Post("loads", f.Load(first.GetProperty("version").GetInt32(), "5.999999"));
        Assert.Equal("10.000000", second.GetProperty("loadedKg").GetString());
        Assert.Equal(10m, Quantity(await f.Stock.LotAsync(f.LotId)));
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.Equal(2, trace.GetProperty("sourceContributions").GetArrayLength());
        Assert.Equal(10m, trace.GetProperty("sourceContributions").EnumerateArray().Sum(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var balances = new NpgsqlCommand("SELECT sum(quantity_kg) FROM grainledger.ledger_container_source_balances WHERE lot_id=$1", owner); balances.Parameters.AddWithValue(f.LotId);
        Assert.Equal(10m, await balances.ExecuteScalarAsync());
        // Original creation facts are retained, not rewritten as remaining stock.
        await using var creation = new NpgsqlCommand("SELECT sum(quantity_kg) FROM grainledger.ledger_contributions WHERE lot_id=$1", owner); creation.Parameters.AddWithValue(f.LotId);
        Assert.Equal(20m, await creation.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Late_source_HOLD_blocks_continuation_without_automatic_container_HOLD_or_stock_change()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var first = await f.Post("loads", f.Load(1, "4"));
        await f.Stock.HoldAsync(f.SourceId);
        var result = await f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(first.GetProperty("version").GetInt32(), "6"));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        var body = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("readiness").GetProperty("findings").EnumerateArray(), x => x.GetString() == "LOT_RESTRICTED");
        var detail = (await f.Detail()).GetProperty("container");
        Assert.Equal("4.000000", detail.GetProperty("loadedKg").GetString());
        Assert.Empty(detail.GetProperty("holds").EnumerateArray());
        Assert.Equal(6m, Quantity(await f.Stock.LotAsync(f.LotId)));
    }

}
