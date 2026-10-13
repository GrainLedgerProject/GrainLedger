using System.Security.Claims;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerSafetyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(false, Roles.CustomerManager)]
    [InlineData(true, null)]
    public async Task Ledger_reads_reject_persisted_inactive_or_roleless_actors(bool active, string? role)
    {
        var (user, _) = await factory.SeedUserAsync(role, active: active);
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"))
        };
        try
        {
            var error = await Assert.ThrowsAsync<LedgerException>(() =>
                scope.ServiceProvider.GetRequiredService<LedgerService>().ReadAsync("lookups", new JsonObject()));
            Assert.Equal(403, error.StatusCode);
        }
        finally { accessor.HttpContext = previous; }
    }

    [Theory]
    [InlineData("1", "t", "1000")]
    [InlineData("0.001", "g", "0.000001")]
    [InlineData("00000000000000001", "kg", "1")]
    [InlineData("000000000000000001", "kg", "1")]
    public async Task Measurements_convert_exactly_and_preserve_original_units(string quantity, string unit, string expected)
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid();
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/receipts", s.ReceiptBody(id, [quantity], unit: unit));
        var receipt = await s.CommitReceiptAsync(id); var line = receipt.GetProperty("lines")[0];
        Assert.Equal(quantity, line.GetProperty("quantity").GetString()); Assert.Equal(unit, line.GetProperty("unit").GetString());
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Decimal(line.GetProperty("quantityKg")));
        Assert.Equal(Decimal(line.GetProperty("quantityKg")), Quantity(await s.LotAsync(Lots(receipt)[0])));
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/receipts", s.ReceiptBody(Guid.NewGuid(), ["0.000001"], unit: "g"), HttpStatusCode.BadRequest);
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/receipts", s.ReceiptBody(Guid.NewGuid(), ["1"], unit: "lb"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unsupported_original_measurement_length_rejects_before_persisting_receipt_draft()
    {
        using var s = await CreateAsync(factory); var id = Guid.NewGuid();
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/receipts", s.ReceiptBody(id, [new string('0', 40) + "1"]), HttpStatusCode.BadRequest);
        Assert.DoesNotContain((await GetAsync(s.Purchaser, "/api/capture-mock/receipts")).EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        var acceptedId = Guid.NewGuid(); var acceptedOriginal = new string('0', 39) + "1";
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/receipts", s.ReceiptBody(acceptedId, [acceptedOriginal]));
        var accepted = await s.CommitReceiptAsync(acceptedId);
        Assert.Equal(acceptedOriginal, accepted.GetProperty("lines")[0].GetProperty("quantity").GetString());
        Assert.Equal(1m, Quantity(await s.LotAsync(Lots(accepted)[0])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NUL_in_source_name_or_phone_returns_client_error_without_persisted_record(bool phone)
    {
        using var s = await CreateAsync(factory); var id = Guid.NewGuid();
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/farmers", new
        {
            id, ownerOrganizationId = s.OwnerId,
            name = phone ? "Valid synthetic farmer" : "Invalid synthetic farmer\0",
            phoneNumber = phone ? "+840000\0" : null
        }, HttpStatusCode.BadRequest);
        Assert.DoesNotContain((await GetAsync(s.Purchaser, "/api/capture-mock/farmers")).EnumerateArray(), f => f.GetProperty("id").GetGuid() == id);
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture-mock/farmers", new { id, ownerOrganizationId = s.OwnerId, name = "Valid synthetic farmer", phoneNumber = "+84000000000" });
    }

    [Fact]
    public async Task Transfer_rejects_same_UUID_location_with_different_text_case_without_blocking_receipt_correction()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10"); var source = Lots(receipt)[0];
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = source,
            fromLocationId = s.LocationId.ToString("D").ToUpperInvariant(),
            toLocationId = s.LocationId.ToString("D").ToLowerInvariant(),
            quantityKg = "10", occurredAt = DateTimeOffset.UtcNow
        }, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        var lot = await s.LotAsync(source);
        Assert.Equal(10m, Quantity(lot)); Assert.Single(lot.GetProperty("positions").EnumerateArray());
        Assert.DoesNotContain((await GetAsync(s.Operator, $"/api/ledger/history?lotId={source}")).EnumerateArray(), e => e.GetProperty("kind").GetString() == "TRANSFER");
        var (correction, replacementId) = await ReceiptCorrectionTests.RequestAsync(s, receipt, "9");
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture-mock/receipt-corrections/{correction}/commit", new { commandId = Guid.NewGuid() });
        var replacement = await GetAsync(s.Purchaser, $"/api/capture-mock/receipts/{replacementId}");
        Assert.Equal(0m, Quantity(await s.LotAsync(source)));
        Assert.Equal(9m, Quantity(await s.LotAsync(Lots(replacement)[0])));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manager_confirmation_requires_explicit_nonnull_review_version(bool omitted)
    {
        using var s = await CreateAsync(factory, policies: false);
        await s.PolicyAsync("DRYING", "1");
        var source = Lots(await s.ReceiptAsync("100"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "100")], [("DRIED_PADDY", "99")]);
        var id = draft.GetProperty("id").GetGuid();
        var review = new JsonObject { ["evidenceId"] = s.EvidenceId.ToString(), ["reason"] = "Synthetic incomplete review must fail" };
        if (!omitted) review["version"] = null;
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", review);
        var unchanged = await GetAsync(s.Operator, $"/api/ledger/operations/{id}");
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("confirmedVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("confirmedBy").ValueKind);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        Assert.Equal(100m, Quantity(await s.LotAsync(source)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reconciliation_requires_explicit_nonnull_replacement_review_version(bool omitted)
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1");
        var correction = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new { id = correction, operationId = original.GetProperty("id").GetGuid(), reason = "Synthetic review-version regression" });
        var review = JsonSerializer.SerializeToNode(new
        {
            evidenceId = s.EvidenceId, replacementOperationId = replacement.GetProperty("id").GetGuid(),
            reconciliation = new[] { new { lotId = Output(original), locationId = s.LocationId, quantityKg = "10" } },
            reason = "Synthetic incomplete reconciliation must fail"
        })!.AsObject();
        if (!omitted) review["replacementVersion"] = null;
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correction}/reconcile", review);
        var state = (await GetAsync(s.Operator, "/api/ledger/corrections")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == correction);
        Assert.Equal("REQUESTED", state.GetProperty("status").GetString());
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/commit", new { replacementOperationId = replacement.GetProperty("id").GetGuid() });
        Assert.Equal(0m, Quantity(await s.LotAsync(source)));
        Assert.Equal(10m, Quantity(await s.LotAsync(Output(original))));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task Missing_null_or_empty_outputs_cannot_commit_disguised_loss_only_operation(string shape)
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0]; var id = Guid.NewGuid();
        var body = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(source, "10")], [("DRIED_PADDY", "1")], "10"))!.AsObject();
        if (shape == "missing") body.Remove("outputs");
        else body["outputs"] = shape == "null" ? null : new JsonArray();
        using var creation = await RawAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        if (creation.IsSuccessStatusCode)
            await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        else Assert.Contains(creation.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
        Assert.Equal(10m, Quantity(await s.LotAsync(source), "availableKg"));
    }

    [Fact]
    public async Task Merge_of_bagged_head_and_broken_rice_cannot_erase_product_identity()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("20"))[0];
        var dry = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "20")], [("DRIED_PADDY", "20")]));
        var mill = await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(dry), "20")], [("HEAD_RICE", "10"), ("BROKEN_RICE", "10")]));
        var head = await s.CommitAsync(await s.DraftAsync("BAGGING", [(Output(mill), "5")], [("BAGGED_RICE", "5")], productIdentity: "HEAD_RICE"));
        var broken = await s.CommitAsync(await s.DraftAsync("BAGGING", [(Output(mill, 1), "5")], [("BAGGED_RICE", "5")], productIdentity: "BROKEN_RICE"));
        var mixed = await s.DraftAsync("MERGE", [(Output(head), "5"), (Output(broken), "5")], [("BAGGED_RICE", "10")], productIdentity: "HEAD_RICE");
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{mixed.GetProperty("id").GetGuid()}/commit", new { });
        Assert.Equal(5m, Quantity(await s.LotAsync(Output(head))));
        Assert.Equal(5m, Quantity(await s.LotAsync(Output(broken))));
        Assert.Equal("HEAD_RICE", (await s.LotAsync(Output(head))).GetProperty("productIdentity").GetString());
        Assert.Equal("BROKEN_RICE", (await s.LotAsync(Output(broken))).GetProperty("productIdentity").GetString());
        var otherHead = await s.CommitAsync(await s.DraftAsync("BAGGING", [(Output(mill), "5")], [("BAGGED_RICE", "5")], productIdentity: "HEAD_RICE"));
        var valid = await s.CommitAsync(await s.DraftAsync("MERGE", [(Output(head), "1"), (Output(otherHead), "1")], [("BAGGED_RICE", "2")], productIdentity: "HEAD_RICE"));
        var merged = await s.LotAsync(Output(valid));
        Assert.Equal(2m, Quantity(merged)); Assert.Equal("BAGGED_RICE", merged.GetProperty("stage").GetString());
        Assert.Equal("HEAD_RICE", merged.GetProperty("productIdentity").GetString());
    }

    [Fact]
    public async Task Correction_review_is_bound_to_replacement_and_cancellation_requires_durable_evidence()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1");
        var correction = await s.CorrectionAsync(original, replacement);
        var replacementId = replacement.GetProperty("id").GetGuid();
        await SendAsync(s.Operator, HttpMethod.Put, $"/api/ledger/operations/{replacementId}", s.OperationBody(replacementId, "DRYING", [(source, "10")], [("DRIED_PADDY", "8")], "2"));
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/commit", new { replacementOperationId = replacementId });
        Assert.Equal(0m, Quantity(await s.LotAsync(source))); Assert.Equal(10m, Quantity(await s.LotAsync(Output(original))));
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/cancel", new { evidenceId = s.EvidenceId, reason = "Operator has no reconciliation authority" }, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correction}/cancel", new { evidenceId = Guid.NewGuid(), reason = "Missing original-correct verification" });
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correction}/cancel", new { evidenceId = s.EvidenceId, reason = "Synthetic immutable evidence verifies original record correct" });
        Assert.Equal(10m, Quantity(await s.LotAsync(Output(original)), "availableKg"));
    }

    [Fact]
    public async Task New_HOLD_after_reconciliation_blocks_correction_compensation_and_cancellation_does_not_release_it()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1");
        var correction = await s.CorrectionAsync(original, replacement);
        await s.HoldAsync(Output(original));
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correction}/commit", new { replacementOperationId = replacement.GetProperty("id").GetGuid() });
        Assert.Equal(0m, Quantity(await s.LotAsync(source))); Assert.Equal(10m, Quantity(await s.LotAsync(Output(original))));
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correction}/cancel", new { evidenceId = s.EvidenceId, reason = "Synthetic evidence verifies original correct" });
        Assert.Equal(0m, Quantity(await s.LotAsync(Output(original)), "availableKg"));
    }

    [Fact]
    public async Task Receiving_resolution_does_not_clear_independent_correction_lock()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10"); var lot = Lots(receipt)[0];
        var correction = Guid.NewGuid();
        await SendAsync(s.Purchaser, HttpMethod.Post, $"/api/capture-mock/receipts/{receipt.GetProperty("id").GetGuid()}/corrections", new { id = correction, reason = "Synthetic independent procurement investigation", replacement = s.ReceiptBody(Guid.NewGuid(), ["10"]) });
        var arrival = await s.ArrivalAsync(receipt, "9");
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture-mock/arrivals/{arrival}/corrections", new { id = Guid.NewGuid(), quantity = "10", unit = "kg", evidenceId = s.EvidenceId, reason = "Synthetic arrival entry evidence" });
        Assert.Equal("RESOLVED", (await GetAsync(s.Manager, $"/api/capture-mock/arrivals/{arrival}")).GetProperty("status").GetString());
        var pending = (await GetAsync(s.Purchaser, "/api/capture-mock/receipt-corrections")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == correction);
        Assert.Equal("REQUESTED", pending.GetProperty("status").GetString());
        Assert.Equal(10m, Quantity(await s.LotAsync(lot))); Assert.Equal(0m, Quantity(await s.LotAsync(lot), "availableKg"));
    }

    [Fact]
    public async Task Blending_requires_exact_explicit_products_and_merge_never_changes_stage()
    {
        using var s = await CreateAsync(factory);
        var raw = Lots(await s.ReceiptAsync("20"))[0];
        var dry = await s.CommitAsync(await s.DraftAsync("DRYING", [(raw, "20")], [("DRIED_PADDY", "20")]));
        var mill = await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(dry), "20")], [("HEAD_RICE", "12"), ("BROKEN_RICE", "8")]));
        var inputs = new[] { (Output(mill), "4"), (Output(mill, 1), "4") };
        var blend = await s.DraftAsync("BLENDING", inputs, [("BLENDED_RICE", "8")]);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{blend.GetProperty("id").GetGuid()}/commit", new { });
        Assert.Equal(12m, Quantity(await s.LotAsync(Output(mill)))); Assert.Equal(8m, Quantity(await s.LotAsync(Output(mill, 1))));
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new { id = Guid.NewGuid(), facilityId = s.FacilityId, inputProducts = new[] { $"{s.CommodityId}:HEAD_RICE", $"{s.CommodityId}:BROKEN_RICE" }, outputCommodityId = s.CommodityId, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = s.EvidenceId });
        var blended = await s.CommitAsync(blend); Assert.Equal(8m, Quantity(await s.LotAsync(Output(blended))));
        var notApproved = await s.DraftAsync("BLENDING", [(Output(blended), "1"), (Output(mill), "1")], [("BLENDED_RICE", "2")]);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{notApproved.GetProperty("id").GetGuid()}/commit", new { });
        var wrongMerge = await s.DraftAsync("MERGE", [(Output(mill), "1"), (Output(mill, 1), "1")], [("HEAD_RICE", "2")]);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{wrongMerge.GetProperty("id").GetGuid()}/commit", new { });
        var disguisedReprocess = await s.DraftAsync("MERGE", [(Output(mill), "1")], [("DRIED_PADDY", "1")]);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{disguisedReprocess.GetProperty("id").GetGuid()}/commit", new { });
        var invalidFraction = await s.DraftAsync("MILLING", [(Output(dry), "1")], [("HEAD_RICE", "1"), ("HUSK", "0")]);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{invalidFraction.GetProperty("id").GetGuid()}/commit", new { });
    }

    [Fact]
    public async Task Compartment_owner_switch_requires_empty_stock_and_recorded_clearance()
    {
        using var s = await CreateAsync(factory);
        var compartment = Guid.NewGuid();
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new { id = compartment, facilityId = s.FacilityId, name = "Synthetic compartment", kind = "COMPARTMENT" });
        var source = Lots(await s.ReceiptAsync("10"))[0];
        await s.TransferAsync(source, "10", to: compartment);
        var foreignLot = Lots(await s.ForeignReceiptAsync("1"))[0];
        var transfer = new { id = Guid.NewGuid(), lotId = foreignLot, fromLocationId = s.LocationId, toLocationId = compartment, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow };
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", transfer);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{compartment}/clearance", new { evidenceId = s.EvidenceId, reason = "Not empty" });
        await s.TransferAsync(source, "10", from: compartment, to: s.LocationId);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", transfer);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{compartment}/clearance", new { evidenceId = Guid.NewGuid(), reason = "No clearance evidence" });
        await SendAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{compartment}/clearance", new { evidenceId = s.EvidenceId, reason = "Synthetic empty physical compartment verified" });
        var setup = await GetAsync(s.Admin, "/api/ledger/setup");
        var cleared = setup.GetProperty("locations").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == compartment);
        Assert.True(cleared.GetProperty("cleared").GetBoolean()); Assert.Equal(JsonValueKind.Null, cleared.GetProperty("ownerId").ValueKind);
        await s.TransferAsync(foreignLot, "1", to: compartment);
        setup = await GetAsync(s.Admin, "/api/ledger/setup");
        var occupied = setup.GetProperty("locations").EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == compartment);
        Assert.Equal(s.ForeignOwnerId, occupied.GetProperty("ownerId").GetGuid()); Assert.False(occupied.GetProperty("cleared").GetBoolean());
    }
}
