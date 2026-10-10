using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class FarmerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task AddCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static object Request(Guid entityId, Guid operationId, string code, string name = "Farmer A") => new
    {
        deviceId = "test-device", clientOperationId = operationId, capturedAt = DateTimeOffset.UtcNow,
        payloadSchemaVersion = 1, payload = new { entityId, code, name, contact = (string?)null }
    };

    [Fact]
    public async Task Create_retry_and_tenant_scope_use_real_cookie_and_postgres()
    {
        var companyA = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var companyB = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (userA, sessionA) = await factory.SeedUserAsync(Roles.PurchasingStaff, companyA.Id);
        var (userB, sessionB) = await factory.SeedUserAsync(Roles.PurchasingStaff, companyB.Id);
        using var clientA = factory.CreateRealCookieClient(userA, sessionA);
        using var clientB = factory.CreateRealCookieClient(userB, sessionB);
        await AddCsrfAsync(clientA);
        await AddCsrfAsync(clientB);

        var entityId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;
        var input = new { deviceId = "test-device", clientOperationId = operationId, capturedAt,
            payloadSchemaVersion = 1, payload = new { entityId, code = "F-001", name = "Farmer A" } };
        var created = await clientA.PostAsJsonAsync("/api/farmers", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(entityId, (await created.Content.ReadFromJsonAsync<FarmerResponse>())!.Id);
        Assert.Equal(HttpStatusCode.OK, (await clientA.PostAsJsonAsync("/api/farmers", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clientA.PostAsJsonAsync("/api/farmers", new
        {
            deviceId = "test-device", clientOperationId = operationId, capturedAt,
            payloadSchemaVersion = 1, payload = new { entityId, code = "F-002", name = "Farmer A" }
        })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await clientA.GetAsync($"/api/farmers/{entityId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/farmers/{entityId}")).StatusCode);
        var listB = await clientB.GetFromJsonAsync<FarmerListResponse>("/api/farmers");
        Assert.DoesNotContain(listB!.Items, x => x.Id == entityId);
        Assert.Equal(HttpStatusCode.Created, (await clientB.PostAsJsonAsync("/api/farmers", Request(Guid.NewGuid(), Guid.NewGuid(), "F-001"))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Farmers.CountAsync(x => x.CompanyId == companyA.Id && x.Id == entityId));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CompanyId == companyA.Id && x.CorrelationId == operationId));
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.CompanyId == companyA.Id && x.ClientOperationId == operationId));
    }

    [Fact]
    public async Task Validation_and_duplicate_code_do_not_add_a_second_farmer()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);
        var code = "F-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/farmers",
            Request(Guid.NewGuid(), Guid.NewGuid(), "  "))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/farmers",
            Request(Guid.NewGuid(), Guid.NewGuid(), code))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/farmers",
            Request(Guid.NewGuid(), Guid.NewGuid(), code))).StatusCode);
        var found = await client.GetFromJsonAsync<FarmerListResponse>($"/api/farmers?q={code}&page=1&pageSize=1");
        Assert.Single(found!.Items);
    }

    [Fact]
    public async Task Authentication_permission_and_csrf_are_enforced()
    {
        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/farmers")).StatusCode);

        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (exporter, session) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
        using var client = factory.CreateRealCookieClient(exporter, session);
        await AddCsrfAsync(client);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/farmers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/farmers",
            Request(Guid.NewGuid(), Guid.NewGuid(), "F-401"))).StatusCode);

        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        Assert.Equal(HttpStatusCode.BadRequest, (await purchaserClient.PostAsJsonAsync("/api/farmers",
            Request(Guid.NewGuid(), Guid.NewGuid(), "F-CSRF"))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_retries_create_one_farmer_and_one_event()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);
        var farmerId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var request = Request(farmerId, operationId, "F-" + Guid.NewGuid().ToString("N")[..8]);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/farmers", request),
            client.PostAsJsonAsync("/api/farmers", request));
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Farmers.CountAsync(x => x.Id == farmerId));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CompanyId == company.Id && x.CorrelationId == operationId));
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.CompanyId == company.Id && x.ClientOperationId == operationId));
    }
}
