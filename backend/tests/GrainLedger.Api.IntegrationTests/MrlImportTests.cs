using System.Net;
using System.Net.Http.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MrlImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, Guid MarketId, Guid CommodityId, string SubstanceCode)> SetupAsync()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var market = await client.PostAsJsonAsync("/api/markets", new { code = $"M-{suffix}", name = "Test market" });
        var commodity = await client.PostAsJsonAsync("/api/commodities", new { code = $"C-{suffix}", name = "Test commodity" });
        var code = $"S-{suffix}";
        var substance = await client.PostAsJsonAsync("/api/active-substances", new { code, name = "Test substance" });
        var unit = await client.PostAsJsonAsync("/api/measurement-units", new
        { code = "MG/KG", name = "Milligram per kilogram", quantityKind = "RESIDUE_CONCENTRATION" });
        Assert.Equal(HttpStatusCode.Created, market.StatusCode);
        Assert.Equal(HttpStatusCode.Created, commodity.StatusCode);
        Assert.Equal(HttpStatusCode.Created, substance.StatusCode);
        Assert.True(unit.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        return (client, (await market.Content.ReadFromJsonAsync<MarketResponse>())!.Id,
            (await commodity.Content.ReadFromJsonAsync<CommodityResponse>())!.Id, code);
    }

    [Fact]
    public async Task Imports_a_draft_and_limits_atomically_with_open_ended_interval()
    {
        var (client, marketId, commodityId, code) = await SetupAsync();
        using (client)
        {
            var result = await client.PostAsJsonAsync("/api/mrl-versions/import", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z",
                effectiveTo = (string?)null, sourceRef = "Verified rule, section 3",
                limits = new[] { new { substanceCode = code.ToLowerInvariant(), limitValue = "0.0100", unit = "MG/KG" } }
            });
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
            var version = (await result.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            Assert.Equal("DRAFT", version.Status);
            Assert.Null(version.EffectiveTo);
            Assert.NotNull(result.Headers.ETag);
            var limits = await client.GetFromJsonAsync<MrlLimitListResponse>($"/api/mrl-versions/{version.Id}/limits");
            Assert.Equal(0.01m, Assert.Single(limits!.Items).LimitValue);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal("MRL_VERSION_IMPORTED", (await db.CatalogEvents.SingleAsync(x => x.ResourceId == version.Id)).EventType);
        }
    }

    [Fact]
    public async Task Invalid_or_unknown_substances_create_no_partial_draft()
    {
        var (client, marketId, commodityId, code) = await SetupAsync();
        using (client)
        {
            var duplicate = await client.PostAsJsonAsync("/api/mrl-versions/import", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Verified rule",
                limits = new[] { new { substanceCode = code, limitValue = "0.01", unit = "mg/kg" },
                    new { substanceCode = code, limitValue = "0.02", unit = "mg/kg" } }
            });
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            var unknown = await client.PostAsJsonAsync("/api/mrl-versions/import", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Verified rule",
                limits = new[] { new { substanceCode = "NOT-IN-CATALOG", limitValue = "0.01", unit = "mg/kg" } }
            });
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.False(await db.MrlVersions.AnyAsync(x => x.MarketId == marketId && x.CommodityId == commodityId));
        }
    }
}
