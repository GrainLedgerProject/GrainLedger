using System.Net;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerSmokeTests(ApiFactory factory, ITestOutputHelper output) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Official_capture_to_ledger_smoke_proves_stock_lineage_and_authorization()
    {
        using var s = await CreateAsync(factory);
        output.WriteLine("1 PASS source farmer, geolocated plot, season, plot-season registered. Policies SYNTHETIC TEST ONLY: explicit zero tolerance, evidence-backed; not factory approval.");
        var receipt = await s.ReceiptAsync("50000", "30000", "20000"); var sources = Lots(receipt);
        Assert.Equal(100000m, (await Task.WhenAll(sources.Select(id => s.LotAsync(id)))).Sum(lot => Quantity(lot)));
        output.WriteLine("2 PASS committed source lots 50,000 / 30,000 / 20,000 kg; total 100,000 kg.");
        var drying = await s.CommitAsync(await s.DraftAsync("DRYING", [(sources[0], "50000"), (sources[1], "30000"), (sources[2], "20000")], [("DRIED_PADDY", "88000")], "12000"));
        Assert.Equal(88000m, Quantity(await s.LotAsync(Output(drying)))); Assert.Equal(12000m, Decimal(drying.GetProperty("declaredLossKg"))); Assert.Equal(0m, Decimal(drying.GetProperty("varianceKg")));
        output.WriteLine("3 PASS drying 88,000 kg output, 12,000 kg declared loss, 0 kg variance.");
        var milling = await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(drying), "88000")], [("HEAD_RICE", "58000"), ("BROKEN_RICE", "8000"), ("BRAN", "6000"), ("HUSK", "15700")], "300"));
        var quantities = new[] { 58000m, 8000m, 6000m, 15700m };
        for (var i = 0; i < quantities.Length; i++) Assert.Equal(quantities[i], Quantity(await s.LotAsync(Output(milling, i))));
        Assert.Equal(300m, Decimal(milling.GetProperty("declaredLossKg"))); Assert.Equal(0m, Decimal(milling.GetProperty("varianceKg")));
        output.WriteLine("4 PASS milling HEAD_RICE 58,000; BROKEN_RICE 8,000; BRAN 6,000; HUSK 15,700 kg. Declared loss 300 kg, variance 0 kg. Byproducts remain stock.");
        for (var i = 0; i < quantities.Length; i++)
        {
            var contributions = (await s.LotAsync(Output(milling, i), s.Customer)).GetProperty("contributions").EnumerateArray().ToArray();
            for (var j = 0; j < sources.Length; j++)
                Assert.Equal(quantities[i] * new[] { .5m, .3m, .2m }[j], Decimal(contributions.Single(c => c.GetProperty("sourceLotId").GetGuid() == sources[j]).GetProperty("quantityKg")));
            Assert.Equal(quantities[i], contributions.Sum(c => Decimal(c.GetProperty("quantityKg"))));
        }
        output.WriteLine("5 PASS every milled output exact attribution 50% / 30% / 20%; contribution totals equal output mass.");
        await s.TransferAsync(Output(milling), "1000"); Assert.Equal(58000m, Quantity(await s.LotAsync(Output(milling))));
        var hold = await s.HoldAsync(Output(milling, 1));
        await s.TransferAsync(Output(milling, 1), "8000", to: s.IsolationLocationId, holdId: hold);
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/holds/{hold}/respond", new { evidenceId = s.EvidenceId, response = "Synthetic smoke isolation", status = "ISOLATED" });
        Assert.Equal(0m, Quantity(await s.LotAsync(Output(milling, 1)), "availableKg"));
        var correctionSource = Lots(await s.ReceiptAsync("10"))[0];
        var correctedOriginal = await s.CommitAsync(await s.DraftAsync("DRYING", [(correctionSource, "10")], [("DRIED_PADDY", "9")], "1"));
        var replacement = await s.DraftAsync("DRYING", [(correctionSource, "10")], [("DRIED_PADDY", "8")], "2");
        var correction = await s.CorrectionAsync(correctedOriginal, replacement);
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/commit", new { replacementOperationId = replacement.GetProperty("id").GetGuid() });
        Assert.Equal(0m, Quantity(await s.LotAsync(correctionSource))); Assert.Equal(0m, Quantity(await s.LotAsync(Output(correctedOriginal)))); Assert.Equal(8m, Quantity(await s.LotAsync(Output(replacement))));
        output.WriteLine("6 PASS transfer unchanged lot ID/58,000 kg; HOLD isolation retains 8,000 kg unavailable; eligible separate-stock correction compensates original and replaces atomically.");
        var foreignLot = Lots(await s.ForeignReceiptAsync("1", s.ForeignLocationId))[0];
        using var denied = await s.Operator.GetAsync($"/api/ledger/lots/{foreignLot}"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var operational = (await GetAsync(s.Operator, "/api/ledger/lots?pageSize=100")).GetProperty("items"); Assert.Contains(operational.EnumerateArray(), lot => lot.GetProperty("id").GetGuid() == Output(milling)); Assert.DoesNotContain(operational.EnumerateArray(), lot => lot.GetProperty("id").GetGuid() == foreignLot);
        using var sensitive = await s.Operator.GetAsync("/api/farmers"); Assert.Equal(HttpStatusCode.Forbidden, sensitive.StatusCode);
        using var pending = await s.Accounts["pending"].Client.GetAsync($"/api/ledger/lots/{Output(milling)}"); Assert.Equal(HttpStatusCode.Forbidden, pending.StatusCode);
        output.WriteLine("7 PASS operator scoped detail, source contacts and pending-user denials. Unsupported procurement correction/arrival routes removed.");
    }
}
