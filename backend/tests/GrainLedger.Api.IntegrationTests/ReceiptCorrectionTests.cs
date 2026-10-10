using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ReceiptCorrectionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal static async Task<(Guid CorrectionId, Guid ReplacementId)> RequestAsync(LedgerScenario s, JsonElement receipt, string quantity)
    {
        var correction = Guid.NewGuid(); var replacement = Guid.NewGuid();
        await SendAsync(s.Purchaser, HttpMethod.Post, $"/api/capture/receipts/{receipt.GetProperty("id").GetGuid()}/corrections", new { id = correction, reason = "SYNTHETIC evidence-backed procurement-entry correction", replacement = s.ReceiptBody(replacement, [quantity]) });
        var detail = await GetAsync(s.Purchaser, $"/api/capture/receipt-corrections/{correction}");
        var lines = detail.GetProperty("replacement").GetProperty("lines").EnumerateArray().Select(line => new { receiptLineId = line.GetProperty("id").GetGuid(), quantityKg = line.GetProperty("quantityKg").GetString() }).ToArray();
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture/receipt-corrections/{correction}/reconcile", new { commandId = Guid.NewGuid(), evidenceId = s.EvidenceId, reason = "Synthetic physical replacement quantity verified", verifiedLines = lines });
        return (correction, replacement);
    }

    [Fact]
    public async Task Orphaned_evidence_retry_rechecks_current_facility_authority_without_freezing_historical_role()
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid(); var encoded = Convert.ToBase64String("SYNTHETIC current authority replay"u8.ToArray());
        const string fileName = "current-authority.txt";
        var stored = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/ledger/evidence", new { id, ownerId = s.OwnerId, facilityId = s.FacilityId, fileName, contentType = "text/plain", contentBase64 = encoded });
        var actor = s.Accounts["purchaser"].User;
        using var accessUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{actor.Id}/access")
        {
            Content = JsonContent.Create(new { role = GrainLedger.Modules.Identity.Roles.MillManager, organizationId = s.Accounts["manager"].User.OrganizationId, isActive = true })
        };
        accessUpdate.Headers.TryAddWithoutValidation("If-Match", actor.Version.ToString("N"));
        using var changed = await s.Admin.SendAsync(accessUpdate);
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = actor.Id, facilityId = s.ForeignFacilityId });
        using var currentClient = factory.CreateRealCookieClient(actor, await factory.SeedSessionAsync(actor));
        await CsrfAsync(currentClient);
        var retry = new { id, ownerOrganizationId = s.OwnerId, locationId = s.LocationId, fileName, mediaType = "text/plain", contentBase64 = encoded };
        // Same actor now lacks facility A authority: old ownership must not authorize replay.
        await RejectedAsync(currentClient, HttpMethod.Post, "/api/capture/evidence", retry, HttpStatusCode.Forbidden);
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = actor.Id, facilityId = s.FacilityId });
        // Current authority restored: role/org transition itself must not forbid exact same-actor replay.
        var replayed = await SendAsync(currentClient, HttpMethod.Post, "/api/capture/evidence", retry);
        Assert.Equal(id, replayed.GetProperty("id").GetGuid());
        Assert.Equal(stored.GetProperty("sha256").GetString(), replayed.GetProperty("sha256").GetString());
        Assert.Equal(Convert.FromBase64String(encoded), await s.Customer.GetByteArrayAsync($"/api/ledger/evidence/{id}"));
    }

    [Fact]
    public async Task Capture_evidence_retry_recovers_preexisting_exact_content_without_allowing_actor_or_content_substitution()
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid(); var bytes = "SYNTHETIC durable evidence replay"u8.ToArray();
        var encoded = Convert.ToBase64String(bytes);
        const string fileName = "capture-evidence-replay.txt";
        var stored = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/ledger/evidence", new
        {
            id, ownerId = s.OwnerId, facilityId = s.FacilityId, fileName,
            contentType = "text/plain", contentBase64 = encoded
        });
        var retry = new { id, ownerOrganizationId = s.OwnerId, locationId = s.LocationId, fileName, mediaType = "text/plain", contentBase64 = encoded };
        await using (var runtime = new NpgsqlConnection(factory.RuntimeConnectionString))
        {
            await runtime.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM capture.commands WHERE kind='evidence' AND id=$1", runtime);
            command.Parameters.AddWithValue(id);
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        // Exercise orphaned durable evidence before any successful Capture command memo exists.
        foreach (var (field, value) in new[]
        {
            ("contentBase64", Convert.ToBase64String("changed bytes"u8.ToArray())),
            ("fileName", "changed-name.txt"),
            ("ownerOrganizationId", s.ForeignOwnerId.ToString()),
            ("locationId", s.ForeignLocationId.ToString())
        })
        {
            var changed = JsonSerializer.SerializeToNode(retry)!.AsObject(); changed[field] = value;
            await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/capture/evidence", changed, HttpStatusCode.Forbidden, HttpStatusCode.Conflict);
        }
        var (otherActor, otherSession) = await factory.SeedUserAsync(GrainLedger.Modules.Identity.Roles.PurchasingStaff, s.OwnerId);
        using var otherClient = factory.CreateRealCookieClient(otherActor, otherSession); await CsrfAsync(otherClient);
        await RejectedAsync(otherClient, HttpMethod.Post, "/api/capture/evidence", retry, HttpStatusCode.Forbidden, HttpStatusCode.Conflict);
        var recovered = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture/evidence", retry);
        Assert.Equal(id, recovered.GetProperty("id").GetGuid());
        Assert.Equal(stored.GetProperty("sha256").GetString(), recovered.GetProperty("sha256").GetString());
        var repeated = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture/evidence", retry);
        Assert.Equal(recovered.GetRawText(), repeated.GetRawText());
        Assert.Equal(bytes, await s.Purchaser.GetByteArrayAsync($"/api/capture/evidence/{id}"));
        Assert.Equal(bytes, await s.Customer.GetByteArrayAsync($"/api/ledger/evidence/{id}"));
        await RejectedAsync(otherClient, HttpMethod.Post, "/api/capture/evidence", retry, HttpStatusCode.Forbidden, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Standalone_runtime_arrival_cannot_commit_without_matching_ledger_receiving_block()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10"); var lot = Lots(receipt)[0]; var arrival = Guid.NewGuid();
        var body = new { id = arrival, receiptLineId = Lines(receipt)[0], quantity = "9", unit = "kg", evidenceId = s.EvidenceId, observedAt = DateTimeOffset.UtcNow };
        await RuntimeMutationRejectedAsync(
            "INSERT INTO capture.arrivals(id,receipt_line_id,body,quantity_kg,actor_id) VALUES(@id,@line,@body::jsonb,9,@actor)",
            ("id", arrival), ("line", Lines(receipt)[0]), ("body", JsonSerializer.Serialize(body)), ("actor", s.Accounts["operator"].User.Id));
        Assert.DoesNotContain((await GetAsync(s.Purchaser, "/api/capture/arrivals")).EnumerateArray(), a => a.GetProperty("id").GetGuid() == arrival);
        Assert.Equal(10m, Quantity(await s.LotAsync(lot))); Assert.Equal(10m, Quantity(await s.LotAsync(lot), "availableKg"));
        var recorded = await s.ArrivalAsync(receipt, "9");
        Assert.Equal("BLOCKED", (await GetAsync(s.Purchaser, $"/api/capture/arrivals/{recorded}")).GetProperty("status").GetString());
        Assert.Equal(10m, Quantity(await s.LotAsync(lot))); Assert.Equal(0m, Quantity(await s.LotAsync(lot), "availableKg"));
    }

    [Fact]
    public async Task Standalone_runtime_receipt_correction_cannot_commit_without_ledger_eligibility_block()
    {
        using var s = await CreateAsync(factory);
        var original = await s.ReceiptAsync("10"); var source = Lots(original)[0];
        var replacementId = Guid.NewGuid(); var correction = Guid.NewGuid();
        var replacementBody = s.ReceiptBody(replacementId, ["9"]);
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture/receipts", replacementBody);
        await RuntimeMutationRejectedAsync(
            "UPDATE capture.receipts SET replacement_for=@original WHERE id=@replacement; INSERT INTO capture.receipt_corrections(id,receipt_id,replacement_receipt_id,body,created_by) VALUES(@id,@original,@replacement,@body::jsonb,@actor)",
            ("id", correction), ("original", original.GetProperty("id").GetGuid()), ("replacement", replacementId),
            ("body", JsonSerializer.Serialize(new { id = correction, reason = "Synthetic standalone correction regression", replacement = replacementBody })), ("actor", s.Accounts["purchaser"].User.Id));
        Assert.DoesNotContain((await GetAsync(s.Purchaser, "/api/capture/receipt-corrections")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == correction);
        Assert.Equal(JsonValueKind.Null, (await GetAsync(s.Purchaser, $"/api/capture/receipts/{replacementId}")).GetProperty("replacementForReceiptId").ValueKind);
        Assert.Equal(10m, Quantity(await s.LotAsync(source))); Assert.Equal(10m, Quantity(await s.LotAsync(source), "availableKg"));
        var (validCorrection, validReplacement) = await RequestAsync(s, original, "9");
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture/receipt-corrections/{validCorrection}/commit", new { commandId = Guid.NewGuid() });
        var corrected = await GetAsync(s.Purchaser, $"/api/capture/receipts/{validReplacement}");
        Assert.Equal(0m, Quantity(await s.LotAsync(source))); Assert.Equal(9m, Quantity(await s.LotAsync(Lots(corrected)[0])));
    }

    [Theory]
    [InlineData("quantity", false)]
    [InlineData("commodity", false)]
    [InlineData("plotSeason", false)]
    [InlineData("quantity", true)]
    [InlineData("commodity", true)]
    [InlineData("plotSeason", true)]
    public async Task Runtime_normalized_receipt_lines_cannot_disagree_with_immutable_receipt_document(string field, bool correction)
    {
        using var s = await CreateAsync(factory);
        Guid receiptId; Guid? correctionId = null;
        if (correction)
        {
            var original = await s.ReceiptAsync("10");
            var prepared = await RequestAsync(s, original, "9");
            receiptId = prepared.ReplacementId; correctionId = prepared.CorrectionId;
        }
        else
        {
            receiptId = Guid.NewGuid();
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture/receipts", s.ReceiptBody(receiptId, ["10"]));
        }
        var draft = await GetAsync(s.Purchaser, $"/api/capture/receipts/{receiptId}"); var line = Lines(draft)[0];
        var alternate = Guid.NewGuid();
        if (field == "commodity")
            await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/commodities", new { id = alternate, name = $"Synthetic alternate commodity {alternate:N}" });
        if (field == "plotSeason")
        {
            var plot = Guid.NewGuid();
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture/plots", new { id = plot, farmerId = s.FarmerId, name = "Synthetic alternate plot", latitude = "10", longitude = "106" });
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/capture/plot-seasons", new { id = alternate, plotId = plot, seasonId = s.SeasonId });
        }
        var sql = field switch
        {
            "quantity" => "UPDATE capture.receipt_lines SET quantity_kg=11,body=jsonb_set(body,'{quantity}','\"11\"'::jsonb) WHERE id=@line",
            "commodity" => "UPDATE capture.receipt_lines SET commodity_id=@alternate,body=jsonb_set(body,'{commodityId}',to_jsonb(CAST(@alternate AS text))) WHERE id=@line",
            _ => "UPDATE capture.receipt_lines SET plot_season_id=@alternate,body=jsonb_set(body,'{plotSeasonId}',to_jsonb(CAST(@alternate AS text))) WHERE id=@line"
        };
        await RuntimeMutationRejectedAsync(sql, ("line", line), ("alternate", alternate));
        if (correctionId is not null)
            await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture/receipt-corrections/{correctionId}/commit", new { commandId = Guid.NewGuid() });
        else await s.CommitReceiptAsync(receiptId);
        var committed = await GetAsync(s.Purchaser, $"/api/capture/receipts/{receiptId}"); var source = Lots(committed)[0];
        Assert.Equal(correction ? 9m : 10m, Quantity(await s.LotAsync(source)));
        Assert.Equal(s.CommodityId, committed.GetProperty("lines")[0].GetProperty("commodityId").GetGuid());
        Assert.Equal(s.PlotSeasonId, committed.GetProperty("lines")[0].GetProperty("plotSeasonId").GetGuid());
        Assert.Equal(s.CommodityId, (await s.LotAsync(source)).GetProperty("commodityId").GetGuid());
    }

    private async Task RuntimeMutationRejectedAsync(string sql, params (string Name, object Value)[] parameters)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
            await using var transaction = await runtime.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(sql, runtime, transaction);
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task Quantity_only_receipt_correction_reuses_code_only_within_original_correction_chain()
    {
        using var s = await CreateAsync(factory);
        var original = await s.ReceiptAsync("10"); var source = Lots(original)[0];
        var code = original.GetProperty("lines")[0].GetProperty("lotCode").GetString();
        var correction = Guid.NewGuid(); var replacementId = Guid.NewGuid();
        var replacementBody = JsonSerializer.SerializeToNode(s.ReceiptBody(replacementId, ["9"]))!.AsObject();
        replacementBody["lines"]![0]!["lotCode"] = code;
        await SendAsync(s.Purchaser, HttpMethod.Post, $"/api/capture/receipts/{original.GetProperty("id").GetGuid()}/corrections", new { id = correction, reason = "Synthetic quantity-only correction retains original physical lot code", replacement = replacementBody });
        var detail = await GetAsync(s.Purchaser, $"/api/capture/receipt-corrections/{correction}");
        var verifiedLines = detail.GetProperty("replacement").GetProperty("lines").EnumerateArray().Select(line => new { receiptLineId = line.GetProperty("id").GetGuid(), quantityKg = line.GetProperty("quantityKg").GetString() }).ToArray();
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture/receipt-corrections/{correction}/reconcile", new { commandId = Guid.NewGuid(), evidenceId = s.EvidenceId, reason = "Synthetic corrected physical stock verified", verifiedLines });
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture/receipt-corrections/{correction}/commit", new { commandId = Guid.NewGuid() });
        var replacement = await GetAsync(s.Purchaser, $"/api/capture/receipts/{replacementId}");
        var correctedSource = Lots(replacement)[0];
        Assert.NotEqual(source, correctedSource);
        Assert.Equal(code, replacement.GetProperty("lines")[0].GetProperty("lotCode").GetString());
        Assert.Equal(code, (await s.LotAsync(source)).GetProperty("code").GetString());
        Assert.Equal(0m, Quantity(await s.LotAsync(source)));
        Assert.Equal(9m, Quantity(await s.LotAsync(correctedSource)));
        Assert.Equal(original.GetProperty("lines").GetRawText(), (await GetAsync(s.Purchaser, $"/api/capture/receipts/{original.GetProperty("id").GetGuid()}")).GetProperty("lines").GetRawText());
        Assert.NotEmpty((await GetAsync(s.Operator, $"/api/ledger/history?lotId={source}")).EnumerateArray());
        Assert.NotEmpty((await GetAsync(s.Operator, $"/api/ledger/history?lotId={correctedSource}")).EnumerateArray());
        var duplicateId = Guid.NewGuid();
        var duplicate = JsonSerializer.SerializeToNode(s.ReceiptBody(duplicateId, ["99"]))!.AsObject();
        duplicate["lines"]![0]!["lotCode"] = code;
        using var created = await RawAsync(s.Purchaser, HttpMethod.Post, "/api/capture/receipts", duplicate);
        if (created.IsSuccessStatusCode)
        {
            var draft = await GetAsync(s.Purchaser, $"/api/capture/receipts/{duplicateId}");
            await RejectedAsync(s.Purchaser, HttpMethod.Post, $"/api/capture/receipts/{duplicateId}/commit", new { commandId = Guid.NewGuid(), version = draft.GetProperty("version").GetString() }, HttpStatusCode.Conflict);
            Assert.Equal("DRAFT", (await GetAsync(s.Purchaser, $"/api/capture/receipts/{duplicateId}")).GetProperty("status").GetString());
        }
        else Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);
        Assert.Equal(9m, Quantity(await s.LotAsync(correctedSource)));
    }

    [Fact]
    public async Task Unused_receipt_correction_is_atomic_preserves_provenance_and_cannot_reverse_twice()
    {
        using var s = await CreateAsync(factory);
        var original = await s.ReceiptAsync("10"); var source = Lots(original)[0];
        var (correction, replacementId) = await RequestAsync(s, original, "9");
        Assert.Equal(10m, Quantity(await s.LotAsync(source))); Assert.Equal(0m, Quantity(await s.LotAsync(source), "availableKg"));
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using (var trigger = new NpgsqlCommand("CREATE FUNCTION ledger.test_receipt_replacement_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.quantity_kg>0 THEN RAISE EXCEPTION 'Test-induced receipt replacement failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER test_receipt_replacement_failure BEFORE INSERT ON ledger.postings FOR EACH ROW EXECUTE FUNCTION ledger.test_receipt_replacement_failure();", owner)) await trigger.ExecuteNonQueryAsync();
        var route = $"/api/capture/receipt-corrections/{correction}/commit"; var commandId = Guid.NewGuid();
        try
        {
            using var failed = await RawAsync(s.Manager, HttpMethod.Post, route, new { commandId });
            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
            Assert.Contains("Test-induced receipt replacement failure", await failed.Content.ReadAsStringAsync());
            Assert.Equal(10m, Quantity(await s.LotAsync(source)));
            Assert.Equal("DRAFT", (await GetAsync(s.Purchaser, $"/api/capture/receipts/{replacementId}")).GetProperty("status").GetString());
            await using var reversals = new NpgsqlCommand("SELECT count(*) FROM ledger.receipt_reversals WHERE receipt_id=@id", owner); reversals.Parameters.AddWithValue("id", original.GetProperty("id").GetGuid()); Assert.Equal(0L, await reversals.ExecuteScalarAsync());
        }
        finally { await using var drop = new NpgsqlCommand("DROP TRIGGER test_receipt_replacement_failure ON ledger.postings; DROP FUNCTION ledger.test_receipt_replacement_failure();", owner); await drop.ExecuteNonQueryAsync(); }
        await SendAsync(s.Manager, HttpMethod.Post, route, new { commandId });
        var replacement = await GetAsync(s.Purchaser, $"/api/capture/receipts/{replacementId}");
        Assert.Equal(0m, Quantity(await s.LotAsync(source))); Assert.Equal(9m, Quantity(await s.LotAsync(Lots(replacement)[0])));
        Assert.Equal(original.GetProperty("id").GetGuid(), replacement.GetProperty("replacementForReceiptId").GetGuid());
        var retained = await GetAsync(s.Purchaser, $"/api/capture/receipts/{original.GetProperty("id").GetGuid()}");
        Assert.Equal(original.GetProperty("lines").GetRawText(), retained.GetProperty("lines").GetRawText());
        Assert.Equal(original.GetProperty("farmerId").GetGuid(), retained.GetProperty("farmerId").GetGuid());
        Assert.Equal(original.GetProperty("seasonId").GetGuid(), retained.GetProperty("seasonId").GetGuid());
        using var duplicate = await RawAsync(s.Manager, HttpMethod.Post, route, new { commandId }); Assert.True(duplicate.IsSuccessStatusCode);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM ledger.receipt_reversals WHERE receipt_id=@id", owner); count.Parameters.AddWithValue("id", original.GetProperty("id").GetGuid()); Assert.Equal(1L, await count.ExecuteScalarAsync());
        await using var unique = new NpgsqlCommand("SELECT count(*) FROM (SELECT reverses_posting_id FROM ledger.postings WHERE reverses_posting_id IS NOT NULL GROUP BY reverses_posting_id HAVING count(*)>1) p", owner); Assert.Equal(0L, await unique.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_correction_rejects_any_prior_transfer_or_processing(bool processing)
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10"); var lot = Lots(receipt)[0];
        if (processing) await s.CommitAsync(await s.DraftAsync("DRYING", [(lot, "1")], [("DRIED_PADDY", "1")]));
        else { await s.TransferAsync(lot, "1"); await s.TransferAsync(lot, "1", from: s.SecondLocationId, to: s.LocationId); }
        await RejectedAsync(s.Purchaser, HttpMethod.Post, $"/api/capture/receipts/{receipt.GetProperty("id").GetGuid()}/corrections", new { id = Guid.NewGuid(), reason = "Unsupported downstream correction", replacement = s.ReceiptBody(Guid.NewGuid(), ["9"]) });
        Assert.Equal(processing ? 9m : 10m, Quantity(await s.LotAsync(lot)));
    }

    [Fact]
    public async Task Wrong_owner_replacement_and_cross_owner_receipt_creation_are_rejected()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10");
        await RejectedAsync(s.Purchaser, HttpMethod.Post, $"/api/capture/receipts/{receipt.GetProperty("id").GetGuid()}/corrections", new { id = Guid.NewGuid(), reason = "Wrong-owner must not transfer ownership", replacement = s.ReceiptBody(Guid.NewGuid(), ["10"], ownerId: s.ForeignOwnerId) });
        await RejectedAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, "/api/capture/receipts", s.ReceiptBody(Guid.NewGuid(), ["10"], ownerId: s.ForeignOwnerId));
        await RejectedAsync(s.Accounts["foreignPurchaser"].Client, HttpMethod.Post, $"/api/capture/receipts/{receipt.GetProperty("id").GetGuid()}/corrections", new { id = Guid.NewGuid(), reason = "Outside customer", replacement = s.ReceiptBody(Guid.NewGuid(), ["10"]) });
        Assert.Equal(10m, Quantity(await s.LotAsync(Lots(receipt)[0])));
    }

    [Fact]
    public async Task Receipt_correction_request_race_and_commit_recheck_prevent_downstream_use()
    {
        using var s = await CreateAsync(factory);
        var receipt = await s.ReceiptAsync("10"); var lot = Lots(receipt)[0];
        var correction = Guid.NewGuid(); var replacementId = Guid.NewGuid();
        var op = await s.DraftAsync("DRYING", [(lot, "1")], [("DRIED_PADDY", "1")]);
        var replies = await Task.WhenAll(RawAsync(s.Purchaser, HttpMethod.Post, $"/api/capture/receipts/{receipt.GetProperty("id").GetGuid()}/corrections", new { id = correction, reason = "Synthetic correction race", replacement = s.ReceiptBody(replacementId, ["9"]) }), RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{op.GetProperty("id").GetGuid()}/commit", new { }));
        Assert.Single(replies, reply => reply.IsSuccessStatusCode);
        Assert.Single(replies, reply => reply.StatusCode is HttpStatusCode.Conflict);
        if (replies[0].IsSuccessStatusCode)
        {
            Assert.Equal(0m, Quantity(await s.LotAsync(lot), "availableKg"));
            await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new { id = Guid.NewGuid(), lotId = lot, fromLocationId = s.LocationId, toLocationId = s.SecondLocationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow });
        }
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task Receipt_replacement_retains_independent_HOLD_and_original_history()
    {
        using var s = await CreateAsync(factory);
        var original = await s.ReceiptAsync("10");
        var hold = await s.HoldAsync(Lots(original)[0]);
        var (correction, replacementId) = await RequestAsync(s, original, "9");
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/capture/receipt-corrections/{correction}/commit", new { commandId = Guid.NewGuid() });
        var replacement = await GetAsync(s.Purchaser, $"/api/capture/receipts/{replacementId}");
        var lot = await s.LotAsync(Lots(replacement)[0]);
        Assert.Equal(9m, Quantity(lot)); Assert.Equal(0m, Quantity(lot, "availableKg"));
        var holds = await GetAsync(s.Customer, "/api/ledger/holds");
        Assert.Contains(holds.EnumerateArray(), h => h.GetProperty("id").GetGuid() == hold);
        Assert.Contains(holds.EnumerateArray(), h => h.GetProperty("lotId").GetGuid() == Lots(replacement)[0]);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new { id = Guid.NewGuid(), lotId = Lots(replacement)[0], fromLocationId = s.LocationId, toLocationId = s.SecondLocationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task Immutable_evidence_keeps_bytes_and_rejects_changed_same_id()
    {
        using var s = await CreateAsync(factory);
        var before = await s.Customer.GetByteArrayAsync($"/api/ledger/evidence/{s.EvidenceId}");
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/evidence", new { id = s.EvidenceId, ownerId = s.OwnerId, facilityId = s.FacilityId, fileName = "changed.txt", contentType = "text/plain", contentBase64 = Convert.ToBase64String("changed"u8.ToArray()) }, HttpStatusCode.Conflict);
        Assert.Equal(before, await s.Customer.GetByteArrayAsync($"/api/ledger/evidence/{s.EvidenceId}"));
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/evidence/{s.EvidenceId}", new { contentBase64 = "Y2hhbmdlZA==" }, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }
}
