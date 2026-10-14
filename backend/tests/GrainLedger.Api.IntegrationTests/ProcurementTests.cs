using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ProcurementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Real_port_atomic_commit_replay_and_current_stock_are_company_scoped()
    {
        using var s = await CreateAsync(factory);
        var reference = await GetAsync(s.Purchaser, $"/api/procurement-reference-data?collectionSiteId={s.FacilityId}");
        Assert.Equal(s.CatalogCommodityId, reference.GetProperty("commodity").GetProperty("id").GetGuid());
        Assert.Contains(reference.GetProperty("receivingOptions").EnumerateArray(), x => x.GetProperty("receivingLocationId").GetGuid() == s.LocationId);
        Assert.DoesNotContain(reference.GetProperty("receivingOptions").EnumerateArray(), x => x.GetProperty("receivingLocationId").GetGuid() == s.IsolationLocationId);
        var id = Guid.NewGuid(); var request = s.ReceiptBody(id, ["600", "400"]);
        using var created = await RawAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = (await created.Content.ReadFromJsonAsync<ProcurementResponse>())!;
        Assert.Equal("1000", receipt.TotalNetMassKg);
        using var replay = await RawAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt.EventId, (await replay.Content.ReadFromJsonAsync<ProcurementResponse>())!.EventId);
        var lotId = receipt.Items[0].LotId;
        var protectedLot = await s.LotAsync(lotId, s.Customer);
        Assert.Equal(600m, Quantity(protectedLot));
        var mappedCommodity = protectedLot.GetProperty("commodityId").GetGuid();
        Assert.NotEqual(s.CatalogCommodityId, mappedCommodity);
        Assert.Equal(mappedCommodity, await ScalarAsync<Guid>("SELECT ledger_commodity_id FROM ledger.catalog_commodities WHERE catalog_commodity_id=@id", s.CatalogCommodityId));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM capture.offline_operations WHERE applied_event_id=@id", receipt.EventId));
        await s.TransferAsync(lotId, "10");
        var source = (await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{receipt.Items[0].SourceLotId}"))!;
        Assert.Equal(s.CatalogCommodityId, source.CommodityId);
        Assert.Equal("600", source.InitialNetMassKg);
        Assert.Equal("590", source.CurrentStock.Single(x => x.LocationId == s.LocationId).OnHandKg);
        Assert.Equal("10", source.CurrentStock.Single(x => x.LocationId == s.SecondLocationId).OnHandKg);
        Assert.Equal(600m, source.CurrentStock.Sum(x => decimal.Parse(x.OnHandKg, CultureInfo.InvariantCulture)));
        Assert.Equal(HttpStatusCode.NotFound, (await s.ForeignCustomer.GetAsync($"/api/procurements/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.ForeignCustomer.GetAsync($"/api/source-lots/{source.Id}")).StatusCode);
        Assert.DoesNotContain((await GetAsync(s.ForeignCustomer, "/api/procurements")).GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IProcurementLedgerPort>().GetCurrentStockAsync(s.ForeignOwnerId, lotId, default));
        var wrongSource = s.ReceiptBody(Guid.NewGuid(), ["1"]);
        await RejectedAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, "/api/procurements/commit", wrongSource, HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("0.001", "G", "0.000001")]
    [InlineData("0.000000001", "T", "0.000001")]
    [InlineData("1.123456", "KG", "1.123456")]
    public async Task Real_port_preserves_exact_mass_and_original_precision(string measured, string unit, string kg)
    {
        using var s = await CreateAsync(factory);
        var request = s.ReceiptBody(Guid.NewGuid(), [measured], unit: unit);
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", request);
        var lot = Lots(receipt)[0];
        Assert.Equal(decimal.Parse(kg, CultureInfo.InvariantCulture), Quantity(await s.LotAsync(lot)));
        Assert.Equal(decimal.Parse(measured, CultureInfo.InvariantCulture), await ScalarAsync<decimal>("SELECT (details->>'originalQuantity')::numeric FROM ledger.events WHERE lot_id=@id AND kind='RECEIPT'", lot));
        Assert.Equal(unit, await ScalarAsync<string>("SELECT details->>'originalUnit' FROM ledger.events WHERE lot_id=@id AND kind='RECEIPT'", lot));
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", request);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM ledger.postings WHERE lot_id=@id", lot));
    }

    [Theory]
    [InlineData("0.0001", "G")]
    [InlineData("0.0000000001", "T")]
    [InlineData("1e3", "KG")]
    public async Task Inexact_or_exponent_mass_is_rejected_without_writes(string mass, string unit)
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid();
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", s.ReceiptBody(id, [mass], unit: unit), HttpStatusCode.BadRequest);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM capture.procurements WHERE id=@id", id));
    }

    [Fact]
    public async Task Committed_procurement_package_is_immutable_at_database_boundary()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        var id = receipt.GetProperty("id").GetGuid();
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE capture.procurements SET status='DRAFT' WHERE id=@id",
            "DELETE FROM capture.procurements WHERE id=@id",
            "UPDATE capture.procurement_items SET net_mass_kg=11 WHERE procurement_id=@id",
            "DELETE FROM capture.procurement_items WHERE procurement_id=@id",
            "UPDATE capture.source_lots SET lot_id=gen_random_uuid() WHERE procurement_item_id IN (SELECT id FROM capture.procurement_items WHERE procurement_id=@id)",
            "DELETE FROM capture.source_lots WHERE procurement_item_id IN (SELECT id FROM capture.procurement_items WHERE procurement_id=@id)",
            "UPDATE capture.business_events SET payload='{}' WHERE id IN (SELECT event_id FROM capture.procurements WHERE id=@id)",
            "UPDATE capture.offline_operations SET result_payload='{}' WHERE applied_event_id IN (SELECT event_id FROM capture.procurements WHERE id=@id)"
        })
        {
            await using var command = new NpgsqlCommand(sql, runtime); command.Parameters.AddWithValue("id", id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        Assert.Equal(10m, Quantity(await s.LotAsync(Lots(receipt)[0])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incomplete_committed_event_or_header_package_cannot_commit(bool header)
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid(); var ev = Guid.NewGuid();
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        await using var transaction = await runtime.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO capture.business_events(id,company_id,actor_id,event_type,occurred_at,recorded_at,correlation_id,payload)
            VALUES(@ev,@company,@actor,'PROCUREMENT_COMMITTED',now(),now(),gen_random_uuid(),jsonb_build_object('procurementId',@id));
            """ + (header ? """
            INSERT INTO capture.procurements(id,company_id,code,farmer_id,collection_site_id,received_by,received_at,status,event_id)
            VALUES(@id,@company,'INCOMPLETE',@farmer,@site,@actor,now(),'COMMITTED',@ev);
            """ : ""), runtime, transaction);
        command.Parameters.AddWithValue("ev", ev); command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("company", s.OwnerId); command.Parameters.AddWithValue("actor", s.Accounts["purchaser"].User.Id);
        command.Parameters.AddWithValue("farmer", s.FarmerId); command.Parameters.AddWithValue("site", s.FacilityId);
        await command.ExecuteNonQueryAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM capture.business_events WHERE id=@id", ev));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM capture.procurements WHERE id=@id", id));
    }

    [Fact]
    public async Task Protected_port_rejects_wrong_persisted_actor_and_company_and_incomplete_source()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        var item = receipt.GetProperty("items")[0];
        using var scope = factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IProcurementLedgerPort>();
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var (company, actor, eventId) in new[]
        {
            (s.ForeignOwnerId, s.Accounts["foreignPurchaser"].User.Id, receipt.GetProperty("eventId").GetGuid()),
            (s.OwnerId, s.Accounts["manager"].User.Id, receipt.GetProperty("eventId").GetGuid()),
            (s.OwnerId, s.Accounts["purchaser"].User.Id, Guid.NewGuid())
        })
        {
            await using var transaction = await runtime.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => port.PostReceiptAsync(runtime, transaction, company, actor,
                eventId, receipt.GetProperty("id").GetGuid(), s.CatalogCommodityId, s.FacilityId, s.LocationId,
                [new(item.GetProperty("lotId").GetGuid(), item.GetProperty("lotCode").GetString()!, item.GetProperty("sourceLotId").GetGuid(), item.GetProperty("id").GetGuid(), 10m)], default));
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.NoDataFound, PostgresErrorCodes.CheckViolation });
            await transaction.RollbackAsync();
        }
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM ledger.postings WHERE lot_id=@id", Lots(receipt)[0]));
    }

    [Fact]
    public async Task Submicrosecond_clock_and_input_times_use_same_snapshot_as_PostgreSQL()
    {
        var now = DateTimeOffset.UtcNow;
        var clockTime = new DateTimeOffset(now.UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
        await using var isolated = new ApiFactory { Clock = new FixedClock(clockTime) };
        await isolated.InitializeAsync();
        using var s = await CreateAsync(isolated);
        var request = JsonSerializer.SerializeToNode(s.ReceiptBody(Guid.NewGuid(), ["1"]))!.AsObject();
        request["capturedAt"] = clockTime.ToString("O");
        request["payload"]!["receivedAt"] = clockTime.AddSeconds(-1).ToString("O");
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", request);
        var expected = new DateTimeOffset(clockTime.UtcTicks / 10 * 10, TimeSpan.Zero);
        Assert.Equal(expected, receipt.GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Equal(expected, receipt.GetProperty("recordedAt").GetDateTimeOffset());
        Assert.Equal(expected.AddSeconds(-1), receipt.GetProperty("receivedAt").GetDateTimeOffset());
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements/commit", request);
        Assert.Equal(1m, Quantity(await s.LotAsync(Lots(receipt)[0])));
    }

    [Fact]
    public async Task Concurrent_procurements_complete_with_one_connection_pool_without_lock_holder_starvation()
    {
        using var s = await CreateAsync(factory);
        var config = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var previous = config["ConnectionStrings:GrainLedger"];
        config["ConnectionStrings:GrainLedger"] = new NpgsqlConnectionStringBuilder(factory.RuntimeConnectionString)
            { MaxPoolSize = 1, Timeout = 5, CommandTimeout = 5 }.ConnectionString;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var requests = new[] { s.ReceiptBody(Guid.NewGuid(), ["1"]), s.ReceiptBody(Guid.NewGuid(), ["2"]) };
            var replies = await Task.WhenAll(requests.Select(body => s.Purchaser.PostAsJsonAsync("/api/procurements/commit", body, deadline.Token)));
            foreach (var reply in replies)
            {
                using (reply) Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
            }
        }
        finally { config["ConnectionStrings:GrainLedger"] = previous; }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner); command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
