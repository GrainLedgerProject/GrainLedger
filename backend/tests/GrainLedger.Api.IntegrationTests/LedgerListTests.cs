using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Stock_filters_count_before_paging_and_sort_exact_quantities_without_cross_scope_ids()
    {
        using var s = await CreateAsync(factory);
        var ids = Lots(await s.ReceiptAsync("2", "10", "9.000001"));
        var foreign = Lots(await s.ForeignReceiptAsync("100", s.ForeignLocationId))[0];
        var query = $"/api/ledger/lots?ownerId={s.OwnerId}&facilityId={s.FacilityId}&locationId={s.LocationId}&stage=RAW_PADDY&sortBy=quantity&sortDirection=asc&pageSize=2";
        var first = await GetAsync(s.Operator, query);
        AssertPage(first, 1, 2, 3, true);
        Assert.Equal(new[] { ids[0], ids[2] }, Ids(first));
        var second = await GetAsync(s.Operator, query + "&page=2");
        AssertPage(second, 2, 2, 3, false);
        Assert.Equal(new[] { ids[1] }, Ids(second));
        var stale = await GetAsync(s.Operator, query + "&page=99");
        AssertPage(stale, 99, 2, 3, false);
        Assert.Empty(Ids(stale));
        var pinned = await GetAsync(s.Operator, $"/api/ledger/lots?ids={ids[0]},{foreign}&pageSize=100");
        AssertPage(pinned, 1, 100, 1, false);
        Assert.Equal(new[] { ids[0] }, Ids(pinned));
        var hidden = await GetAsync(s.ForeignCustomer, $"/api/ledger/lots?ids={ids[0]}");
        AssertPage(hidden, 1, 25, 0, false);
        var code = (await s.LotAsync(ids[1])).GetProperty("code").GetString();
        var searched = await GetAsync(s.Operator, query + "&search=" + code);
        Assert.Equal(new[] { ids[1] }, Ids(searched));
        Assert.Equal(1, searched.GetProperty("totalCount").GetInt32());
        var wrongLocation = await GetAsync(s.Operator, query.Replace(s.LocationId.ToString(), s.ForeignLocationId.ToString()));
        Assert.Empty(Ids(wrongLocation));
        Assert.Equal(0, wrongLocation.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Draft_and_committed_pages_have_stable_ties_creator_flags_and_stale_page_counts()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var drafts = new List<JsonElement>();
        var occurredAt = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            var body = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(source, "1")], [("DRIED_PADDY", "1")]))!.AsObject();
            body["occurredAt"] = occurredAt;
            drafts.Add(await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body));
        }
        var query = $"/api/ledger/operations?ownerId={s.OwnerId}&facilityId={s.FacilityId}&kind=DRYING&status=DRAFT&sortBy=occurredAt&sortDirection=asc&pageSize=2";
        var first = await GetAsync(s.Operator, query);
        var second = await GetAsync(s.Operator, query + "&page=2");
        AssertPage(first, 1, 2, 3, true);
        AssertPage(second, 2, 2, 3, false);
        Assert.Equal(drafts.Select(Id).OrderBy(IdKey), Ids(first).Concat(Ids(second)));
        var outputCode = drafts[2].GetProperty("outputs")[0].GetProperty("code").GetString();
        var searched = await GetAsync(s.Operator, query + "&search=" + outputCode);
        AssertPage(searched, 1, 2, 1, false);
        Assert.Equal(new[] { Id(drafts[2]) }, Ids(searched));
        var hiddenSearch = await GetAsync(s.ForeignCustomer, "/api/ledger/operations?search=" + outputCode);
        Assert.Equal(0, hiddenSearch.GetProperty("totalCount").GetInt32());
        Assert.All(first.GetProperty("items").EnumerateArray(), row =>
        {
            Assert.Equal(s.Accounts["operator"].User.Id, row.GetProperty("createdBy").GetGuid());
            Assert.True(row.GetProperty("canEdit").GetBoolean());
            Assert.True(row.GetProperty("canDelete").GetBoolean());
        });
        var manager = await GetAsync(s.Manager, query);
        Assert.All(manager.GetProperty("items").EnumerateArray(), row => Assert.False(row.GetProperty("canEdit").GetBoolean()));
        var committed = await s.CommitAsync(drafts[0]);
        var committedPage = await GetAsync(s.Operator, query.Replace("status=DRAFT", "status=COMMITTED"));
        Assert.Equal(new[] { Id(committed) }, Ids(committedPage));
        Assert.False(committedPage.GetProperty("items")[0].GetProperty("canDelete").GetBoolean());
        await SendAsync(s.Operator, HttpMethod.Delete, $"/api/ledger/operations/{Id(drafts[1])}", new { version = drafts[1].GetProperty("version").GetString() });
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var retained = new NpgsqlCommand("SELECT is_deleted AND content->'inputs'=$2::jsonb AND content->'outputs'=$3::jsonb AND content->>'declaredLossKg'=$4 AND version<>$5 FROM ledger.operations WHERE id=$1", owner);
            retained.Parameters.AddWithValue(Id(drafts[1]));
            retained.Parameters.AddWithValue(drafts[1].GetProperty("inputs").GetRawText());
            retained.Parameters.AddWithValue(drafts[1].GetProperty("outputs").GetRawText());
            retained.Parameters.AddWithValue(drafts[1].GetProperty("declaredLossKg").GetString()!);
            retained.Parameters.AddWithValue(drafts[1].GetProperty("version").GetGuid());
            Assert.True((bool)(await retained.ExecuteScalarAsync())!);
        }
        var deletedPinned = await GetAsync(s.Operator, $"/api/ledger/operations?ids={Id(drafts[1])}");
        Assert.Equal(0, deletedPinned.GetProperty("totalCount").GetInt32());
        using (var deletedDetail = await s.Operator.GetAsync($"/api/ledger/operations/{Id(drafts[1])}"))
            Assert.Equal(HttpStatusCode.Forbidden, deletedDetail.StatusCode);
        var stale = await GetAsync(s.Operator, query + "&page=2");
        AssertPage(stale, 2, 2, 1, false);
        Assert.Empty(Ids(stale));
        var pinned = await GetAsync(s.ForeignCustomer, $"/api/ledger/operations?ids={Id(committed)}");
        Assert.Equal(0, pinned.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Holds_corrections_notifications_history_and_receiving_have_filtered_scoped_totals()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "5")], [("DRIED_PADDY", "5")]));
        var replacement = await s.DraftAsync("DRYING", [(source, "5")], [("DRIED_PADDY", "5")]);
        var correction = await s.CorrectionAsync(original, replacement);
        var corrections = await GetAsync(s.Operator, $"/api/ledger/corrections?ownerId={s.OwnerId}&status=RECONCILED&kind=DRYING&pageSize=1&sortBy=requestedAt");
        AssertPage(corrections, 1, 1, 1, false);
        Assert.Equal(correction, Id(corrections.GetProperty("items")[0]));
        Assert.Equal(JsonValueKind.Null, corrections.GetProperty("items")[0].GetProperty("evidenceId").ValueKind);
        var foreignCorrections = await GetAsync(s.ForeignCustomer, $"/api/ledger/corrections?ownerId={s.OwnerId}");
        Assert.Equal(0, foreignCorrections.GetProperty("totalCount").GetInt32());
        var originalCode = original.GetProperty("outputs")[0].GetProperty("code").GetString();
        var searchedCorrection = await GetAsync(s.Operator, $"/api/ledger/corrections?ownerId={s.OwnerId}&search={originalCode}&pageSize=1");
        AssertPage(searchedCorrection, 1, 1, 1, false);
        Assert.Equal(correction, Id(searchedCorrection.GetProperty("items")[0]));
        var hiddenCorrectionSearch = await GetAsync(s.ForeignCustomer, "/api/ledger/corrections?search=" + originalCode);
        Assert.Equal(0, hiddenCorrectionSearch.GetProperty("totalCount").GetInt32());
        var hold = await s.HoldAsync(source);
        var holds = await GetAsync(s.Operator, $"/api/ledger/holds?lotId={source}&status=ACTIVE&sortBy=owner&pageSize=1");
        AssertPage(holds, 1, 1, 1, false);
        Assert.Equal(hold, Id(holds.GetProperty("items")[0]));
        var notifications = await GetAsync(s.Operator, $"/api/ledger/notifications?lotId={source}&status=UNREAD&pageSize=1&sortBy=readAt");
        AssertPage(notifications, 1, 1, 1, false);
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/notifications/{Id(notifications.GetProperty("items")[0])}/read", new { });
        var unread = await GetAsync(s.Operator, $"/api/ledger/notifications?lotId={source}&status=UNREAD");
        Assert.Equal(0, unread.GetProperty("totalCount").GetInt32());
        var history = await GetAsync(s.Operator, $"/api/ledger/history?ownerId={s.OwnerId}&lotId={source}&kind=RECEIPT&sortBy=quantity&pageSize=1");
        AssertPage(history, 1, 1, 1, false);
        Assert.Equal("10.000000", history.GetProperty("items")[0].GetProperty("quantityKg").GetString());
        var pending = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["3"]));
        var receiving = await GetAsync(s.Operator, $"/api/ledger/receiving-tasks?facilityId={s.FacilityId}&ownerId={s.OwnerId}&status=PENDING_ARRIVAL&sortBy=owner&pageSize=1");
        AssertPage(receiving, 1, 1, 1, false);
        Assert.Equal(Id(pending), Id(receiving.GetProperty("items")[0]));
        Assert.False(receiving.GetProperty("items")[0].TryGetProperty("farmerName", out _));
        using var denied = await s.Operator.GetAsync($"/api/ledger/receiving-tasks?facilityId={s.ForeignFacilityId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Soft_deleted_setup_rows_stay_stored_but_leave_lookups_and_assignments_revoke_scope()
    {
        using var s = await CreateAsync(factory);
        var facilityId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var operatorId = s.Accounts["operator"].User.Id;
        var facility = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/facilities", new { id = facilityId, name = "Removable facility" });
        var location = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = locationId, facilityId, name = "Removable location", kind = "STORE" });
        var equipment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/equipment", new { id = equipmentId, facilityId, name = "Removable equipment" });
        var assignment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorId, facilityId });
        var before = await GetAsync(s.Operator, "/api/ledger/lookups");
        Assert.Contains(before.GetProperty("facilities").EnumerateArray(), row => Id(row) == facilityId);
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/assignments/{operatorId}/{facilityId}", new { version = assignment.GetProperty("version").GetString() });
        var revoked = await GetAsync(s.Operator, "/api/ledger/lookups");
        Assert.DoesNotContain(revoked.GetProperty("facilities").EnumerateArray(), row => Id(row) == facilityId);
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/equipment/{equipmentId}", new { version = equipment.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/locations/{locationId}", new { version = location.GetProperty("version").GetString() });
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/facilities/{facilityId}", new { version = facility.GetProperty("version").GetString() });
        var setup = await GetAsync(s.Admin, "/api/ledger/setup");
        Assert.DoesNotContain(setup.GetProperty("facilities").EnumerateArray(), row => Id(row) == facilityId);
        Assert.DoesNotContain(setup.GetProperty("locations").EnumerateArray(), row => Id(row) == locationId);
        Assert.DoesNotContain(setup.GetProperty("equipment").EnumerateArray(), row => Id(row) == equipmentId);
        Assert.DoesNotContain(setup.GetProperty("assignments").EnumerateArray(), row => row.GetProperty("userId").GetGuid() == operatorId && row.GetProperty("facilityId").GetGuid() == facilityId);
        var lookups = await GetAsync(s.Purchaser, "/api/ledger/lookups");
        Assert.DoesNotContain(lookups.GetProperty("facilities").EnumerateArray(), row => Id(row) == facilityId);
        Assert.DoesNotContain(lookups.GetProperty("locations").EnumerateArray(), row => Id(row) == locationId);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using var retained = new NpgsqlCommand("SELECT (SELECT is_deleted FROM ledger.facilities WHERE id=$1) AND (SELECT is_deleted FROM ledger.locations WHERE id=$2) AND (SELECT is_deleted FROM ledger.equipment WHERE id=$3) AND (SELECT is_deleted FROM ledger.assignments WHERE user_id=$4 AND facility_id=$1) AND NOT ledger.assigned($4,$1)", owner);
        retained.Parameters.AddWithValue(facilityId);
        retained.Parameters.AddWithValue(locationId);
        retained.Parameters.AddWithValue(equipmentId);
        retained.Parameters.AddWithValue(operatorId);
        Assert.True((bool)(await retained.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Legacy_creator_is_read_only_and_private_list_helpers_are_not_runtime_grants()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("1"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "1")], [("DRIED_PADDY", "1")]);
        var legacyFacility = Guid.NewGuid();
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var operation = new NpgsqlCommand("UPDATE ledger.operations SET created_by=NULL WHERE id=$1", owner);
            operation.Parameters.AddWithValue(Id(draft));
            await operation.ExecuteNonQueryAsync();
            await using var facility = new NpgsqlCommand("INSERT INTO ledger.facilities(id,name) VALUES($1,'Legacy facility')", owner);
            facility.Parameters.AddWithValue(legacyFacility);
            await facility.ExecuteNonQueryAsync();
        }
        var detail = await GetAsync(s.Operator, $"/api/ledger/operations/{Id(draft)}");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("createdBy").ValueKind);
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        Assert.False(detail.GetProperty("canDelete").GetBoolean());
        var setup = await GetAsync(s.Admin, "/api/ledger/setup");
        var legacy = setup.GetProperty("facilities").EnumerateArray().Single(x => Id(x) == legacyFacility);
        Assert.Equal(JsonValueKind.Null, legacy.GetProperty("createdBy").ValueKind);
        Assert.False(legacy.GetProperty("canEdit").GetBoolean());
        Assert.False(legacy.GetProperty("canDelete").GetBoolean());
        var lookups = await GetAsync(s.Operator, "/api/ledger/lookups");
        Assert.All(lookups.GetProperty("facilities").EnumerateArray(), row => Assert.False(row.TryGetProperty("createdBy", out _)));
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var privileges = new NpgsqlCommand("SELECT has_function_privilege(current_user,'ledger.receiving_tasks_page(uuid,jsonb)','EXECUTE') OR has_function_privilege(current_user,'ledger.operation_for_actor(uuid,uuid)','EXECUTE')", runtime);
        Assert.False((bool)(await privileges.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("lots?page=0")]
    [InlineData("lots?page=2147483647&pageSize=100")]
    [InlineData("lots?pageSize=101")]
    [InlineData("lots?pageSize=-1")]
    [InlineData("lots?page=oops")]
    [InlineData("lots?sortBy=quantity;DROP")]
    [InlineData("lots?sortDirection=backwards")]
    [InlineData("lots?stage=UNKNOWN")]
    [InlineData("lots?ids=not-a-guid")]
    [InlineData("operations?status=REVERSED")]
    [InlineData("operations?queueOnly=invalid")]
    [InlineData("holds?sortBy=quantity")]
    [InlineData("notifications?status=ACTIVE")]
    [InlineData("corrections?kind=UNKNOWN")]
    [InlineData("history?pageSize=0")]
    [InlineData("receiving-tasks?page=0")]
    public async Task Invalid_queries_return_400(string query)
    {
        using var s = await CreateAsync(factory);
        using var response = await s.Operator.GetAsync("/api/ledger/" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pinned_ids_reject_more_than_100_values()
    {
        using var s = await CreateAsync(factory);
        using var response = await s.Operator.GetAsync("/api/ledger/lots?ids=" + string.Join(',', Enumerable.Range(0, 101).Select(_ => Guid.NewGuid())));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();
    private static string IdKey(Guid id) => id.ToString("N");
    private static Guid[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(Id).ToArray();
    private static void AssertPage(JsonElement page, int number, int size, int total, bool hasMore)
    {
        Assert.Equal(number, page.GetProperty("page").GetInt32());
        Assert.Equal(size, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(total, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(hasMore, page.GetProperty("hasMore").GetBoolean());
        Assert.InRange(page.GetProperty("items").GetArrayLength(), 0, size);
    }
}
