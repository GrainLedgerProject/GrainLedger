using System.Globalization;
using System.Net;
using System.Text.Json;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerReadScalingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Lot_history_keeps_origin_siblings_and_only_target_postings_from_later_events()
    {
        using var s = await CreateAsync(factory);
        var received = await s.ReceiptAsync("10", "6");
        var source = Lots(received);
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source[0], "4"), (source[1], "2")], [("DRIED_PADDY", "3"), ("DRIED_PADDY", "3")], "0"));
        var target = Output(original);
        var siblingOutput = Output(original, 1);
        var later = await s.CommitAsync(await s.DraftAsync("MILLING", [(target, "2"), (siblingOutput, "1")], [("HEAD_RICE", "2"), ("BROKEN_RICE", "1")], "0"));
        await s.TransferAsync(target, "1");

        var history = (await GetAsync(s.Operator, $"/api/ledger/history?lotId={target}&pageSize=100")).GetProperty("items").EnumerateArray().ToArray();
        AssertStableOrder(history);

        var originalPostings = history.Where(row => row.GetProperty("operationId").ValueKind == JsonValueKind.String && row.GetProperty("operationId").GetGuid() == original.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(new[] { source[0], source[1], target, siblingOutput }.OrderBy(IdKey), originalPostings.Select(row => row.GetProperty("lotId").GetGuid()).Distinct().OrderBy(IdKey));
        Assert.Equal(new[] { (source[0], -4m), (source[1], -2m), (target, 3m), (siblingOutput, 3m) }.OrderBy(x => IdKey(x.Item1)),
            originalPostings.Select(row => (row.GetProperty("lotId").GetGuid(), Kg(row))).OrderBy(x => IdKey(x.Item1)));

        var laterRows = history.Where(row => row.GetProperty("operationId").ValueKind == JsonValueKind.String && row.GetProperty("operationId").GetGuid() == later.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(new[] { (target, -2m) }, laterRows.Select(row => (row.GetProperty("lotId").GetGuid(), Kg(row))));
        Assert.DoesNotContain(laterRows, row => row.GetProperty("lotId").GetGuid() == siblingOutput || row.GetProperty("lotId").GetGuid() == Output(later) || row.GetProperty("lotId").GetGuid() == Output(later, 1));

        var transfers = history.Where(row => row.GetProperty("kind").GetString() == "TRANSFER").ToArray();
        Assert.Equal(new[] { (s.LocationId, -1m), (s.SecondLocationId, 1m) }.OrderBy(x => IdKey(x.Item1)),
            transfers.Select(row => (row.GetProperty("locationId").GetGuid(), Kg(row))).OrderBy(x => IdKey(x.Item1)));
        Assert.All(transfers, row => Assert.Equal(target, row.GetProperty("lotId").GetGuid()));
        Assert.DoesNotContain(history, row => row.GetProperty("details").TryGetProperty("evidenceId", out _));

        using (var otherTenant = await s.ForeignCustomer.GetAsync($"/api/ledger/history?lotId={target}"))
            Assert.Equal(HttpStatusCode.Forbidden, otherTenant.StatusCode);

        var foreignReceipt = await s.ForeignReceiptAsync("5", s.ForeignLocationId);
        var foreignLot = Lots(foreignReceipt)[0];
        using (var unassigned = await s.Operator.GetAsync($"/api/ledger/history?lotId={foreignLot}"))
            Assert.Equal(HttpStatusCode.Forbidden, unassigned.StatusCode);
    }


    private static decimal Kg(JsonElement row) => decimal.Parse(row.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture);
    private static string IdKey(Guid id) => id.ToString("N");

    private static void AssertStableOrder(JsonElement[] rows)
    {
        var expected = rows.OrderBy(row => row.GetProperty("occurredAt").GetDateTimeOffset())
            .ThenBy(row => IdKey(row.GetProperty("id").GetGuid()), StringComparer.Ordinal)
            .ThenBy(row => row.GetProperty("postingId").ValueKind == JsonValueKind.String ? IdKey(row.GetProperty("postingId").GetGuid()) : "", StringComparer.Ordinal)
            .Select(row => (row.GetProperty("id").GetGuid(), row.GetProperty("postingId").ValueKind == JsonValueKind.String ? row.GetProperty("postingId").GetGuid() : (Guid?)null));
        var actual = rows.Select(row => (row.GetProperty("id").GetGuid(), row.GetProperty("postingId").ValueKind == JsonValueKind.String ? row.GetProperty("postingId").GetGuid() : (Guid?)null));
        Assert.Equal(expected, actual);
    }
}
