using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ReceivingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Measurements(Guid id) => $"/api/ledger/receiving/{id}/measurements";
    private static string Reviews(Guid id) => $"/api/ledger/receiving/{id}/reviews";
    private static object Review(JsonElement state, string action, Guid evidence, Guid? operationId = null) => new
    {
        operationId = operationId ?? Guid.NewGuid(), expectedReceivingVersion = state.GetProperty("receivingVersion").GetInt32(),
        measurementSetId = state.GetProperty("latestMeasurementSetId").GetGuid(), action,
        reason = "Physical evidence reviewed; immutable investigation record", evidenceId = evidence
    };
    private static Guid Id(JsonElement receipt) => receipt.GetProperty("id").GetGuid();
    private static async Task<JsonElement> RegisterAsync(LedgerScenario s, params string[] kg) =>
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), kg));

    [Fact]
    public async Task Pending_queue_exact_arrival_credits_once_without_leaking_farmer_contacts()
    {
        using var s = await CreateAsync(factory);
        var receipt = await RegisterAsync(s, "6", "4"); var id = Id(receipt);
        Assert.Equal("PENDING_ARRIVAL", receipt.GetProperty("status").GetString());
        Assert.Equal(0L, await CountAsync("ledger.lots", "receipt_id", id));
        var queue = await GetAsync(s.Operator, $"/api/ledger/receiving-tasks?facilityId={s.FacilityId}");
        var task = queue.GetProperty("items").EnumerateArray().Single(x => Id(x) == id);
        Assert.Equal(s.OwnerId, task.GetProperty("ownerCompanyId").GetGuid());
        Assert.Equal(s.FacilityId, task.GetProperty("facilityId").GetGuid());
        Assert.False(task.TryGetProperty("farmerId", out _)); Assert.False(task.TryGetProperty("farmerName", out _));
        Assert.DoesNotContain("+84000000000", task.GetRawText());
        Assert.Equal("0", task.GetProperty("usableStockKg").GetString());
        var body = MeasurementBody(receipt); var route = Measurements(id);
        var accepted = await SendAsync(s.Operator, HttpMethod.Post, route, body);
        Assert.Equal("RECEIVED_MATCHED", accepted.GetProperty("status").GetString());
        Assert.Equal(1, accepted.GetProperty("receivingVersion").GetInt32());
        Assert.Equal(10m, Decimal(accepted.GetProperty("usableStockKg")));
        var replay = await SendAsync(s.Operator, HttpMethod.Post, route, body);
        Assert.Equal(accepted.GetRawText(), replay.GetRawText());
        Assert.Equal(2L, await CountAsync("ledger.lots", "receipt_id", id));
        Assert.Equal(1L, await CountAsync("ledger.arrival_measurement_sets", "procurement_id", id));
        await RejectedAsync(s.Operator, HttpMethod.Post, route, MeasurementBody(receipt), HttpStatusCode.Conflict);
        var changed = JsonSerializer.SerializeToNode(body)!.AsObject(); changed["observations"] = "different observations";
        await RejectedAsync(s.Operator, HttpMethod.Post, route, changed, HttpStatusCode.Conflict);
        Assert.Equal("RECEIVED_MATCHED", (await GetAsync(s.Purchaser, $"/api/procurements/{id}")).GetProperty("status").GetString());
        using var obsolete = await s.Purchaser.PostAsJsonAsync("/api/procurements/commit", s.ReceiptBody(Guid.NewGuid(), ["1"]));
        Assert.Equal(HttpStatusCode.NotFound, obsolete.StatusCode);
    }

    [Fact]
    public async Task Shortage_requires_manager_scoped_evidence_and_posts_actual_preserving_source()
    {
        using var s = await CreateAsync(factory);
        var receipt = await RegisterAsync(s, "60", "40"); var id = Id(receipt);
        var observed = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, actual: ["55", "40"]));
        Assert.Equal("AWAITING_MANAGER_REVIEW", observed.GetProperty("status").GetString());
        Assert.Equal(0L, await CountAsync("ledger.lots", "receipt_id", id));
        await RejectedAsync(s.Operator, HttpMethod.Post, Reviews(id), Review(observed, "APPROVE_SHORTAGE", s.EvidenceId), HttpStatusCode.Forbidden);
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(observed, "APPROVE_SHORTAGE", Guid.NewGuid()), HttpStatusCode.Forbidden);
        var body = Review(observed, "APPROVE_SHORTAGE", s.EvidenceId);
        var accepted = await SendAsync(s.Manager, HttpMethod.Post, Reviews(id), body);
        Assert.Equal("RECEIVED_SHORTAGE_APPROVED", accepted.GetProperty("status").GetString());
        Assert.Equal(95m, Decimal(accepted.GetProperty("usableStockKg")));
        Assert.Equal(55m, Quantity(await s.LotAsync(Lots(receipt)[0])));
        var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()}");
        Assert.Equal("60", source!.InitialNetMassKg); Assert.Equal("55", source.CurrentStock.Single().OnHandKg);
        Assert.Equal(55m, await ScalarAsync<decimal>("SELECT quantity_kg FROM ledger.contributions WHERE lot_id=@id", Lots(receipt)[0]));
        var replay = await SendAsync(s.Manager, HttpMethod.Post, Reviews(id), body);
        Assert.Equal(accepted.GetRawText(), replay.GetRawText());
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(observed, "APPROVE_SHORTAGE", s.EvidenceId), HttpStatusCode.Conflict);
        Assert.Equal(1L, await CountAsync("ledger.arrival_reviews", "procurement_id", id));
        var stale = JsonSerializer.SerializeToNode(body)!.AsObject(); stale["reason"] = "Changed explanation";
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), stale, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Surplus_blocked_investigation_can_request_remeasurement_and_retains_observations()
    {
        using var s = await CreateAsync(factory);
        var receipt = await RegisterAsync(s, "10", "20"); var id = Id(receipt);
        var firstBody = MeasurementBody(receipt, actual: ["11", "19"], observations: "First full arrival observations");
        var first = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), firstBody);
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(first, "APPROVE_SHORTAGE", s.EvidenceId), HttpStatusCode.Conflict);
        var blocked = await SendAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(first, "KEEP_BLOCKED", s.EvidenceId));
        Assert.Equal("BLOCKED_UNRESOLVED", blocked.GetProperty("status").GetString());
        Assert.Equal(0L, await CountAsync("ledger.lots", "receipt_id", id));
        await RejectedAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, version: 2), HttpStatusCode.Conflict);
        var requested = await SendAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(blocked, "REQUEST_REMEASUREMENT", s.EvidenceId));
        Assert.Equal("REMEASUREMENT_REQUESTED", requested.GetProperty("status").GetString());
        var replayFirst = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), firstBody);
        Assert.Equal(first.GetRawText(), replayFirst.GetRawText());
        var exact = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, version: 3, observations: "Second full exact observations"));
        Assert.Equal("RECEIVED_MATCHED", exact.GetProperty("status").GetString());
        Assert.Equal(2, exact.GetProperty("measurementSets").GetArrayLength());
        Assert.Equal("First full arrival observations", exact.GetProperty("measurementSets")[0].GetProperty("observations").GetString());
        Assert.Equal(2, exact.GetProperty("reviews").GetArrayLength());
        Assert.Equal(4, exact.GetProperty("receivingVersion").GetInt32());
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(first, "APPROVE_SHORTAGE", s.EvidenceId), HttpStatusCode.Conflict);
        Assert.Equal(30m, Decimal(exact.GetProperty("usableStockKg")));
    }

    [Fact]
    public async Task Complete_line_set_positive_mass_version_and_actor_scope_fail_closed()
    {
        using var s = await CreateAsync(factory);
        var receipt = await RegisterAsync(s, "6", "4"); var id = Id(receipt);
        var (user, session) = await factory.SeedUserAsync(Roles.MillOperator, s.Accounts["operator"].User.OrganizationId);
        using var unassigned = factory.CreateRealCookieClient(user, session); await CsrfAsync(unassigned);
        using var hidden = await unassigned.GetAsync($"/api/ledger/receiving/{id}"); Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        using var queueHidden = await s.Operator.GetAsync($"/api/ledger/receiving-tasks?facilityId={s.ForeignFacilityId}"); Assert.Equal(HttpStatusCode.Forbidden, queueHidden.StatusCode);
        await RejectedAsync(unassigned, HttpMethod.Post, Measurements(id), MeasurementBody(receipt), HttpStatusCode.Forbidden);
        await RejectedAsync(s.Manager, HttpMethod.Post, Measurements(id), MeasurementBody(receipt), HttpStatusCode.Forbidden);
        await RejectedAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, version: 1), HttpStatusCode.Conflict);
        await RejectedAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, actual: ["0", "4"]), HttpStatusCode.BadRequest);
        var partial = JsonSerializer.SerializeToNode(MeasurementBody(receipt))!.AsObject(); partial["items"]!.AsArray().RemoveAt(1);
        await RejectedAsync(s.Operator, HttpMethod.Post, Measurements(id), partial, HttpStatusCode.BadRequest);
        var foreignLine = JsonSerializer.SerializeToNode(MeasurementBody(receipt))!.AsObject(); foreignLine["items"]![0]!["procurementItemId"] = Guid.NewGuid();
        await RejectedAsync(s.Operator, HttpMethod.Post, Measurements(id), foreignLine, HttpStatusCode.BadRequest);
        Assert.Equal(0L, await CountAsync("ledger.arrival_measurement_sets", "procurement_id", id));
        Assert.Equal(0L, await CountAsync("ledger.lots", "receipt_id", id));
        var mismatch = await SendAsync(s.Operator, HttpMethod.Post, Measurements(id), MeasurementBody(receipt, actual: ["5", "4"]));
        var badReason = JsonSerializer.SerializeToNode(Review(mismatch, "KEEP_BLOCKED", s.EvidenceId))!.AsObject(); badReason["reason"] = " ";
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), badReason, HttpStatusCode.BadRequest);
        var foreignEvidence = Guid.NewGuid();
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/evidence", new { id = foreignEvidence, ownerId = s.ForeignOwnerId, facilityId = s.FacilityId, fileName = "foreign.txt", contentType = "text/plain", contentBase64 = "eA==" });
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(mismatch, "KEEP_BLOCKED", foreignEvidence), HttpStatusCode.Forbidden);
        var otherFacilityEvidence = Guid.NewGuid();
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/evidence", new { id = otherFacilityEvidence, ownerId = s.OwnerId, facilityId = s.ForeignFacilityId, fileName = "foreign.txt", contentType = "text/plain", contentBase64 = "eA==" });
        await RejectedAsync(s.Manager, HttpMethod.Post, Reviews(id), Review(mismatch, "KEEP_BLOCKED", otherFacilityEvidence), HttpStatusCode.Forbidden);
        Assert.Equal(0L, await CountAsync("ledger.arrival_reviews", "procurement_id", id));
    }

    [Fact]
    public async Task Concurrent_arrival_and_review_replays_credit_at_most_once_and_stale_competitor_loses()
    {
        using var s = await CreateAsync(factory);
        var receipt = await RegisterAsync(s, "10"); var id = Id(receipt);
        var a = MeasurementBody(receipt); var b = MeasurementBody(receipt);
        var results = await Task.WhenAll(RawAsync(s.Operator, HttpMethod.Post, Measurements(id), a), RawAsync(s.Operator, HttpMethod.Post, Measurements(id), b));
        try { Assert.Single(results, x => x.IsSuccessStatusCode); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in results) result.Dispose(); }
        Assert.Equal(1L, await CountAsync("ledger.lots", "receipt_id", id));
        Assert.Equal(1L, await CountAsync("ledger.arrival_operations", "procurement_id", id));
        var second = await RegisterAsync(s, "20"); var secondId = Id(second);
        var mismatched = await SendAsync(s.Operator, HttpMethod.Post, Measurements(secondId), MeasurementBody(second, actual: ["19"]));
        var review = Review(mismatched, "APPROVE_SHORTAGE", s.EvidenceId);
        var repeated = await Task.WhenAll(RawAsync(s.Manager, HttpMethod.Post, Reviews(secondId), review), RawAsync(s.Manager, HttpMethod.Post, Reviews(secondId), review));
        try { foreach (var response in repeated) Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); }
        finally { foreach (var response in repeated) response.Dispose(); }
        Assert.Equal(1L, await CountAsync("ledger.lots", "receipt_id", secondId));
        Assert.Equal(1L, await CountAsync("ledger.arrival_reviews", "procurement_id", secondId));
        Assert.Equal(19m, Quantity(await s.LotAsync(Lots(second)[0])));
    }

    [Fact]
    public async Task Runtime_cannot_mutate_receiving_projections_or_observations_or_call_internal_credit()
    {
        using var s = await CreateAsync(factory); var receipt = await s.ReceiptAsync("10"); var id = Id(receipt);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var sql in new[] { "UPDATE ledger.receiving SET status='PENDING_ARRIVAL' WHERE procurement_id=@id", "DELETE FROM ledger.arrival_measurements", "UPDATE ledger.arrival_reviews SET reason='changed'", "SELECT ledger.accept_arrival(@id)", "SELECT ledger.legacy_procurement_package_complete(@id)" })
        {
            await using var command = new NpgsqlCommand(sql, runtime); command.Parameters.AddWithValue("id", id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task Accepted_source_stock_respects_hold_and_missing_positions_remain_service_unavailable()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10"); var lot = Lots(receipt)[0];
        var sourceId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        await s.HoldAsync(lot);
        var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sourceId}");
        Assert.Equal("10", source!.CurrentStock.Single().OnHandKg);
        Assert.Equal("0", source.CurrentStock.Single().AvailableKg);
        Assert.Equal("0", source.UsableStockKg);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var corrupt = new NpgsqlCommand("DELETE FROM ledger.positions WHERE lot_id=@id", owner);
        corrupt.Parameters.AddWithValue("id", lot); await corrupt.ExecuteNonQueryAsync();
        try
        {
            using var unavailable = await s.Purchaser.GetAsync($"/api/source-lots/{sourceId}");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        }
        finally
        {
            await using var restore = new NpgsqlCommand("INSERT INTO ledger.positions SELECT lot_id,location_id,sum(quantity_kg) FROM ledger.postings WHERE lot_id=@id GROUP BY lot_id,location_id", owner);
            restore.Parameters.AddWithValue("id", lot); await restore.ExecuteNonQueryAsync();
        }
    }

    private Task<long> CountAsync(string table, string column, Guid id) => ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE {column}=@id", id);
    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner); command.Parameters.AddWithValue("id", id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
