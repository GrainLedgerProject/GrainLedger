using System.Net;
using System.Text.Json.Nodes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Matrix_derives_versions_preserves_unchanged_and_rejects_stale_without_partial_write()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var first = f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY");
        first["relativeUncertaintyPercent"] = "10";
        var second = f.RuleBody("ND", "LOD");
        await f.PublishRuleAsync(first, adopt: false);
        await f.PublishRuleAsync(second, adopt: false);
        var before = await GetAsync(s.Admin, "/api/residue-rules");
        var changed = (JsonObject)first.DeepClone();
        changed["relativeUncertaintyPercent"] = "12.500";
        changed["id"] = Guid.Empty;
        changed["versionNo"] = -999;
        changed["supersedesId"] = Guid.NewGuid();
        var body = Publication(first, Change(first, changed), Change(second, (JsonObject)second.DeepClone()));
        var published = await SendAsync(s.Admin, HttpMethod.Post, "/api/residue-matrix/publish", body);
        var item = Assert.Single(published.GetProperty("rules").EnumerateArray());
        Assert.Equal(2, item.GetProperty("versionNo").GetInt32());
        Assert.Equal(first["id"]!.GetValue<Guid>(), item.GetProperty("supersedesId").GetGuid());
        Assert.NotEqual(Guid.Empty, item.GetProperty("id").GetGuid());
        Assert.Equal(s.Accounts["admin"].User.Id, item.GetProperty("publishedBy").GetGuid());
        Assert.Equal("12.500", item.GetProperty("relativeUncertaintyPercent").GetString());
        Assert.Empty(published.GetProperty("settings").EnumerateArray());
        var after = await GetAsync(s.Admin, "/api/residue-rules");
        foreach (var original in before.GetProperty("items").EnumerateArray())
            Assert.Contains(after.GetProperty("items").EnumerateArray(), value => value.GetRawText() == original.GetRawText());
        Assert.Equal(before.GetProperty("items").GetArrayLength() + 1, after.GetProperty("items").GetArrayLength());
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-matrix/publish", body, HttpStatusCode.Conflict);
        Assert.Equal(after.GetRawText(), (await GetAsync(s.Admin, "/api/residue-rules")).GetRawText());
    }

    [Fact]
    public async Task Matrix_rolls_back_rule_when_later_protected_adoption_command_fails()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var system = f.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY");
        system["relativeUncertaintyPercent"] = "10";
        await f.PublishRuleAsync(system, adopt: false);
        var own = (JsonObject)system.DeepClone();
        own["companyId"] = s.OwnerId;
        own["relativeUncertaintyPercent"] = "20";
        var body = Publication(system, Change(system, own, clone: true));
        body["companyId"] = s.OwnerId;
        body["settings"] = new JsonArray(new JsonObject
        {
            ["ruleCode"] = $"MISSING-{Guid.NewGuid()}", ["baseId"] = null, ["mode"] = "COMPANY_OVERRIDE",
            ["evidenceRef"] = "Reviewed synthetic matrix study", ["applicabilityStatement"] = "Synthetic scope only"
        });
        var rulesBefore = await GetAsync(s.Customer, "/api/residue-rules");
        var settingsBefore = await GetAsync(s.Customer, "/api/residue-policy-settings");
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-matrix/publish", body, HttpStatusCode.BadRequest);
        Assert.Equal(rulesBefore.GetRawText(), (await GetAsync(s.Customer, "/api/residue-rules")).GetRawText());
        Assert.Equal(settingsBefore.GetRawText(), (await GetAsync(s.Customer, "/api/residue-policy-settings")).GetRawText());
        ((JsonObject)((JsonArray)body["settings"]!)[0]!)["ruleCode"] = system["code"]!.DeepClone();
        var result = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-matrix/publish", body);
        Assert.Single(result.GetProperty("rules").EnumerateArray());
        var setting = Assert.Single(result.GetProperty("settings").EnumerateArray());
        Assert.Equal("COMPANY_OVERRIDE", setting.GetProperty("mode").GetString());
        Assert.Equal(s.OwnerId, setting.GetProperty("companyId").GetGuid());
        Assert.Equal(1, setting.GetProperty("versionNo").GetInt32());
        Assert.Equal(s.Accounts["customer"].User.Id, setting.GetProperty("publishedBy").GetGuid());
        var unchangedRule = JsonNode.Parse(result.GetProperty("rules")[0].GetRawText())!.AsObject();
        var adoptionOnly = Publication(system, Change(unchangedRule, unchangedRule));
        adoptionOnly["companyId"] = s.OwnerId;
        adoptionOnly["settings"] = new JsonArray(new JsonObject
        {
            ["ruleCode"] = system["code"]!.DeepClone(), ["baseId"] = setting.GetProperty("id").GetGuid(), ["mode"] = "COMPANY_OVERRIDE",
            ["evidenceRef"] = "Reviewed adoption without scientific changes", ["applicabilityStatement"] = "Synthetic scope only"
        });
        var historyBeforeAdoption = await GetAsync(s.Customer, "/api/residue-rules");
        var adoptionResult = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-matrix/publish", adoptionOnly);
        Assert.Empty(adoptionResult.GetProperty("rules").EnumerateArray());
        Assert.Equal(2, Assert.Single(adoptionResult.GetProperty("settings").EnumerateArray()).GetProperty("versionNo").GetInt32());
        Assert.Equal(historyBeforeAdoption.GetRawText(), (await GetAsync(s.Customer, "/api/residue-rules")).GetRawText());
    }

    [Fact]
    public async Task Matrix_denies_global_foreign_and_nonmanager_publication()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var system = f.RuleBody("ND", "LOD");
        await f.PublishRuleAsync(system, adopt: false);
        var revised = (JsonObject)system.DeepClone(); revised["ndThresholdKind"] = "LOQ";
        var global = Publication(system, Change(system, revised));
        foreach (var client in new[] { s.Customer, s.ForeignCustomer, s.Purchaser, s.Operator })
            await RejectedAsync(client, HttpMethod.Post, "/api/residue-matrix/publish", global, HttpStatusCode.Forbidden);
        revised["companyId"] = s.OwnerId;
        var own = Publication(system, Change(system, revised, clone: true)); own["companyId"] = s.OwnerId;
        foreach (var client in new[] { s.Admin, s.ForeignCustomer, s.Purchaser, s.Operator })
            await RejectedAsync(client, HttpMethod.Post, "/api/residue-matrix/publish", own, HttpStatusCode.Forbidden);
        own["companyId"] = s.ForeignOwnerId;
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-matrix/publish", own, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Matrix_rejects_invalid_later_rule_and_stale_system_clone_source()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var first = f.RuleBody("ND", "LOD"); var second = f.RuleBody("ND", "LOD");
        await f.PublishRuleAsync(first, adopt: false); await f.PublishRuleAsync(second, adopt: false);
        var valid = (JsonObject)first.DeepClone(); valid["ndThresholdKind"] = "LOQ";
        var invalid = (JsonObject)second.DeepClone(); invalid["ndThresholdKind"] = null;
        var before = await GetAsync(s.Admin, "/api/residue-rules");
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/residue-matrix/publish",
            Publication(first, Change(first, valid), Change(second, invalid)), HttpStatusCode.BadRequest);
        Assert.Equal(before.GetRawText(), (await GetAsync(s.Admin, "/api/residue-rules")).GetRawText());
        var clone = (JsonObject)first.DeepClone(); clone["companyId"] = s.OwnerId;
        var stale = Publication(first, Change(first, clone, clone: true)); stale["companyId"] = s.OwnerId;
        await SendAsync(s.Admin, HttpMethod.Post, "/api/residue-matrix/publish", Publication(first, Change(first, valid)));
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-matrix/publish", stale, HttpStatusCode.Conflict);
    }

    private static JsonObject Change(JsonObject source, JsonObject rule, bool clone = false) => new()
    {
        ["baseId"] = clone ? null : source["id"]!.DeepClone(), ["sourceId"] = source["id"]!.DeepClone(), ["rule"] = rule.DeepClone()
    };

    private static JsonObject Publication(JsonObject source, params JsonObject[] changes) => new()
    {
        ["companyId"] = null, ["rules"] = new JsonArray(changes.Select(change => (JsonNode)change).ToArray()), ["settings"] = new JsonArray(),
        ["effectiveFrom"] = source["effectiveFrom"]!.DeepClone(), ["effectiveTo"] = null,
        ["reviewedBy"] = "Synthetic matrix reviewer", ["reviewAuthority"] = "Synthetic test QA", ["reviewedAt"] = source["reviewedAt"]!.DeepClone()
    };
}
