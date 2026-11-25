using System.Net;
using System.Text.Json;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ByproductVarietyStockTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string PolicyPath = "/api/ledger/byproduct-variety-policy";

    [Fact]
    public async Task Byproduct_merges_apply_owner_and_stage_policy_at_commit_without_changing_blocked_stock()
    {
        using var s = await CreateAsync(factory);
        var same = Lots(await s.ReceiptAsync("30", "30"));
        var other = await ReceiveOtherVarietyAsync(s);
        var products = new List<(Guid Bran, Guid Husk)>();
        foreach (var source in same.Append(other))
        {
            var drying = await s.DraftAsync("DRYING", [(source, "30")], [("DRIED_PADDY", "30")]);
            await s.CommitAsync(drying);
            var milling = await s.DraftAsync("MILLING", [(Output(drying), "30")],
                [("HEAD_RICE", "15"), ("BROKEN_RICE", "6"), ("BRAN", "3"), ("HUSK", "6")]);
            await s.CommitAsync(milling);
            products.Add((Output(milling, 2), Output(milling, 3)));
        }

        var defaults = (await GetAsync(s.Customer, PolicyPath)).GetProperty("items");
        Assert.All(defaults.EnumerateArray(), item =>
        {
            Assert.True(item.GetProperty("allowCrossVariety").GetBoolean());
            Assert.Equal("SYSTEM_DEFAULT", item.GetProperty("source").GetString());
        });
        foreach (var stage in new[] { "BRAN", "HUSK" })
        {
            var left = stage == "BRAN" ? products[0].Bran : products[0].Husk;
            var right = stage == "BRAN" ? products[2].Bran : products[2].Husk;
            var merged = await MergeAsync(s, stage, left, right);
            await AssertMixedLineageAsync(s, merged, same[0], other);
        }

        // Draft before policy change proves the effective policy is read at commit.
        var blockedBran = await DraftMergeAsync(s, "BRAN", products[0].Bran, products[2].Bran);
        var branPolicy = await SendAsync(s.Customer, HttpMethod.Put, PolicyPath + "/BRAN",
            new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Separate bran" });
        var foreign = (await GetAsync(s.ForeignCustomer, PolicyPath)).GetProperty("items");
        Assert.All(foreign.EnumerateArray(), item => Assert.True(item.GetProperty("allowCrossVariety").GetBoolean()));
        await AssertBlockedAsync(s, blockedBran, products[0].Bran, products[2].Bran, "BRAN");

        using var otherCompany = await CreateAsync(factory);
        var otherCompanyFirst = Lots(await otherCompany.ReceiptAsync("30"))[0];
        var otherCompanySecond = await ReceiveOtherVarietyAsync(otherCompany);
        var foreignBran = new List<Guid>();
        foreach (var source in new[] { otherCompanyFirst, otherCompanySecond })
        {
            var drying = await otherCompany.DraftAsync("DRYING", [(source, "30")], [("DRIED_PADDY", "30")]);
            await otherCompany.CommitAsync(drying);
            var milling = await otherCompany.DraftAsync("MILLING", [(Output(drying), "30")],
                [("HEAD_RICE", "15"), ("BROKEN_RICE", "6"), ("BRAN", "3"), ("HUSK", "6")]);
            await otherCompany.CommitAsync(milling);
            foreignBran.Add(Output(milling, 2));
        }
        var foreignMerge = await MergeAsync(otherCompany, "BRAN", foreignBran[0], foreignBran[1]);
        await AssertMixedLineageAsync(otherCompany, foreignMerge, otherCompanyFirst, otherCompanySecond);

        var sameVariety = await MergeAsync(s, "BRAN", products[0].Bran, products[1].Bran);
        var sameLot = await s.LotAsync(Output(sameVariety));
        Assert.Equal("SINGLE", sameLot.GetProperty("varietyStatus").GetString());
        Assert.Equal(s.RiceVarietyId, sameLot.GetProperty("riceVarietyId").GetGuid());
        Assert.Equal(2m, Quantity(sameLot));
        var husk = await MergeAsync(s, "HUSK", products[0].Husk, products[2].Husk);
        await AssertMixedLineageAsync(s, husk, same[0], other);

        await SendAsync(s.Customer, HttpMethod.Put, PolicyPath + "/HUSK",
            new { allowCrossVariety = false, expectedRevision = (Guid?)null, reason = "Separate husk" });
        await SendAsync(s.Customer, HttpMethod.Put, PolicyPath + "/BRAN",
            new { allowCrossVariety = (bool?)null, expectedRevision = branPolicy.GetProperty("revision").GetGuid(), reason = "Reset bran" });
        var blockedHusk = await DraftMergeAsync(s, "HUSK", products[0].Husk, products[2].Husk);
        await AssertBlockedAsync(s, blockedHusk, products[0].Husk, products[2].Husk, "HUSK");
        var resetMerge = await MergeAsync(s, "BRAN", products[0].Bran, products[2].Bran);
        await AssertMixedLineageAsync(s, resetMerge, same[0], other);
    }

    private static Task<JsonElement> DraftMergeAsync(LedgerScenario s, string stage, Guid first, Guid second) =>
        s.DraftAsync("MERGE", [(first, "1"), (second, "1")], [(stage, "2")]);

    private static async Task<JsonElement> MergeAsync(LedgerScenario s, string stage, Guid first, Guid second)
    {
        var draft = await DraftMergeAsync(s, stage, first, second);
        await s.CommitAsync(draft);
        return draft;
    }

    private static async Task AssertBlockedAsync(LedgerScenario s, JsonElement draft, Guid first, Guid second, string stage)
    {
        var firstBefore = Quantity(await s.LotAsync(first));
        var secondBefore = Quantity(await s.LotAsync(second));
        using var response = await RawAsync(s.Operator, HttpMethod.Post,
            $"/api/ledger/operations/{draft.GetProperty("id").GetGuid()}/commit", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains($"Company policy forbids cross-variety mixing for {stage}", await response.Content.ReadAsStringAsync());
        Assert.Equal(firstBefore, Quantity(await s.LotAsync(first)));
        Assert.Equal(secondBefore, Quantity(await s.LotAsync(second)));
        var visible = await GetAsync(s.Operator, $"/api/ledger/lots?ids={first},{Output(draft)}");
        Assert.Equal(first, Assert.Single(visible.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    private static async Task AssertMixedLineageAsync(LedgerScenario s, JsonElement merge, Guid firstSource, Guid secondSource)
    {
        var id = Output(merge);
        var lot = await s.LotAsync(id);
        Assert.Equal("MIXED", lot.GetProperty("varietyStatus").GetString());
        Assert.Equal(JsonValueKind.Null, lot.GetProperty("riceVarietyId").ValueKind);
        Assert.Equal(JsonValueKind.Null, lot.GetProperty("riceVariety").ValueKind);
        Assert.Equal(2m, Quantity(lot));
        var trace = await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/trace", new { lotId = id, direction = "backward", view = "current" });
        var result = trace.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        var contributions = result.GetProperty("contributions").EnumerateArray().ToArray();
        Assert.Equal(new[] { firstSource, secondSource }.Order(), contributions.Select(x => x.GetProperty("sourceLotId").GetGuid()).Order());
        Assert.All(contributions, item => Assert.Equal(1m, Decimal(item.GetProperty("quantityKg"))));
    }

    private static async Task<Guid> ReceiveOtherVarietyAsync(LedgerScenario s)
    {
        var variety = await SendAsync(s.Admin, HttpMethod.Post, "/api/rice-varieties",
            new { code = $"OTHER-{Guid.NewGuid():N}", name = "OM5451" });
        var farmer = Guid.NewGuid();
        var plot = Guid.NewGuid();
        var plotSeason = Guid.NewGuid();
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/farmers",
            CaptureCommand(new { entityId = farmer, name = "Other variety farmer", contact = "+84000000000" }, 2));
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/plots",
            CaptureCommand(new { entityId = plot, farmerId = farmer, areaHa = 1m,
                geometry = new { type = "Point", coordinates = new[] { 106.123456m, 10.123456m } } }, 2));
        var season = await SendAsync(s.Admin, HttpMethod.Post, "/api/crop-seasons",
            new { companyId = s.OwnerId, name = "Other variety season", startsOn = "2026-01-01", endsOn = "2026-12-31" });
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/plot-seasons",
            CaptureCommand(new { entityId = plotSeason, plotId = plot, seasonId = season.GetProperty("id").GetGuid(),
                riceVarietyId = variety.GetProperty("id").GetGuid() }));
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["30"], farmerId: farmer, plotSeasonId: plotSeason));
        await s.ConfirmArrivalAsync(receipt);
        return Lots(receipt)[0];
    }
}
