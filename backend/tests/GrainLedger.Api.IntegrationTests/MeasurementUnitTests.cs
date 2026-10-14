using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MeasurementUnitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_and_updates_unit_customer_reads_and_audit_is_recorded()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        var csrf = await adminClient.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        adminClient.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        var created = await adminClient.PostAsJsonAsync("/api/measurement-units", new { code = " kg ", name = " Kilogram ", quantityKind = " mass " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var unit = (await created.Content.ReadFromJsonAsync<MeasurementUnitResponse>())!;
        Assert.Equal("KG", unit.Code);
        Assert.Equal("Kilogram", unit.Name);
        Assert.Equal("MASS", unit.QuantityKind);
        Assert.Equal($"/api/measurement-units/{unit.Id}", created.Headers.Location?.OriginalString);
        var etag = created.Headers.ETag!.ToString();

        var detail = await purchaserClient.GetAsync($"/api/measurement-units/{unit.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(etag, detail.Headers.ETag?.ToString());
        var list = await purchaserClient.GetFromJsonAsync<MeasurementUnitListResponse>("/api/measurement-units?q=KG");
        Assert.Contains(list!.Items, x => x.Id == unit.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await purchaserClient.PostAsJsonAsync("/api/measurement-units", new { code = "HA", name = "Hectare", quantityKind = "AREA" })).StatusCode);

        using var missing = new HttpRequestMessage(HttpMethod.Patch, $"/api/measurement-units/{unit.Id}")
            { Content = JsonContent.Create(new { name = "Kilogram updated" }) };
        Assert.Equal((HttpStatusCode)428, (await adminClient.SendAsync(missing)).StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/measurement-units/{unit.Id}")
            { Content = JsonContent.Create(new { name = "Kilogram updated" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await adminClient.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, updated.Headers.ETag?.ToString());
        Assert.Equal("Kilogram updated", (await updated.Content.ReadFromJsonAsync<MeasurementUnitResponse>())!.Name);
        using var changeKind = new HttpRequestMessage(HttpMethod.Patch, $"/api/measurement-units/{unit.Id}")
            { Content = JsonContent.Create(new { name = "Kilogram updated", quantityKind = "AREA" }) };
        changeKind.Headers.TryAddWithoutValidation("If-Match", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.SendAsync(changeKind)).StatusCode);
        using var changeCode = new HttpRequestMessage(HttpMethod.Patch, $"/api/measurement-units/{unit.Id}")
            { Content = JsonContent.Create(new { name = "Kilogram updated", code = "G" }) };
        changeCode.Headers.TryAddWithoutValidation("If-Match", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.SendAsync(changeCode)).StatusCode);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/measurement-units/{unit.Id}")
            { Content = JsonContent.Create(new { name = "Outdated" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == unit.Id));
        Assert.Equal("MASS", (await db.MeasurementUnits.SingleAsync(x => x.Id == unit.Id)).QuantityKind);
    }

    [Fact]
    public async Task Invalid_and_duplicate_unit_codes_are_rejected()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var code = $"M{Guid.NewGuid():N}"[..30];
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/measurement-units", new { code = " ", name = "Test", quantityKind = "MASS" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/measurement-units", new { code = new string('A', 31), name = "Test", quantityKind = "MASS" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/measurement-units", new { code, name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/measurement-units", new { code, name = "Test", quantityKind = "UNKNOWN" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/measurement-units", new { code, name = "Test", quantityKind = "MASS" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/measurement-units", new { code = code.ToLowerInvariant(), name = "Duplicate", quantityKind = "MASS" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/measurement-units?page=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/measurement-units?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/measurement-units/{Guid.NewGuid()}")).StatusCode);
    }
}
