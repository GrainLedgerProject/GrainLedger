using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CompanionCaptureCodeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Server_assigns_stable_farmer_and_plot_codes_and_rejects_reparenting()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());

        var capturedAt = DateTimeOffset.UtcNow;
        var farmerId = Guid.NewGuid();
        var farmerRequest = new { deviceId = "companion-test", clientOperationId = Guid.NewGuid(),
            capturedAt, payloadSchemaVersion = 2,
            payload = new { entityId = farmerId, name = "Farmer A", contact = (string?)null } };
        var createdFarmer = await client.PostAsJsonAsync("/api/farmers", farmerRequest);
        Assert.Equal(HttpStatusCode.Created, createdFarmer.StatusCode);
        var farmer = (await createdFarmer.Content.ReadFromJsonAsync<FarmerResponse>())!;
        Assert.Equal("FRM-0001", farmer.Code);
        var replay = await client.PostAsJsonAsync("/api/farmers", farmerRequest);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(farmer.Code, (await replay.Content.ReadFromJsonAsync<FarmerResponse>())!.Code);

        var secondFarmerId = Guid.NewGuid();
        var second = await client.PostAsJsonAsync("/api/farmers", new { deviceId = "companion-test",
            clientOperationId = Guid.NewGuid(), capturedAt, payloadSchemaVersion = 2,
            payload = new { entityId = secondFarmerId, name = "Farmer B", contact = (string?)null } });
        Assert.Equal("FRM-0002", (await second.Content.ReadFromJsonAsync<FarmerResponse>())!.Code);

        var firstPlotId = Guid.NewGuid();
        object PlotRequest(Guid plotId) => new { deviceId = "companion-test", clientOperationId = Guid.NewGuid(),
            capturedAt, payloadSchemaVersion = 2, payload = new { entityId = plotId, farmerId,
                geometry = new { type = "Point", coordinates = new[] { 106.35, 10.4 } }, areaHa = 1.25m } };
        var firstPlot = await client.PostAsJsonAsync("/api/plots", PlotRequest(firstPlotId));
        Assert.Equal(HttpStatusCode.Created, firstPlot.StatusCode);
        Assert.Equal("PLT-0001-01", (await firstPlot.Content.ReadFromJsonAsync<PlotResponse>())!.Code);
        var secondPlot = await client.PostAsJsonAsync("/api/plots", PlotRequest(Guid.NewGuid()));
        Assert.Equal("PLT-0001-02", (await secondPlot.Content.ReadFromJsonAsync<PlotResponse>())!.Code);

        var reparent = await client.PutAsJsonAsync($"/api/plots/{firstPlotId}", new {
            farmerId = secondFarmerId, code = "PLT-0001-01", expectedVersion = 1,
            geometry = new { type = "Point", coordinates = new[] { 106.35, 10.4 } }, areaHa = 1.25m });
        Assert.Equal(HttpStatusCode.Conflict, reparent.StatusCode);
        Assert.Equal(farmerId, (await client.GetFromJsonAsync<PlotReferenceResponse>($"/api/plots/{firstPlotId}"))!.FarmerId);
    }
}
