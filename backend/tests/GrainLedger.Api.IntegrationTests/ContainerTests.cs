using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ContainerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> Export(Guid owner)
    {
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, owner);
        var client = factory.CreateRealCookieClient(user, session); await CsrfAsync(client); return client;
    }
    private static object Create(Guid id, Guid facility, string target = "10") => new { id, requestId = Guid.NewGuid(), code = $"BOX-{id:N}", facilityId = facility, targetKg = target };
    private static Task<JsonElement> Post(HttpClient c, string path, object body) => SendAsync(c, HttpMethod.Post, "/api/ledger/containers" + path, body);
    private static object Draft(JsonElement c, Guid lot, Guid loc, Guid? id = null, params string[] amounts) => new
    {
        requestId = Guid.NewGuid(), version = c.GetProperty("version").GetGuid(), draftId = id ?? Guid.NewGuid(), loadedAt = DateTimeOffset.UtcNow,
        lines = amounts.Select(q => new { lotId = lot, locationId = loc, quantityKg = q }).ToArray()
    };

    [Fact]
    public async Task Draft_metadata_retry_and_dependency_gates_preserve_stock_and_history()
    {
        using var s = await CreateAsync(factory); using var export = await Export(s.OwnerId);
        var lot = Lots(await s.ReceiptAsync("10"))[0]; var id = Guid.NewGuid(); var request = Create(id, s.FacilityId);
        var created = await Post(export, "", request); Assert.Equal(created.GetRawText(), (await Post(export, "", request)).GetRawText());
        var metadata = await Post(export, $"/{id}/metadata", new { requestId = Guid.NewGuid(), version = created.GetProperty("version"), orderId = Guid.NewGuid(), containerNumber = "BOX-01", sealNumber = "SEAL-01" });
        var draftId = Guid.NewGuid(); var draft = Draft(metadata, lot, s.LocationId, draftId, "6.000001", "3.999999");
        var saved = await Post(export, $"/{id}/loading-draft", draft);
        Assert.Equal("10.000000", saved.GetProperty("drafts")[0].GetProperty("totalKg").GetString());
        Assert.Equal("0.000000", saved.GetProperty("committedLoadedKg").GetString());
        Assert.Equal(saved.GetRawText(), (await Post(export, $"/{id}/loading-draft", draft)).GetRawText());
        var check = await Post(export, $"/{id}/loading-check", new { draftId });
        Assert.False(check.GetProperty("ready").GetBoolean());
        Assert.Contains(check.GetProperty("findings").EnumerateArray(), x => x.GetProperty("code").GetString() == "OUTPUT_RESERVATION_UNAVAILABLE");
        foreach (var action in new[] { "commit-loading", "close" })
        {
            using var blocked = await export.PostAsJsonAsync($"/api/ledger/containers/{id}/{action}", action == "close"
                ? (object)new { requestId = Guid.NewGuid(), version = saved.GetProperty("version") }
                : new { requestId = Guid.NewGuid(), version = saved.GetProperty("version"), draftId });
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.False((await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ready").GetBoolean());
        }
        Assert.Equal(10m, Quantity(await s.LotAsync(lot)));
        var detail = await export.GetFromJsonAsync<JsonElement>($"/api/ledger/containers/{id}");
        Assert.Equal("OPEN", detail.GetProperty("status").GetString());
        var history = await export.GetFromJsonAsync<JsonElement>($"/api/ledger/containers/{id}/history");
        Assert.Equal(3, history.GetProperty("totalCount").GetInt32()); // create, metadata, draft; retries/refusals do not mutate.
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        await using var denied = new NpgsqlCommand("UPDATE ledger.containers SET status='CLOSED'", runtime);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task Persisted_scope_and_roles_guard_lists_details_writes_and_spoofed_authority()
    {
        using var s = await CreateAsync(factory); using var export = await Export(s.OwnerId); using var other = await Export(s.ForeignOwnerId);
        var id = Guid.NewGuid(); var c = await Post(export, "", Create(id, s.FacilityId));
        Assert.Equal(HttpStatusCode.OK, (await s.Customer.GetAsync($"/api/ledger/containers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Operator.GetAsync($"/api/ledger/containers/{id}")).StatusCode);
        foreach (var client in new[] { other, s.ForeignCustomer, s.Purchaser, s.Admin })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ledger/containers/{id}")).StatusCode);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/ledger/containers")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Customer.PostAsJsonAsync("/api/ledger/containers", Create(Guid.NewGuid(), s.FacilityId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await export.PostAsJsonAsync($"/api/ledger/containers/{id}/close", new { ready = true, approved = true, version = c.GetProperty("version"), requestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Draft_validation_duplicate_positions_stale_writes_and_new_stock_are_checked_fresh()
    {
        using var s = await CreateAsync(factory); using var export = await Export(s.OwnerId);
        var lot = Lots(await s.ReceiptAsync("10"))[0]; var id = Guid.NewGuid(); var c = await Post(export, "", Create(id, s.FacilityId, "20"));
        var draftId = Guid.NewGuid(); var saved = await Post(export, $"/{id}/loading-draft", Draft(c, lot, s.LocationId, draftId, "6", "6"));
        var check = await Post(export, $"/{id}/loading-check", new { draftId });
        Assert.Contains(check.GetProperty("findings").EnumerateArray(), x => x.GetProperty("code").GetString() == "INSUFFICIENT_PHYSICAL_STOCK");
        Assert.Equal(HttpStatusCode.Conflict, (await export.PostAsJsonAsync($"/api/ledger/containers/{id}/loading-draft", Draft(c, lot, s.LocationId, draftId, "1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await export.PostAsJsonAsync($"/api/ledger/containers/{id}/loading-draft", Draft(saved, lot, s.ForeignLocationId, null, "1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await export.PostAsJsonAsync($"/api/ledger/containers/{id}/loading-draft", Draft(saved, lot, s.LocationId, null, "21"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await export.PostAsJsonAsync($"/api/ledger/containers/{id}/loading-draft", Draft(saved, lot, s.LocationId, null, "0.0000001"))).StatusCode);
        var tiny = await Post(export, $"/{id}/loading-draft", Draft(saved, lot, s.LocationId, draftId, "0.000001"));
        Assert.Equal("0.000001", tiny.GetProperty("drafts")[0].GetProperty("totalKg").GetString());
        var list = await export.GetFromJsonAsync<JsonElement>("/api/ledger/containers?page=1&pageSize=1&search=BOX");
        Assert.Equal(1, list.GetProperty("pageSize").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await export.GetAsync("/api/ledger/containers?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await export.PostAsJsonAsync("/api/ledger/containers", new { id = Guid.NewGuid(), requestId = Guid.NewGuid(), code = "NUMERIC-KG", facilityId = s.FacilityId, targetKg = 2 })).StatusCode);
    }
    [Fact]
    public async Task Concurrent_metadata_has_one_winner_and_hold_readiness_is_fresh()
    {
        using var s = await CreateAsync(factory); using var export = await Export(s.OwnerId);
        var lot = Lots(await s.ReceiptAsync("2"))[0]; var id = Guid.NewGuid(); var c = await Post(export, "", Create(id, s.FacilityId, "2"));
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => export.PostAsJsonAsync($"/api/ledger/containers/{id}/metadata",
            new { requestId = Guid.NewGuid(), version = c.GetProperty("version"), containerNumber = $"BOX-{i}", sealNumber = "S" })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        c = await export.GetFromJsonAsync<JsonElement>($"/api/ledger/containers/{id}");
        var draftId = Guid.NewGuid(); await Post(export, $"/{id}/loading-draft", Draft(c, lot, s.LocationId, draftId, "2"));
        await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new { id = Guid.NewGuid(), lotId = lot, reason = "Synthetic container readiness check" });
        var check = await Post(export, $"/{id}/loading-check", new { draftId });
        Assert.Contains(check.GetProperty("findings").EnumerateArray(), x => x.GetProperty("code").GetString() == "LOT_RESTRICTED");
        Assert.Equal(2m, Quantity(await s.LotAsync(lot)));
    }

}
