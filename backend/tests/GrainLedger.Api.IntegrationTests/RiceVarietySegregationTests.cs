using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class RiceVarietySegregationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Procurement_snapshot_survives_catalog_rename_and_propagates_to_receipt_and_source()
    {
        using var s = await CreateAsync(factory);
        var request = s.ReceiptBody(Guid.NewGuid(), ["10"]);
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", request);
        var line = receipt.GetProperty("items")[0];
        AssertIdentity(line, s.RiceVarietyId, "ST25");

        using var catalog = await s.Admin.GetAsync($"/api/rice-varieties/{s.RiceVarietyId}");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/rice-varieties/{s.RiceVarietyId}")
        {
            Content = JsonContent.Create(new { name = "ST25 renamed" })
        };
        patch.Headers.TryAddWithoutValidation("If-Match", catalog.Headers.ETag!.ToString());
        using var renamed = await s.Admin.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var plotSeason = await GetAsync(s.Purchaser, $"/api/plot-seasons/{s.PlotSeasonId}");
        AssertIdentity(plotSeason, s.RiceVarietyId, "ST25 renamed");
        using var replay = await RawAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(receipt.GetProperty("eventId").GetGuid(), replayed.GetProperty("eventId").GetGuid());
        AssertIdentity(replayed.GetProperty("items")[0], s.RiceVarietyId, "ST25");

        var receiving = await GetAsync(s.Operator, $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}");
        AssertIdentity(receiving.GetProperty("items")[0], s.RiceVarietyId, "ST25");
        await s.ConfirmArrivalAsync(receipt);
        var lot = await s.LotAsync(line.GetProperty("lotId").GetGuid());
        Assert.Equal("SINGLE", lot.GetProperty("varietyStatus").GetString());
        AssertIdentity(lot, s.RiceVarietyId, "ST25");
        Assert.Equal(10m, Quantity(lot));
        var source = await GetAsync(s.Purchaser, $"/api/source-lots/{line.GetProperty("sourceLotId").GetGuid()}");
        AssertIdentity(source, s.RiceVarietyId, "ST25");
        Assert.Equal(HttpStatusCode.NotFound,
            (await s.ForeignCustomer.GetAsync($"/api/source-lots/{line.GetProperty("sourceLotId").GetGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("DRYING", "DRIED_PADDY")]
    [InlineData("MERGE", "RAW_PADDY")]
    public async Task Processing_rejects_different_varieties_without_stock_changes_and_accepts_same_variety(string kind, string outputStage)
    {
        using var s = await CreateAsync(factory);
        var same = Lots(await s.ReceiptAsync("10", "10"));
        var other = await ReceiveOtherVarietyAsync(s);
        var rejected = await s.DraftAsync(kind, [(same[0], "10"), (other, "10")], [(outputStage, "20")]);
        using var response = await RawAsync(s.Operator, HttpMethod.Post,
            $"/api/ledger/operations/{rejected.GetProperty("id").GetGuid()}/commit", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Different rice varieties cannot be mixed", await response.Content.ReadAsStringAsync());
        Assert.Equal(10m, Quantity(await s.LotAsync(same[0])));
        Assert.Equal(10m, Quantity(await s.LotAsync(same[1])));
        Assert.Equal(10m, Quantity(await s.LotAsync(other)));
        var visible = await GetAsync(s.Operator, $"/api/ledger/lots?ids={same[0]},{Output(rejected)}");
        Assert.Equal(same[0], Assert.Single(visible.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        var accepted = await s.DraftAsync(kind, [(same[0], "10"), (same[1], "10")], [(outputStage, "20")]);
        await s.CommitAsync(accepted);
        var result = await s.LotAsync(Output(accepted));
        Assert.Equal(20m, Quantity(result));
        Assert.Equal("SINGLE", result.GetProperty("varietyStatus").GetString());
        AssertIdentity(result, s.RiceVarietyId, "ST25");
        Assert.Equal(0m, Quantity(await s.LotAsync(same[0])));
        Assert.Equal(0m, Quantity(await s.LotAsync(same[1])));
        Assert.Equal(10m, Quantity(await s.LotAsync(other)));
    }

    [Fact]
    public async Task Milling_rejects_cross_variety_and_propagates_same_variety_to_every_output()
    {
        using var s = await CreateAsync(factory);
        var same = Lots(await s.ReceiptAsync("10", "10"));
        var other = await ReceiveOtherVarietyAsync(s);
        var dried = new List<Guid>();
        foreach (var input in same.Append(other))
        {
            var drying = await s.DraftAsync("DRYING", [(input, "10")], [("DRIED_PADDY", "10")]);
            await s.CommitAsync(drying);
            dried.Add(Output(drying));
        }

        (string Stage, string Quantity)[] outputs =
            [("HEAD_RICE", "10"), ("BROKEN_RICE", "4"), ("BRAN", "2"), ("HUSK", "4")];
        var rejected = await s.DraftAsync("MILLING", [(dried[0], "10"), (dried[2], "10")], outputs);
        using var response = await RawAsync(s.Operator, HttpMethod.Post,
            $"/api/ledger/operations/{rejected.GetProperty("id").GetGuid()}/commit", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Different rice varieties cannot be mixed", await response.Content.ReadAsStringAsync());
        foreach (var input in dried) Assert.Equal(10m, Quantity(await s.LotAsync(input)));
        var rejectedIds = rejected.GetProperty("outputs").EnumerateArray().Select(x => x.GetProperty("id").GetGuid());
        var visible = await GetAsync(s.Operator, $"/api/ledger/lots?ids={dried[0]},{string.Join(',', rejectedIds)}");
        Assert.Equal(dried[0], Assert.Single(visible.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        var accepted = await s.DraftAsync("MILLING", [(dried[0], "10"), (dried[1], "10")], outputs);
        await s.CommitAsync(accepted);
        for (var i = 0; i < outputs.Length; i++)
        {
            var lot = await s.LotAsync(Output(accepted, i));
            Assert.Equal(Decimal(JsonSerializer.SerializeToElement(outputs[i].Quantity)), Quantity(lot));
            Assert.Equal("SINGLE", lot.GetProperty("varietyStatus").GetString());
            AssertIdentity(lot, s.RiceVarietyId, "ST25");
        }
        Assert.Equal(0m, Quantity(await s.LotAsync(dried[0])));
        Assert.Equal(0m, Quantity(await s.LotAsync(dried[1])));
        Assert.Equal(10m, Quantity(await s.LotAsync(dried[2])));
    }

    private static void AssertIdentity(JsonElement item, Guid varietyId, string name)
    {
        Assert.Equal(varietyId, item.GetProperty("riceVarietyId").GetGuid());
        Assert.Equal(name, item.GetProperty("riceVariety").GetString());
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
            s.ReceiptBody(Guid.NewGuid(), ["10"], farmerId: farmer, plotSeasonId: plotSeason));
        await s.ConfirmArrivalAsync(receipt);
        var lot = await s.LotAsync(Lots(receipt)[0]);
        Assert.Equal("SINGLE", lot.GetProperty("varietyStatus").GetString());
        AssertIdentity(lot, variety.GetProperty("id").GetGuid(), "OM5451");
        return Lots(receipt)[0];
    }
}
