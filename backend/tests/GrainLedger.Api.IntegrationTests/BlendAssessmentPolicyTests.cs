using System.Net;
using System.Net.Http.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BlendAssessmentPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Manager_publishes_versioned_policy_and_staff_can_read_but_not_publish()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (manager, managerSession) = await factory.SeedUserAsync(Roles.CustomerManager, company.Id);
        var (staff, staffSession) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        var market = Guid.NewGuid();
        var commodity = Guid.NewGuid();
        var substance = Guid.NewGuid();
        var attribute = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Markets.Add(new Market { Id = market, Code = $"M{market:N}", Name = "Synthetic market" });
            db.Commodities.Add(new Commodity { Id = commodity, Code = $"C{commodity:N}", Name = "Synthetic commodity" });
            db.ActiveSubstances.Add(new ActiveSubstance { Id = substance, Code = $"S{substance:N}", Name = "Synthetic substance" });
            db.QualityAttributes.Add(new QualityAttribute { Id = attribute, Code = $"Q{attribute:N}", Name = "Synthetic quality", Unit = "%" });
            await db.SaveChangesAsync();
        }
        using var managerClient = factory.CreateRealCookieClient(manager, managerSession);
        using var staffClient = factory.CreateRealCookieClient(staff, staffSession);
        async Task AddCsrfAsync(HttpClient client)
        {
            var value = (await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/auth/csrf"))
                .GetProperty("token").GetString();
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value);
        }
        await AddCsrfAsync(managerClient);
        await AddCsrfAsync(staffClient);
        var request = new PublishBlendAssessmentPolicyRequest(Guid.NewGuid(), market,
            commodity, 1, DateTimeOffset.UtcNow.AddMinutes(-2), null, [substance],
            "0.1", "AS_RECEIVED", [new PublishedQualityPropagationRule(attribute, "%",
                "MASS_WEIGHTED_MEAN")], "Synthetic approved policy", "Test reviewer",
            "Test authority", DateTimeOffset.UtcNow.AddMinutes(-3));
        var denied = await staffClient.PostAsJsonAsync("/api/blend-assessment-policies", request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var published = await managerClient.PostAsJsonAsync("/api/blend-assessment-policies", request);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var read = await staffClient.GetFromJsonAsync<PublishedBlendAssessmentPolicy>(
            $"/api/blend-assessment-policies/applicable?marketId={market}&commodityId={commodity}");
        Assert.NotNull(read);
        Assert.Equal(request.Id, read.Id);
        Assert.Equal(company.Id, read.CompanyId);
        Assert.Equal("0.1", read.MrlMarginFraction);
        Assert.Equal(substance, Assert.Single(read.RequiredSubstanceIds));
        Assert.Equal(attribute, Assert.Single(read.QualityRules).AttributeId);
    }
}
