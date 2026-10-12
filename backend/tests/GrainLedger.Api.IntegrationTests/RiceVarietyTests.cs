using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class RiceVarietyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_and_updates_variety_customer_reads_and_audit_is_recorded()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        var csrf = await adminClient.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        adminClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        var created = await adminClient.PostAsJsonAsync("/api/rice-varieties", new { code = " st25 ", name = " ST25 " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var variety = (await created.Content.ReadFromJsonAsync<RiceVarietyResponse>())!;
        Assert.Equal("ST25", variety.Code);
        Assert.Equal("ST25", variety.Name);
        Assert.Equal($"/api/rice-varieties/{variety.Id}", created.Headers.Location?.OriginalString);
        var etag = created.Headers.ETag!.ToString();

        var detail = await purchaserClient.GetAsync($"/api/rice-varieties/{variety.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(etag, detail.Headers.ETag?.ToString());
        var list = await purchaserClient.GetFromJsonAsync<RiceVarietyListResponse>("/api/rice-varieties?q=ST25");
        Assert.Contains(list!.Items, x => x.Id == variety.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await purchaserClient.PostAsJsonAsync("/api/rice-varieties", new { code = "JP", name = "Japan" })).StatusCode);

        using var missing = new HttpRequestMessage(HttpMethod.Patch, $"/api/rice-varieties/{variety.Id}")
            { Content = JsonContent.Create(new { name = "ST25 updated" }) };
        Assert.Equal((HttpStatusCode)428, (await adminClient.SendAsync(missing)).StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/rice-varieties/{variety.Id}")
            { Content = JsonContent.Create(new { name = "ST25 updated" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await adminClient.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, updated.Headers.ETag?.ToString());
        Assert.Equal("ST25 updated", (await updated.Content.ReadFromJsonAsync<RiceVarietyResponse>())!.Name);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/rice-varieties/{variety.Id}")
            { Content = JsonContent.Create(new { name = "Outdated" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == variety.Id));
    }

    [Fact]
    public async Task Invalid_and_duplicate_variety_codes_are_rejected()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var code = $"M{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/rice-varieties", new { code = " ", name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/rice-varieties", new { code, name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/rice-varieties", new { code = code.ToLowerInvariant(), name = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/rice-varieties?page=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/rice-varieties?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/rice-varieties/{Guid.NewGuid()}")).StatusCode);
    }
}
