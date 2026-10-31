using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class PlanningYieldRuleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Assigned_mill_manager_publishes_yield_and_owner_reads_it()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var compatibilityId = Guid.NewGuid();
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post,
            "/api/ledger/compatibilities", new
            {
                id = compatibilityId, facilityId = scenario.FacilityId,
                inputProducts = new[] { $"{scenario.CommodityId}:HEAD_RICE" },
                outputCommodityId = scenario.CommodityId,
                effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = scenario.EvidenceId
            });
        var ruleId = Guid.NewGuid();
        var request = new
        {
            id = ruleId, facilityId = scenario.FacilityId,
            compatibilityPolicyId = compatibilityId, versionNo = 1,
            outputFraction = "0.9", effectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-2),
            evidenceId = scenario.EvidenceId, reviewedBy = "Synthetic reviewer",
            reviewAuthority = "Synthetic authority", reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        var denied = await scenario.Operator.PostAsJsonAsync("/api/ledger/planning-yield-rules", request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var published = await scenario.Manager.PostAsJsonAsync("/api/ledger/planning-yield-rules", request);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var reader = factory.CreateRealCookieClient(staff, session);
        var response = await reader.GetAsync($"/api/ledger/planning-yield-rules/applicable" +
            $"?ownerId={scenario.OwnerId}&facilityId={scenario.FacilityId}&compatibilityPolicyId={compatibilityId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rule = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ruleId, rule.GetProperty("id").GetGuid());
        Assert.Equal("0.9", rule.GetProperty("outputFraction").GetString());
        var (foreignStaff, foreignSession) = await factory.SeedUserAsync(Roles.ExportStaff,
            scenario.ForeignOwnerId);
        using var foreignReader = factory.CreateRealCookieClient(foreignStaff, foreignSession);
        var foreignResponse = await foreignReader.GetAsync($"/api/ledger/planning-yield-rules/applicable" +
            $"?ownerId={scenario.OwnerId}&facilityId={scenario.FacilityId}&compatibilityPolicyId={compatibilityId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    }
}
