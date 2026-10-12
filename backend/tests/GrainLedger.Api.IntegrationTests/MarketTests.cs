using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MarketTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_and_updates_market_customer_reads_and_audit_is_recorded()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        var csrf = await adminClient.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        adminClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        var created = await adminClient.PostAsJsonAsync("/api/markets", new { code = " eu ", name = " European Union " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var market = (await created.Content.ReadFromJsonAsync<MarketResponse>())!;
        Assert.Equal("EU", market.Code);
        Assert.Equal("European Union", market.Name);
        Assert.Equal($"/api/markets/{market.Id}", created.Headers.Location?.OriginalString);
        var etag = created.Headers.ETag!.ToString();

        var detail = await purchaserClient.GetAsync($"/api/markets/{market.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(etag, detail.Headers.ETag?.ToString());
        var list = await purchaserClient.GetFromJsonAsync<MarketListResponse>("/api/markets?q=EU");
        Assert.Contains(list!.Items, x => x.Id == market.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await purchaserClient.PostAsJsonAsync("/api/markets", new { code = "JP", name = "Japan" })).StatusCode);

        using var missing = new HttpRequestMessage(HttpMethod.Patch, $"/api/markets/{market.Id}")
            { Content = JsonContent.Create(new { name = "European Union updated" }) };
        Assert.Equal((HttpStatusCode)428, (await adminClient.SendAsync(missing)).StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/markets/{market.Id}")
            { Content = JsonContent.Create(new { name = "European Union updated" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await adminClient.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, updated.Headers.ETag?.ToString());
        Assert.Equal("European Union updated", (await updated.Content.ReadFromJsonAsync<MarketResponse>())!.Name);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/markets/{market.Id}")
            { Content = JsonContent.Create(new { name = "Outdated" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == market.Id));
    }

    [Fact]
    public async Task Invalid_and_duplicate_market_codes_are_rejected()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var code = $"M{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/markets", new { code = " ", name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/markets", new { code, name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/markets", new { code = code.ToLowerInvariant(), name = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/markets?page=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/markets?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/markets/{Guid.NewGuid()}")).StatusCode);
    }
}
