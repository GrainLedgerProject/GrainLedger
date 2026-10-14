using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class QualityAttributeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_and_updates_attribute_customer_reads_and_audit_is_recorded()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        var csrf = await adminClient.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        adminClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        var created = await adminClient.PostAsJsonAsync("/api/quality-attributes", new { code = " moisture ", name = " Moisture content ", unit = " % " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var attribute = (await created.Content.ReadFromJsonAsync<QualityAttributeResponse>())!;
        Assert.Equal("MOISTURE", attribute.Code);
        Assert.Equal("Moisture content", attribute.Name);
        Assert.Equal("%", attribute.Unit);
        Assert.Equal($"/api/quality-attributes/{attribute.Id}", created.Headers.Location?.OriginalString);
        var etag = created.Headers.ETag!.ToString();

        var detail = await purchaserClient.GetAsync($"/api/quality-attributes/{attribute.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(etag, detail.Headers.ETag?.ToString());
        var list = await purchaserClient.GetFromJsonAsync<QualityAttributeListResponse>("/api/quality-attributes?q=MOISTURE");
        Assert.Contains(list!.Items, x => x.Id == attribute.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await purchaserClient.PostAsJsonAsync("/api/quality-attributes", new { code = "OTHER", name = "Other", unit = "%" })).StatusCode);

        using var missing = new HttpRequestMessage(HttpMethod.Patch, $"/api/quality-attributes/{attribute.Id}")
            { Content = JsonContent.Create(new { name = "Moisture content updated" }) };
        Assert.Equal((HttpStatusCode)428, (await adminClient.SendAsync(missing)).StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/quality-attributes/{attribute.Id}")
            { Content = JsonContent.Create(new { name = "Moisture content updated" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await adminClient.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, updated.Headers.ETag?.ToString());
        Assert.Equal("Moisture content updated", (await updated.Content.ReadFromJsonAsync<QualityAttributeResponse>())!.Name);
        using var changeUnit = new HttpRequestMessage(HttpMethod.Patch, $"/api/quality-attributes/{attribute.Id}")
            { Content = JsonContent.Create(new { name = "Moisture content updated", unit = "kg" }) };
        changeUnit.Headers.TryAddWithoutValidation("If-Match", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.SendAsync(changeUnit)).StatusCode);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/quality-attributes/{attribute.Id}")
            { Content = JsonContent.Create(new { name = "Outdated" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == attribute.Id));
        Assert.Equal("%", (await db.QualityAttributes.SingleAsync(x => x.Id == attribute.Id)).Unit);
    }

    [Fact]
    public async Task Invalid_and_duplicate_attribute_codes_are_rejected()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var code = $"M{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/quality-attributes", new { code = " ", name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/quality-attributes", new { code, name = "Test", unit = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/quality-attributes", new { code, name = "Test", unit = "%" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/quality-attributes", new { code = code.ToLowerInvariant(), name = "Duplicate", unit = "%" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/quality-attributes?page=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/quality-attributes?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/quality-attributes/{Guid.NewGuid()}")).StatusCode);
    }
}
