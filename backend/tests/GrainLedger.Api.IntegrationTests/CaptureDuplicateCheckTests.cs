using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CaptureDuplicateCheckTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Advisory_checks_find_only_active_records_in_the_current_company()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var otherCompany = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var farmerId = Guid.NewGuid();
        var foreignFarmerId = Guid.NewGuid();
        var plotId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
            db.Farmers.AddRange(
                new Farmer { Id = farmerId, CompanyId = company.Id, Code = "DUP-A", Name = "Nguyễn Văn A", Contact = "0900 123 456", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = user.Id },
                new Farmer { Id = foreignFarmerId, CompanyId = otherCompany.Id, Code = "DUP-B", Name = "Nguyễn Văn A", Contact = "0900 123 456", CreatedAt = DateTimeOffset.UtcNow },
                new Farmer { Id = Guid.NewGuid(), CompanyId = company.Id, Code = "DUP-DELETED", Name = "Nguyễn Văn A", Contact = "0900 123 456", CreatedAt = DateTimeOffset.UtcNow, IsDeleted = true });
            await db.SaveChangesAsync();
            db.Plots.AddRange(
                new Plot { Id = plotId, CompanyId = company.Id, FarmerId = farmerId, Code = "DUP-PLOT", BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m, CreatedBy = user.Id },
                new Plot { Id = Guid.NewGuid(), CompanyId = otherCompany.Id, FarmerId = foreignFarmerId, Code = "OTHER-PLOT", BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m });
            await db.SaveChangesAsync();
        }

        using var farmerResponse = await client.PostAsJsonAsync("/api/farmers/duplicate-check",
            new FarmerDuplicateRequest("  Nguyễn   Văn A  ", "0900-123-456", null));
        Assert.Equal(HttpStatusCode.OK, farmerResponse.StatusCode);
        var farmers = await farmerResponse.Content.ReadFromJsonAsync<FarmerDuplicateResponse>();
        Assert.Single(farmers!.Items);
        Assert.Equal(farmerId, farmers.Items[0].Id);
        Assert.Contains("SAME_NAME", farmers.Items[0].Reasons);
        Assert.Contains("SAME_CONTACT", farmers.Items[0].Reasons);

        using var excluded = await client.PostAsJsonAsync("/api/farmers/duplicate-check",
            new FarmerDuplicateRequest("Nguyễn Văn A", null, farmerId));
        Assert.Empty((await excluded.Content.ReadFromJsonAsync<FarmerDuplicateResponse>())!.Items);

        var point = JsonDocument.Parse("{\"type\":\"Point\",\"coordinates\":[106.35001,10.4]}").RootElement.Clone();
        using var plotResponse = await client.PostAsJsonAsync("/api/plots/duplicate-check", new PlotDuplicateRequest(point, null));
        Assert.Equal(HttpStatusCode.OK, plotResponse.StatusCode);
        var plots = await plotResponse.Content.ReadFromJsonAsync<PlotDuplicateResponse>();
        Assert.Single(plots!.Items);
        Assert.Equal(plotId, plots.Items[0].Id);
        Assert.InRange(plots.Items[0].DistanceMeters, 0, 20);
        Assert.Equal("POINT_ONLY", plots.Coverage);

        using var farAway = await client.PostAsJsonAsync("/api/plots/duplicate-check",
            new PlotDuplicateRequest(JsonDocument.Parse("{\"type\":\"Point\",\"coordinates\":[107,11]}").RootElement.Clone(), null));
        Assert.Empty((await farAway.Content.ReadFromJsonAsync<PlotDuplicateResponse>())!.Items);
    }
}
