using System.Net;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LocationStockTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Physical_totals_include_holds_and_paging_without_leaking_other_companies_or_facilities()
    {
        using var s = await CreateAsync(factory);
        var lots = Lots(await s.ReceiptAsync("2.000001", "10.000002", "9.000003"));
        var foreign = Lots(await s.ForeignReceiptAsync("100.000004"))[0];
        await s.ForeignReceiptAsync("500", s.ForeignLocationId);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new { id = Guid.NewGuid(), lotId = lots[0], reason = "Synthetic stock hold" });
        var route = $"/api/ledger/locations/{s.LocationId}/stock?pageSize=1";
        var first = await GetAsync(s.Customer, route);
        Assert.Equal("21.000006", first.GetProperty("totalKg").GetString());
        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        var pages = new[] { first, await GetAsync(s.Customer, route + "&page=2"), await GetAsync(s.Customer, route + "&page=3") };
        Assert.All(pages, p => Assert.Equal("21.000006", p.GetProperty("totalKg").GetString()));
        var items = pages.SelectMany(p => p.GetProperty("items").EnumerateArray()).ToArray();
        Assert.Equal(lots.Order(), items.Select(x => x.GetProperty("lotId").GetGuid()).Order());
        var held = items.Single(x => x.GetProperty("lotId").GetGuid() == lots[0]);
        Assert.Equal("2.000001", held.GetProperty("quantityKg").GetString());
        Assert.Equal("0.000000", held.GetProperty("availableKg").GetString());
        Assert.Contains(held.GetProperty("restrictions").EnumerateArray(), x => x.GetString() == "HOLD");
        Assert.False(pages[2].GetProperty("hasMore").GetBoolean());
        var stale = await GetAsync(s.Customer, route + "&page=99");
        Assert.Empty(stale.GetProperty("items").EnumerateArray());
        Assert.Equal("21.000006", stale.GetProperty("totalKg").GetString());
        var other = await GetAsync(s.ForeignCustomer, route);
        Assert.Equal("100.000004", other.GetProperty("totalKg").GetString());
        Assert.Equal(foreign, Assert.Single(other.GetProperty("items").EnumerateArray()).GetProperty("lotId").GetGuid());
        var mill = await GetAsync(s.Operator, route);
        Assert.Equal("121.000010", mill.GetProperty("totalKg").GetString());
        Assert.Equal(4, mill.GetProperty("totalCount").GetInt32());
        using var denied = await s.Operator.GetAsync($"/api/ledger/locations/{s.ForeignLocationId}/stock");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var hidden = await GetAsync(s.Customer, $"/api/ledger/locations/{s.ForeignLocationId}/stock");
        Assert.Equal("0.000000", hidden.GetProperty("totalKg").GetString());
        Assert.Empty(hidden.GetProperty("items").EnumerateArray());
        using var admin = await s.Admin.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task Reservation_does_not_reduce_physical_stock_and_mixed_pool_is_current()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, mixedStock: true);
        var page = await GetAsync(f.Stock.Customer, $"/api/ledger/locations/{f.LoadingLocationId}/stock");
        Assert.Equal("10.000000", page.GetProperty("totalKg").GetString());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(f.LotId, item.GetProperty("lotId").GetGuid());
        Assert.Equal("10.000000", item.GetProperty("quantityKg").GetString());
        Assert.Equal("0.000000", item.GetProperty("availableKg").GetString());
    }

    [Fact]
    public async Task Revoked_assignment_immediately_denies_location()
    {
        using var s = await CreateAsync(factory);
        var facility = Guid.NewGuid();
        var location = Guid.NewGuid();
        var operatorId = s.Accounts["operator"].User.Id;
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/facilities", new { id = facility, name = "Temporary facility" });
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = location, facilityId = facility, name = "Temporary location", kind = "STORE" });
        var assignment = await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = operatorId, facilityId = facility });
        await GetAsync(s.Operator, $"/api/ledger/locations/{location}/stock");
        await SendAsync(s.Admin, HttpMethod.Delete, $"/api/ledger/assignments/{operatorId}/{facility}", new { version = assignment.GetProperty("version").GetString() });
        using var denied = await s.Operator.GetAsync($"/api/ledger/locations/{location}/stock");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=oops")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("pageSize=101")]
    [InlineData("page=1&page=2")]
    public async Task Invalid_paging_is_rejected(string query)
    {
        using var s = await CreateAsync(factory);
        using var response = await s.Customer.GetAsync($"/api/ledger/locations/{s.LocationId}/stock?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
