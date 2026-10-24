using System.Net;
using System.Text.Json.Nodes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResiduePolicySettingBatchTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Batch_publishes_each_setting_with_current_actor_and_replays_original_history()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var firstRule = f.RuleBody("NUMERIC_ABSOLUTE_UNCERTAINTY");
        var secondRule = f.RuleBody("ND", "LOD");
        await f.PublishRuleAsync(firstRule, adopt: false);
        await f.PublishRuleAsync(secondRule, adopt: false);
        var first = f.SettingBody(firstRule["code"]!.GetValue<string>());
        var second = f.SettingBody(secondRule["code"]!.GetValue<string>());
        second["reviewedBy"] = first["reviewedBy"]!.DeepClone();
        second["reviewedAt"] = first["reviewedAt"]!.DeepClone();
        second["effectiveFrom"] = first["effectiveFrom"]!.DeepClone();
        first["publishedBy"] = s.Accounts["admin"].User.Id;
        var body = new JsonObject { ["items"] = new JsonArray(first, second) };
        var result = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body);
        var items = result.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal(s.Accounts["customer"].User.Id, item.GetProperty("publishedBy").GetGuid()));
        Assert.NotEqual(items[0].GetProperty("id").GetGuid(), items[1].GetProperty("id").GetGuid());
        var history = await GetAsync(s.Customer, "/api/residue-policy-settings");
        var replay = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body);
        Assert.Equal(result.GetRawText(), replay.GetRawText());
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Customer, "/api/residue-policy-settings")).GetRawText());
        first["evidenceRef"] = "Different evidence cannot replace published UUID";
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body, HttpStatusCode.Conflict);
        Assert.Equal(history.GetRawText(), (await GetAsync(s.Customer, "/api/residue-policy-settings")).GetRawText());
    }

    [Theory]
    [InlineData("unreviewed", HttpStatusCode.BadRequest)]
    [InlineData("missing-series", HttpStatusCode.BadRequest)]
    [InlineData("foreign-company", HttpStatusCode.BadRequest)]
    [InlineData("duplicate-id", HttpStatusCode.BadRequest)]
    [InlineData("stale-version", HttpStatusCode.Conflict)]
    public async Task Invalid_later_setting_cannot_partially_publish_batch(string invalid, HttpStatusCode expected)
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var rule = f.RuleBody("NUMERIC_ABSOLUTE_UNCERTAINTY");
        var otherRule = f.RuleBody("ND", "LOD");
        await f.PublishRuleAsync(rule, adopt: false);
        await f.PublishRuleAsync(otherRule, adopt: false);
        var first = f.SettingBody(rule["code"]!.GetValue<string>());
        var second = f.SettingBody(otherRule["code"]!.GetValue<string>());
        if (invalid == "unreviewed") second["reviewedBy"] = "";
        if (invalid == "missing-series") second["ruleCode"] = $"MISSING-{Guid.NewGuid()}";
        if (invalid == "foreign-company") second["companyId"] = s.ForeignOwnerId;
        if (invalid == "duplicate-id") second["id"] = first["id"]!.DeepClone();
        if (invalid == "stale-version") await f.PublishSettingAsync(f.SettingBody(otherRule["code"]!.GetValue<string>()));
        var before = await GetAsync(s.Customer, "/api/residue-policy-settings");
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch",
            new JsonObject { ["items"] = new JsonArray(first, second) }, expected);
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Customer, "/api/residue-policy-settings")).GetRawText());
    }

    [Fact]
    public async Task Batch_retains_permission_and_company_boundaries()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var rule = f.RuleBody("NUMERIC_ABSOLUTE_UNCERTAINTY");
        await f.PublishRuleAsync(rule, adopt: false);
        var setting = f.SettingBody(rule["code"]!.GetValue<string>());
        var body = new JsonObject { ["items"] = new JsonArray(setting) };
        foreach (var client in new[] { s.Admin, s.ForeignCustomer, s.Purchaser, s.Operator })
            await RejectedAsync(client, HttpMethod.Post, "/api/residue-policy-settings/batch", body, HttpStatusCode.Forbidden);
        setting["companyId"] = s.ForeignOwnerId;
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-policy-settings/batch", body, HttpStatusCode.Forbidden);
        Assert.Empty((await GetAsync(s.Customer, "/api/residue-policy-settings")).GetProperty("items").EnumerateArray());
    }
}
