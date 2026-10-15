using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CommodityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_and_updates_commodity_customer_reads_and_audit_is_recorded()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (otherAdmin, otherAdminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var otherAdminClient = factory.CreateRealCookieClient(otherAdmin, otherAdminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        var csrf = await adminClient.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        adminClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        var created = await adminClient.PostAsJsonAsync("/api/commodities", new { code = " rice ", name = " Milled rice " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var commodity = (await created.Content.ReadFromJsonAsync<CommodityResponse>())!;
        Assert.Equal("RICE", commodity.Code);
        Assert.Equal(admin.Id, commodity.CreatedBy);
        Assert.Equal("Milled rice", commodity.Name);
        Assert.Equal($"/api/commodities/{commodity.Id}", created.Headers.Location?.OriginalString);
        var etag = created.Headers.ETag!.ToString();
        var secondCreated = await adminClient.PostAsJsonAsync("/api/commodities", new { code = "RICE2", name = "Rice flour" });
        Assert.Equal(HttpStatusCode.Created, secondCreated.StatusCode);
        var secondCommodity = (await secondCreated.Content.ReadFromJsonAsync<CommodityResponse>())!;

        var detail = await purchaserClient.GetAsync($"/api/commodities/{commodity.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(etag, detail.Headers.ETag?.ToString());
        var list = await adminClient.GetFromJsonAsync<CatalogPage<CommodityResponse>>("/api/commodities?q=RICE&sortBy=name&sortDirection=desc&pageSize=1");
        Assert.Equal(2, list!.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(1, list.PageSize);
        Assert.True(list.HasMore);
        Assert.Equal(secondCommodity.Id, Assert.Single(list.Items).Id);
        var secondPage = await adminClient.GetFromJsonAsync<CatalogPage<CommodityResponse>>("/api/commodities?q=RICE&sortBy=name&sortDirection=desc&page=2&pageSize=1");
        Assert.Equal(2, secondPage!.TotalCount);
        Assert.Equal(2, secondPage.Page);
        Assert.False(secondPage.HasMore);
        Assert.Equal(commodity.Id, Assert.Single(secondPage.Items).Id);
        Assert.True(secondPage.Items.Single().CanEdit);
        Assert.True(secondPage.Items.Single().CanDelete);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await purchaserClient.PostAsJsonAsync("/api/commodities", new { code = "JP", name = "Japan" })).StatusCode);
        var otherList = await otherAdminClient.GetFromJsonAsync<CatalogPage<CommodityResponse>>("/api/commodities?q=RICE");
        Assert.False(otherList!.Items.Single(x => x.Id == commodity.Id).CanEdit);
        Assert.False(otherList.Items.Single(x => x.Id == commodity.Id).CanDelete);

        using var missing = new HttpRequestMessage(HttpMethod.Patch, $"/api/commodities/{commodity.Id}")
            { Content = JsonContent.Create(new { name = "Milled rice updated" }) };
        Assert.Equal((HttpStatusCode)428, (await adminClient.SendAsync(missing)).StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/commodities/{commodity.Id}")
            { Content = JsonContent.Create(new { name = "Milled rice updated" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await adminClient.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, updated.Headers.ETag?.ToString());
        Assert.Equal("Milled rice updated", (await updated.Content.ReadFromJsonAsync<CommodityResponse>())!.Name);
        using var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/commodities/{commodity.Id}")
            { Content = JsonContent.Create(new { name = "Outdated" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/commodities/{commodity.Id}");
        var otherCsrf = await otherAdminClient.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        otherAdminClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", otherCsrf.GetProperty("token").GetString());
        using var otherPatch = new HttpRequestMessage(HttpMethod.Patch, $"/api/commodities/{commodity.Id}")
            { Content = JsonContent.Create(new { name = "Unauthorized" }) };
        otherPatch.Headers.TryAddWithoutValidation("If-Match", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await otherAdminClient.SendAsync(otherPatch)).StatusCode);
        delete.Headers.TryAddWithoutValidation("If-Match", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.SendAsync(delete)).StatusCode);
        var secondDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/commodities/{secondCommodity.Id}");
        secondDelete.Headers.TryAddWithoutValidation("If-Match", secondCreated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.SendAsync(secondDelete)).StatusCode);
        Assert.Equal(0, (await adminClient.GetFromJsonAsync<CatalogPage<CommodityResponse>>("/api/commodities?q=RICE"))!.TotalCount);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(3, await db.CatalogEvents.CountAsync(x => x.ResourceId == commodity.Id));
        var archived = await db.Commodities.IgnoreQueryFilters().SingleAsync(x => x.Id == commodity.Id);
        Assert.True(archived.IsArchived);
        Assert.True(await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM ledger.catalog_commodities WHERE catalog_commodity_id = {commodity.Id}) AS \"Value\"").SingleAsync());
    }

    [Fact]
    public async Task Archive_rejects_compatibility_reference_with_uppercase_braced_ledger_uuid()
    {
        using var s = await LedgerScenario.CreateAsync(factory);
        using var created = await s.Admin.PostAsJsonAsync("/api/commodities", new { code = $"ARCHIVE-{Guid.NewGuid():N}", name = "Referenced commodity" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var commodity = (await created.Content.ReadFromJsonAsync<CommodityResponse>())!;
        var etag = created.Headers.ETag!.ToString();
        var ledgerCommodityId = await LedgerCommodityIdAsync(commodity.Id);
        await LedgerScenario.SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(),
            facilityId = s.FacilityId,
            inputProducts = new[] { $"{ledgerCommodityId.ToString().ToUpperInvariant()}:HEAD_RICE" },
            outputCommodityId = s.CommodityId,
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
            evidenceId = s.EvidenceId
        });

        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/commodities/{commodity.Id}");
        delete.Headers.TryAddWithoutValidation("If-Match", etag);
        using var response = await s.Admin.SendAsync(delete);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await db.Commodities.IgnoreQueryFilters().SingleAsync(x => x.Id == commodity.Id);
        Assert.False(stored.IsArchived);
    }

    private async Task<Guid> LedgerCommodityIdAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Database.SqlQuery<Guid>($"SELECT ledger_commodity_id AS \"Value\" FROM ledger.catalog_commodities WHERE catalog_commodity_id = {id}").SingleAsync();
    }

    [Fact]
    public async Task Invalid_and_duplicate_commodity_codes_are_rejected()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var code = $"M{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/commodities", new { code = " ", name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/commodities", new { code, name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/commodities", new { code = code.ToLowerInvariant(), name = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/commodities?page=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/commodities?pageSize=101")).StatusCode);
        using var missing = new HttpRequestMessage(HttpMethod.Delete, $"/api/commodities/{Guid.NewGuid()}");
        missing.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(missing)).StatusCode);
    }
}
