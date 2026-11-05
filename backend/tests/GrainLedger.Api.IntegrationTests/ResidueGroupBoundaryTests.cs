using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.ResidueScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueGroupBoundaryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Group_stamps_each_receipt_policy_and_rejects_missing_member_adoption_atomically()
    {
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var now = DateTimeOffset.UtcNow;
        var missing = await ReceiptAtAsync(s, now.AddHours(-6));
        var first = await ReceiptAtAsync(s, now.AddHours(-4));
        var second = await ReceiptAtAsync(s, now.AddHours(-2));
        var boundary = now.AddHours(-3);

        var profile = f.RuleBody();
        profile["effectiveTo"] = JsonValue.Create(boundary);
        var firstRule = await f.PublishRuleAsync(profile, adopt: false);
        var code = profile["code"]!.GetValue<string>();
        var setting = f.SettingBody(code);
        setting["effectiveFrom"] = JsonValue.Create(now.AddHours(-5));
        setting["effectiveTo"] = JsonValue.Create(boundary);
        var firstSetting = await f.PublishSettingAsync(setting);

        var revised = (JsonObject)profile.DeepClone();
        revised["id"] = Guid.NewGuid();
        revised["versionNo"] = 2;
        revised["supersedesId"] = firstRule;
        revised["effectiveFrom"] = JsonValue.Create(boundary);
        revised["effectiveTo"] = null;
        var secondRule = await f.PublishRuleAsync(revised, adopt: false);
        var revisedSetting = f.SettingBody(code, version: 2, supersedesId: firstSetting);
        revisedSetting["effectiveFrom"] = JsonValue.Create(boundary);
        var secondSetting = await f.PublishSettingAsync(revisedSetting);

        var firstSource = Source(first);
        var secondSource = Source(second);
        var group = f.GroupBody([firstSource, secondSource], []);
        var published = await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        Assert.Equal(code, published.GetProperty("profileCode").GetString());
        var bindings = published.GetProperty("profileBindings").EnumerateArray().ToArray();
        Assert.Equal(2, bindings.Length);
        AssertBinding(bindings, firstSource, firstRule, firstSetting);
        AssertBinding(bindings, secondSource, secondRule, secondSetting);
        var stored = await GetAsync(s.Customer, $"/api/residue-source-groups/{group["id"]!.GetValue<Guid>()}");
        var storedBindings = stored.GetProperty("profileBindings").EnumerateArray().ToArray();
        Assert.Equal(2, storedBindings.Length);
        AssertBinding(storedBindings, firstSource, firstRule, firstSetting);
        AssertBinding(storedBindings, secondSource, secondRule, secondSetting);

        await AssertEvaluationAsync(f, first, firstSource, firstRule, firstSetting, code);
        await AssertEvaluationAsync(f, second, secondSource, secondRule, secondSetting, code);

        var rejected = f.GroupBody([firstSource, Source(missing)], []);
        var before = await GetAsync(s.Customer, "/api/residue-source-groups");
        await RejectedAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", rejected, HttpStatusCode.BadRequest);
        var after = await GetAsync(s.Customer, "/api/residue-source-groups");
        Assert.Equal(before.GetProperty("items").GetRawText(), after.GetProperty("items").GetRawText());
        using var notFound = await s.Customer.GetAsync($"/api/residue-source-groups/{rejected["id"]!.GetValue<Guid>()}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Unresolved(ResultOf(await f.EvaluateAsync(Lots(missing).Single()), f.SubstanceId));
    }

    private static async Task<JsonElement> ReceiptAtAsync(LedgerScenario s, DateTimeOffset receivedAt)
    {
        var body = JsonSerializer.SerializeToNode(s.ReceiptBody(Guid.NewGuid(), ["10"]))!.AsObject();
        body["payload"]!["receivedAt"] = JsonValue.Create(receivedAt);
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", body);
        await s.ConfirmArrivalAsync(receipt);
        return receipt;
    }

    private static Guid Source(JsonElement receipt) => receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();

    private static void AssertBinding(JsonElement[] bindings, Guid source, Guid rule, Guid setting)
    {
        var binding = Assert.Single(bindings.Where(x => x.GetProperty("sourceId").GetGuid() == source));
        Assert.Equal(rule, binding.GetProperty("profileRuleId").GetGuid());
        Assert.Equal(setting, binding.GetProperty("profileSettingId").GetGuid());
    }

    private static async Task AssertEvaluationAsync(ResidueScenario f, JsonElement receipt, Guid source, Guid rule, Guid setting, string code)
    {
        var result = ResultOf(await f.EvaluateAsync(Lots(receipt).Single()), f.SubstanceId);
        Complete(result, 0.3m, 0.8m);
        Assert.Contains(result.GetProperty("provenance").EnumerateArray(), x =>
            x.GetProperty("sourceId").GetGuid() == source && x.GetProperty("kind").GetString() == "SOURCE_GROUP");
        Assert.Contains(result.GetProperty("rules").EnumerateArray(), x =>
            x.GetProperty("id").GetGuid() == rule && x.GetProperty("policySettingId").GetGuid() == setting &&
            x.GetProperty("ruleCode").GetString() == code);
    }
}
