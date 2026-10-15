using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class PlotTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task AddCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private async Task<Farmer> SeedFarmerAsync(Guid companyId, string? code = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        var farmer = new Farmer
        {
            Id = Guid.NewGuid(), CompanyId = companyId, Code = code ?? Guid.NewGuid().ToString("N"),
            Name = "Test Farmer", CreatedAt = DateTimeOffset.UtcNow
        };
        db.Farmers.Add(farmer);
        await db.SaveChangesAsync();
        return farmer;
    }

    private static object PointRequest(Guid entityId, Guid farmerId, Guid operationId, decimal areaHa = 1.25m,
        string? code = null, DateTimeOffset? capturedAt = null) => new
    {
        deviceId = "plot-test-device", clientOperationId = operationId,
        capturedAt = capturedAt ?? DateTimeOffset.UtcNow, payloadSchemaVersion = 1,
        payload = new
        {
            entityId, farmerId, code,
            geometry = new { type = "Point", coordinates = new[] { 106.35, 10.40 } }, areaHa
        }
    };

    [Fact]
    public async Task Create_list_detail_retry_and_tenant_scope()
    {
        var companyA = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var companyB = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var farmer = await SeedFarmerAsync(companyA.Id);
        var (userA, sessionA) = await factory.SeedUserAsync(Roles.PurchasingStaff, companyA.Id);
        var (userB, sessionB) = await factory.SeedUserAsync(Roles.PurchasingStaff, companyB.Id);
        using var clientA = factory.CreateRealCookieClient(userA, sessionA);
        using var clientB = factory.CreateRealCookieClient(userB, sessionB);
        await AddCsrfAsync(clientA);
        await AddCsrfAsync(clientB);
        var entityId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;
        var input = PointRequest(entityId, farmer.Id, operationId, capturedAt: capturedAt);

        var created = await clientA.PostAsJsonAsync("/api/plots", input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/plots/{entityId}", created.Headers.Location?.OriginalString);
        var plot = (await created.Content.ReadFromJsonAsync<PlotResponse>())!;
        Assert.Equal(entityId, plot.Id);
        Assert.Equal(farmer.Id, plot.FarmerId);
        Assert.Equal($"TR-{entityId:N}".ToUpperInvariant(), plot.Code);
        Assert.Equal(1.25m, plot.AreaHa);
        Assert.Equal("Point", plot.Geometry.GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.OK, (await clientA.PostAsJsonAsync("/api/plots", input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientA.PostAsJsonAsync("/api/plots",
            PointRequest(entityId, farmer.Id, operationId, 1.250m, capturedAt: capturedAt))).StatusCode);

        var details = await clientA.GetFromJsonAsync<PlotResponse>($"/api/plots/{entityId}");
        Assert.Equal(plot.Code, details!.Code);
        var list = await clientA.GetFromJsonAsync<PlotListResponse>($"/api/plots?farmerId={farmer.Id}");
        Assert.Contains(list!.Items, x => x.Id == entityId && x.AreaHa == 1.25m);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/plots/{entityId}")).StatusCode);
        var listB = await clientB.GetFromJsonAsync<PlotListResponse>("/api/plots");
        Assert.DoesNotContain(listB!.Items, x => x.Id == entityId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Plots.CountAsync(x => x.Id == entityId && x.CompanyId == companyA.Id));
        Assert.Equal(userA.Id, (await db.Plots.SingleAsync(x => x.Id == entityId)).CreatedBy);
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CompanyId == companyA.Id && x.CorrelationId == operationId));
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.CompanyId == companyA.Id && x.ClientOperationId == operationId));
    }

    [Fact]
    public async Task Invalid_geometry_cross_company_farmer_and_conflicting_retry_do_not_write()
    {
        var companyA = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var companyB = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var farmerA = await SeedFarmerAsync(companyA.Id);
        var farmerB = await SeedFarmerAsync(companyB.Id);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, companyA.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);

        var foreign = await client.PostAsJsonAsync("/api/plots",
            PointRequest(Guid.NewGuid(), farmerB.Id, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);

        var invalid = await client.PostAsJsonAsync("/api/plots", new
        {
            deviceId = "plot-test-device", clientOperationId = Guid.NewGuid(),
            capturedAt = DateTimeOffset.UtcNow, payloadSchemaVersion = 1,
            payload = new
            {
                entityId = Guid.NewGuid(), farmerId = farmerA.Id,
                geometry = new { type = "Point", coordinates = new[] { 106.35, 100.0 } }, areaHa = 1.25m
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var entityId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;
        var valid = PointRequest(entityId, farmerA.Id, operationId, capturedAt: capturedAt);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/plots", valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/plots",
            PointRequest(entityId, farmerA.Id, operationId, 2m, capturedAt: capturedAt))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Plots.CountAsync(x => x.CompanyId == companyA.Id));
        Assert.Equal(1.25m, (await db.Plots.SingleAsync(x => x.Id == entityId)).AreaHa);
    }

    [Fact]
    public async Task Polygon_must_be_topologically_valid_and_code_unique_per_farmer()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var farmerA = await SeedFarmerAsync(company.Id);
        var farmerB = await SeedFarmerAsync(company.Id);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);
        var ring = new[] { new[] { 106.0, 10.0 }, new[] { 107.0, 10.0 }, new[] { 107.0, 11.0 },
            new[] { 106.0, 11.0 }, new[] { 106.0, 10.0 } };
        var valid = new
        {
            deviceId = "plot-test-device", clientOperationId = Guid.NewGuid(), capturedAt = DateTimeOffset.UtcNow,
            payloadSchemaVersion = 1,
            payload = new { entityId = Guid.NewGuid(), farmerId = farmerA.Id, code = "P-01",
                geometry = new { type = "Polygon", coordinates = new[] { ring } }, areaHa = 2m }
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/plots", valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/plots",
            PointRequest(Guid.NewGuid(), farmerA.Id, Guid.NewGuid(), code: "P-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/plots",
            PointRequest(Guid.NewGuid(), farmerB.Id, Guid.NewGuid(), code: "P-01"))).StatusCode);

        var bowTie = new[] { new[] { 106.0, 10.0 }, new[] { 107.0, 11.0 }, new[] { 106.0, 11.0 },
            new[] { 107.0, 10.0 }, new[] { 106.0, 10.0 } };
        var invalid = await client.PostAsJsonAsync("/api/plots", new
        {
            deviceId = "plot-test-device", clientOperationId = Guid.NewGuid(), capturedAt = DateTimeOffset.UtcNow,
            payloadSchemaVersion = 1,
            payload = new { entityId = Guid.NewGuid(), farmerId = farmerA.Id,
                geometry = new { type = "Polygon", coordinates = new[] { bowTie } }, areaHa = 2m }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
