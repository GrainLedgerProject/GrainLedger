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

        var history = (await GetAsync(s.Operator, $"/api/ledger/history?lotId={target}")).EnumerateArray().ToArray();
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

    [Fact]
    public async Task Receiving_restriction_event_without_posting_is_unfiltered_only_and_redacted_for_operator()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        var lot = Lots(receipt)[0];
        var arrival = await s.ArrivalAsync(receipt, "9");

        var history = (await GetAsync(s.Operator, $"/api/ledger/history?lotId={lot}")).EnumerateArray().ToArray();
        var restriction = Assert.Single(history, row => row.GetProperty("kind").GetString() == "RECEIVING_RESTRICTION");
        Assert.Equal(lot, restriction.GetProperty("lotId").GetGuid());
        Assert.Equal(JsonValueKind.Null, restriction.GetProperty("postingId").ValueKind);
        Assert.Equal(JsonValueKind.Null, restriction.GetProperty("quantityKg").ValueKind);
        Assert.Equal(JsonValueKind.Null, restriction.GetProperty("locationId").ValueKind);
        var details = restriction.GetProperty("details");
        Assert.Equal(JsonValueKind.Null, details.GetProperty("declaredLossKg").ValueKind);
        Assert.Equal(JsonValueKind.Null, details.GetProperty("varianceKg").ValueKind);
        Assert.False(details.TryGetProperty("evidenceId", out _));
        Assert.False(details.TryGetProperty("discrepancyId", out _));

        var facilityHistory = (await GetAsync(s.Operator, $"/api/ledger/history?lotId={lot}&facilityId={s.FacilityId}")).EnumerateArray().ToArray();
        Assert.DoesNotContain(facilityHistory, row => row.GetProperty("id").GetGuid() == restriction.GetProperty("id").GetGuid());
        Assert.Contains(facilityHistory, row => row.GetProperty("kind").GetString() == "RECEIPT" && row.GetProperty("lotId").GetGuid() == lot);

        var managerRestriction = (await GetAsync(s.Manager, $"/api/ledger/history?lotId={lot}")).EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == restriction.GetProperty("id").GetGuid());
        var managerDetails = managerRestriction.GetProperty("details");
        Assert.Equal(arrival, managerDetails.GetProperty("discrepancyId").GetGuid());
        Assert.True(managerDetails.GetProperty("blocked").GetBoolean());
        Assert.Equal("ARRIVAL_MISMATCH", managerDetails.GetProperty("reason").GetString());
        Assert.Equal(s.EvidenceId, managerDetails.GetProperty("evidenceId").GetGuid());
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
