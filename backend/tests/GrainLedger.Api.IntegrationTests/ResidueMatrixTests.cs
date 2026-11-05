using System.Net;
using System.Text.Json.Nodes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Invalid_later_step_rejects_entire_profile_and_stale_revision_preserves_history()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var profile = f.RuleBody();
        ResidueScenario.AddStep(profile, "DRYING", "RAW_PADDY", "DRIED_PADDY");
        ResidueScenario.AddStep(profile, "MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5", "0.5");
        var before = await GetAsync(s.Admin, "/api/residue-rules");
        var invalid = (JsonObject)profile.DeepClone();
        invalid["steps"]![1]!["factorUpper"] = "-1";
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", invalid, HttpStatusCode.BadRequest);
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Admin, "/api/residue-rules")).GetRawText());

        var id = await f.PublishRuleAsync(profile, adopt: false);
        var revision = (JsonObject)profile.DeepClone();
        revision["id"] = Guid.NewGuid();
        revision["versionNo"] = 2;
        revision["supersedesId"] = id;
        revision["steps"]![1]!["factorUpper"] = "0.6";
        await SendAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", revision);
        var after = await GetAsync(s.Admin, "/api/residue-rules");
        var stale = (JsonObject)revision.DeepClone();
        stale["id"] = Guid.NewGuid();
        stale["versionNo"] = 3;
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", stale, HttpStatusCode.Conflict);
        Assert.Equal(after.GetRawText(), (await GetAsync(s.Admin, "/api/residue-rules")).GetRawText());
    }

    [Theory]
    [InlineData("duplicate-method")]
    [InlineData("duplicate-path")]
    [InlineData("conditional-overlap")]
    [InlineData("different-anchor")]
    [InlineData("legacy-basis")]
    public async Task Ambiguous_or_legacy_profile_is_rejected_atomically(string invalid)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var profile = f.RuleBody();
        ResidueScenario.AddStep(profile, "DRYING", "RAW_PADDY", "DRIED_PADDY");
        if (invalid == "duplicate-method") profile["laboratory"]!.AsArray().Add(profile["laboratory"]![0]!.DeepClone());
        if (invalid is "duplicate-path" or "conditional-overlap")
        {
            var duplicate = profile["steps"]![0]!.DeepClone();
            duplicate["id"] = Guid.NewGuid();
            if (invalid == "conditional-overlap") duplicate["conditions"] = "Reviewed condition";
            profile["steps"]!.AsArray().Add(duplicate);
        }
        if (invalid == "different-anchor")
        {
            ResidueScenario.AddStep(profile, "MILLING", "DRIED_PADDY", "HEAD_RICE", semantics: "CUMULATIVE_CONCENTRATION", anchorStage: "RAW_PADDY");
            var duplicate = profile["steps"]![1]!.DeepClone();
            duplicate["id"] = Guid.NewGuid();
            duplicate["anchorStage"] = "DRIED_PADDY";
            profile["steps"]!.AsArray().Add(duplicate);
        }
        if (invalid == "legacy-basis") profile["inputBasis"] = "AS_RECEIVED";
        var before = await GetAsync(s.Admin, "/api/residue-rules");
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", profile, HttpStatusCode.BadRequest);
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Admin, "/api/residue-rules")).GetRawText());
    }

    [Fact]
    public async Task Publication_enforces_scope_and_company_visibility()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var system = f.RuleBody();
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", system, HttpStatusCode.Forbidden);
        await f.PublishRuleAsync(system, adopt: false);
        var own = f.RuleBody();
        own["code"] = system["code"]!.DeepClone();
        own["companyId"] = s.OwnerId;
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-rules", own, HttpStatusCode.Forbidden);
        await RejectedAsync(s.ForeignCustomer, HttpMethod.Post, "/api/residue-rules", own, HttpStatusCode.Forbidden);
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-rules", own);
        var id = own["id"]!.GetValue<Guid>();
        Assert.Equal(id, (await GetAsync(s.Customer, $"/api/residue-rules/{id}")).GetProperty("id").GetGuid());
        using var hidden = await s.ForeignCustomer.GetAsync($"/api/residue-rules/{id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
