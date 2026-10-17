using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerAcceptanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{

    [Fact]
    public async Task Consumed_lot_HOLD_response_still_requires_manager_scope_and_durable_evidence()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        Assert.Matches(@"^DRY-\d{8}-\d{6,}$", draft.GetProperty("outputs")[0].GetProperty("code").GetString()!);
        await s.CommitAsync(draft);
        Assert.Equal(0m, Quantity(await s.LotAsync(source)));
        var hold = await s.HoldAsync(source);
        var (unassigned, session) = await factory.SeedUserAsync(GrainLedger.Modules.Identity.Roles.MillManager, s.Accounts["manager"].User.OrganizationId);
        using var unassignedClient = factory.CreateRealCookieClient(unassigned, session);
        await CsrfAsync(unassignedClient);
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = unassigned.Id, facilityId = s.ForeignFacilityId });
        await RejectedAsync(unassignedClient, HttpMethod.Post, $"/api/ledger/holds/{hold}/respond", new { evidenceId = s.EvidenceId, response = "Unassigned manager must not respond", status = "ACKNOWLEDGED" }, HttpStatusCode.Forbidden);
        foreach (var body in new object[]
        {
            new { response = "Missing evidence", status = "ACKNOWLEDGED" },
            new { evidenceId = (Guid?)null, response = "Null evidence", status = "ACKNOWLEDGED" },
            new { evidenceId = Guid.NewGuid(), response = "Nonexistent evidence", status = "ACKNOWLEDGED" }
        }) await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/holds/{hold}/respond", body);
        foreach (var actor in new[] { s.Accounts["customer"].User.Id, unassigned.Id })
        {
            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT ledger.command($1,$2,$3::jsonb)", runtime);
            command.Parameters.AddWithValue(actor); command.Parameters.AddWithValue("hold_response");
            command.Parameters.AddWithValue(JsonSerializer.Serialize(new { id = hold, evidenceId = s.EvidenceId, response = "Runtime role or scope bypass", status = "ACKNOWLEDGED" }));
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.CheckViolation });
        }
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/holds/{hold}/respond", new { evidenceId = s.EvidenceId, response = "Synthetic assigned manager validates consumed source history", status = "ACKNOWLEDGED" });
        var state = (await GetAsync(s.Customer, "/api/ledger/holds")).GetProperty("items").EnumerateArray().Single(h => h.GetProperty("id").GetGuid() == hold);
        Assert.Equal("ACKNOWLEDGED", state.GetProperty("status").GetString());
        Assert.Equal(s.EvidenceId, state.GetProperty("evidenceId").GetGuid());
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Runtime_repeatable_read_snapshot_cannot_ignore_later_committed_HOLD()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var draft = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var operationId = draft.GetProperty("id").GetGuid();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
            await runtime.OpenAsync();
            await using var transaction = await runtime.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            await using (var snapshot = new NpgsqlCommand("SELECT count(*) FROM ledger.holds WHERE lot_id=$1", runtime, transaction))
            {
                snapshot.Parameters.AddWithValue(source);
                Assert.Equal(0L, await snapshot.ExecuteScalarAsync());
            }
            // Independent HTTP connection commits HOLD after RR snapshot has been established.
            await s.HoldAsync(source);
            await using var command = new NpgsqlCommand("SELECT ledger.command($1,$2,$3::jsonb)", runtime, transaction);
            command.Parameters.AddWithValue(s.Accounts["operator"].User.Id);
            command.Parameters.AddWithValue("commit");
            command.Parameters.AddWithValue(JsonSerializer.Serialize(new { id = operationId }));
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        });
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.CheckViolation, PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.RaiseException, PostgresErrorCodes.SerializationFailure });
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
        Assert.Equal(0m, Quantity(await s.LotAsync(source), "availableKg"));
        Assert.Equal("DRAFT", (await GetAsync(s.Operator, $"/api/ledger/operations/{operationId}")).GetProperty("status").GetString());
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.events WHERE operation_id=@id", operationId));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE operation_id=@id", operationId));
        Assert.Equal(10m, await ScalarAsync<decimal>("SELECT sum(quantity_kg) FROM ledger.postings WHERE lot_id=@id", source));
        await AssertProjectionAsync();
    }


    [Fact]
    public async Task Concurrent_consumption_cannot_overspend_and_duplicate_commit_is_idempotent()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("100"))[0];
        var a = await s.DraftAsync("DRYING", [(source, "80")], [("DRIED_PADDY", "80")]);
        var b = await s.DraftAsync("DRYING", [(source, "80")], [("DRIED_PADDY", "80")]);
        var responses = await Task.WhenAll(RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{a.GetProperty("id").GetGuid()}/commit", new { }), RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{b.GetProperty("id").GetGuid()}/commit", new { }));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var winning = responses[0].IsSuccessStatusCode ? a : b;
        foreach (var response in responses) response.Dispose();
        await s.CommitAsync(winning); await s.CommitAsync(winning);
        Assert.Equal(20m, Quantity(await s.LotAsync(source)));
        Assert.Equal(80m, Quantity(await s.LotAsync(Output(winning))));
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Procurement_duplicate_commands_and_conflicting_ids_cannot_create_extra_stock()
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid(); var body = s.ReceiptBody(id, ["5", "7"]);
        var replies = await Task.WhenAll(RawAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", body), RawAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", body));
        foreach (var reply in replies) { Assert.True(reply.IsSuccessStatusCode, await reply.Content.ReadAsStringAsync()); reply.Dispose(); }
        var receipt = await GetAsync(s.Purchaser, $"/api/procurements/{id}");
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
        await s.ConfirmArrivalAsync(receipt);
        Assert.Equal(2, Lots(receipt).Distinct().Count());
        Assert.Equal(12m, (await Task.WhenAll(Lots(receipt).Select(l => s.LotAsync(l)))).Sum(l => Quantity(l)));
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(id, ["99"]), HttpStatusCode.Conflict);
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Mid_arrival_posting_failure_rolls_back_measurements_lots_events_and_postings_preserving_source()
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid(); var body = s.ReceiptBody(id, ["5", "7"]);
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", body);
        var measurement = MeasurementBody(receipt);
        await WithPostingFailureAsync(async () =>
        {
            using var result = await RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/receiving/{id}/measurements", measurement);
            Assert.False(result.IsSuccessStatusCode);
            Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM capture.procurements WHERE id=@id", id));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE receipt_id=@id", id));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.events WHERE details->>'receiptId'=CAST(@id AS text)", id));
            Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM capture.procurement_items WHERE procurement_id=@id", id));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.arrival_measurement_sets WHERE procurement_id=@id", id));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.arrival_operations WHERE procurement_id=@id", id));
            Assert.Equal("PENDING_ARRIVAL", await ScalarAsync<string>("SELECT status FROM ledger.receiving WHERE procurement_id=@id", id));
        });
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/receiving/{id}/measurements", measurement);
        Assert.Equal(12m, (await Task.WhenAll(Lots(receipt).Select(l => s.LotAsync(l)))).Sum(l => Quantity(l)));
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Mid_processing_failure_rolls_back_whole_package()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var op = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        await WithPostingFailureAsync(async () =>
        {
            using var response = await RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{op.GetProperty("id").GetGuid()}/commit", new { });
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(10m, Quantity(await s.LotAsync(source)));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE operation_id=@id", op.GetProperty("id").GetGuid()));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.events WHERE operation_id=@id", op.GetProperty("id").GetGuid()));
        });
        await s.CommitAsync(op);
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Missing_policy_blocks_even_exact_balance_without_partial_writes()
    {
        using var s = await CreateAsync(factory, policies: false);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var op = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        using var response = await RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{op.GetProperty("id").GetGuid()}/commit", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("policy", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.events WHERE operation_id=@id", op.GetProperty("id").GetGuid()));
        await s.PolicyAsync("DRYING", "0");
        // Draft predates policy entry, but policy explicitly effective before occurrence.
        await s.CommitAsync(op);
    }

    [Theory]
    [InlineData("99", "1")]
    [InlineData("101", "-1")]
    public async Task Signed_variance_boundary_requires_authenticated_confirmation(string output, string variance)
    {
        using var s = await CreateAsync(factory, policies: false);
        await s.PolicyAsync("DRYING", "1");
        var source = Lots(await s.ReceiptAsync("100"))[0];
        var op = await s.DraftAsync("DRYING", [(source, "100")], [("DRIED_PADDY", output)]);
        var id = op.GetProperty("id").GetGuid();
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version = op.GetProperty("version").GetString(), evidenceId = s.EvidenceId, reason = "Synthetic boundary confirmation", confirmedBy = s.Accounts["manager"].User.Id }, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version = op.GetProperty("version").GetString(), evidenceId = s.EvidenceId, reason = "Admin not business authority" }, HttpStatusCode.Forbidden);
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version = op.GetProperty("version").GetString(), evidenceId = s.EvidenceId, reason = "SYNTHETIC TEST ONLY variance" });
        var committed = await s.CommitAsync(op);
        Assert.Equal(decimal.Parse(variance, CultureInfo.InvariantCulture), Decimal(committed.GetProperty("varianceKg")));
        Assert.Equal(0m, Decimal(committed.GetProperty("declaredLossKg")));
        Assert.Equal(1m, Decimal(committed.GetProperty("allowedVarianceKg")));
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Outside_tolerance_and_stale_confirmation_fail_without_consuming_stock()
    {
        using var s = await CreateAsync(factory, policies: false);
        await s.PolicyAsync("DRYING", "1");
        var source = Lots(await s.ReceiptAsync("100"))[0];
        var op = await s.DraftAsync("DRYING", [(source, "100")], [("DRIED_PADDY", "99")]);
        var id = op.GetProperty("id").GetGuid(); var version = op.GetProperty("version").GetString();
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version, evidenceId = s.EvidenceId, reason = "Synthetic review" });
        await s.UpdateDraftAsync(id, s.OperationBody(id, "DRYING", [(source, "100")], [("DRIED_PADDY", "99.5")]));
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version, evidenceId = s.EvidenceId, reason = "Stale review" });
        await s.UpdateDraftAsync(id, s.OperationBody(id, "DRYING", [(source, "100")], [("DRIED_PADDY", "98.999999")]));
        var edited = await GetAsync(s.Operator, $"/api/ledger/operations/{id}");
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/operations/{id}/confirm", new { version = edited.GetProperty("version").GetString(), evidenceId = s.EvidenceId, reason = "Synthetic outside boundary" });
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        Assert.Equal(100m, Quantity(await s.LotAsync(source)));
    }

    [Fact]
    public async Task Tiny_lineage_largest_remainder_ties_preserve_exact_totals()
    {
        using var s = await CreateAsync(factory);
        var sources = Lots(await s.ReceiptAsync("0.000001", "0.000001", "0.000001"));
        var op = await s.DraftAsync("DRYING", sources.Select(id => (id, "0.000001")).ToArray(), [("DRIED_PADDY", "0.000001")], "0.000002");
        await s.CommitAsync(op);
        var contributions = (await s.LotAsync(Output(op), s.Customer)).GetProperty("contributions").EnumerateArray().ToArray();
        Assert.Equal(3, contributions.Length);
        Assert.Equal(0.000001m, contributions.Sum(c => Decimal(c.GetProperty("quantityKg"))));
        var first = sources.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).First();
        Assert.Equal(first, contributions.Single(c => Decimal(c.GetProperty("quantityKg")) > 0).GetProperty("sourceLotId").GetGuid());
        var second = await s.DraftAsync("MERGE", [(Output(op), "0.000001")], [("DRIED_PADDY", "0.000001")]);
        await s.CommitAsync(second);
        Assert.Equal(0.000001m, (await s.LotAsync(Output(second), s.Customer)).GetProperty("contributions").EnumerateArray().Sum(c => Decimal(c.GetProperty("quantityKg"))));
    }

    [Fact]
    public async Task Transfer_preserves_identity_and_HOLD_survives_response_notification_and_isolation()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0]; var before = await s.LotAsync(source, s.Customer);
        await s.TransferAsync(source, "4");
        var moved = await s.LotAsync(source, s.Customer);
        Assert.Equal(10m, Quantity(moved)); Assert.Equal(source, moved.GetProperty("id").GetGuid());
        Assert.Equal(before.GetProperty("contributions").GetRawText(), moved.GetProperty("contributions").GetRawText());
        var hold = await s.HoldAsync(source);
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/holds/{hold}/respond", new { evidenceId = s.EvidenceId, response = "Synthetic acknowledged", status = "ACKNOWLEDGED" });
        var notifications = await GetAsync(s.Manager, $"/api/ledger/notifications?lotId={source}");
        var notification = notifications.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("holdId").GetGuid() == hold);
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/notifications/{notification.GetProperty("id").GetGuid()}/read", new { });
        await s.TransferAsync(source, "6", to: s.IsolationLocationId, holdId: hold);
        Assert.Equal(0m, Quantity(await s.LotAsync(source), "availableKg"));
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new { id = Guid.NewGuid(), lotId = source, fromLocationId = s.IsolationLocationId, toLocationId = s.LocationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow });
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/holds/{hold}/release", new { }, HttpStatusCode.NotFound);
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task HOLD_race_is_linearizable_and_unrelated_stock_stays_usable()
    {
        using var s = await CreateAsync(factory);
        var sources = Lots(await s.ReceiptAsync("10", "10"));
        var op = await s.DraftAsync("DRYING", [(sources[0], "5")], [("DRIED_PADDY", "5")]);
        var holdId = Guid.NewGuid();
        var responses = await Task.WhenAll(RawAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new { id = holdId, lotId = sources[0], reason = "Synthetic concurrent HOLD" }), RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{op.GetProperty("id").GetGuid()}/commit", new { }));
        Assert.True(responses[0].IsSuccessStatusCode, await responses[0].Content.ReadAsStringAsync());
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        foreach (var response in responses) response.Dispose();
        Assert.Equal(0m, Quantity(await s.LotAsync(sources[0]), "availableKg"));
        var blocked = await s.DraftAsync("DRYING", [(sources[0], "1")], [("DRIED_PADDY", "1")]);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{blocked.GetProperty("id").GetGuid()}/commit", new { });
        await s.CommitAsync(await s.DraftAsync("DRYING", [(sources[1], "10")], [("DRIED_PADDY", "10")]));
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Correction_request_race_blocks_output_use_or_rejects_downstream_correction()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var downstream = await s.DraftAsync("MILLING", [(Output(original), "5")], [("HEAD_RICE", "5")]);
        var correctionId = Guid.NewGuid();
        var replies = await Task.WhenAll(RawAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new { id = correctionId, operationId = original.GetProperty("id").GetGuid(), reason = "Synthetic correction race" }), RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{downstream.GetProperty("id").GetGuid()}/commit", new { }));
        Assert.True(replies[0].IsSuccessStatusCode || replies[1].IsSuccessStatusCode);
        if (replies[1].IsSuccessStatusCode)
        {
            // A request may still record investigation, but no reconciliation/compensation is eligible.
            if (replies[0].IsSuccessStatusCode)
            {
                var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
                await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correctionId}/reconcile", new { evidenceId = s.EvidenceId, replacementOperationId = replacement.GetProperty("id").GetGuid(), replacementVersion = replacement.GetProperty("version").GetString(), reconciliation = new[] { new { lotId = Output(original), locationId = s.LocationId, quantityKg = "5" } }, reason = "Downstream stock verification" });
            }
        }
        else { Assert.True(replies[0].IsSuccessStatusCode); Assert.Equal(0m, Quantity(await s.LotAsync(Output(original)), "availableKg")); }
        foreach (var reply in replies) reply.Dispose();
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Processing_correction_is_atomic_preserves_history_and_prevents_double_reversal()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "9")], "1"));
        var oldHistory = (await GetAsync(s.Operator, $"/api/ledger/history?lotId={source}&pageSize=100")).GetProperty("items");
        var replacement = await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "8")], "2");
        var correctionId = await s.CorrectionAsync(original, replacement);
        var route = $"/api/ledger/corrections/{correctionId}/commit";
        var body = new { replacementOperationId = replacement.GetProperty("id").GetGuid() };
        await WithPostingFailureAsync(async () =>
        {
            using var response = await RawAsync(s.Operator, HttpMethod.Post, route, body);
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(0m, Quantity(await s.LotAsync(source)));
            Assert.Equal(9m, Quantity(await s.LotAsync(Output(original))));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.postings p JOIN ledger.events e ON e.id=p.event_id WHERE e.operation_id=@id AND p.reverses_posting_id IS NOT NULL", original.GetProperty("id").GetGuid()));
        });
        await SendAsync(s.Operator, HttpMethod.Post, route, body);
        Assert.Equal(0m, Quantity(await s.LotAsync(source)));
        Assert.Equal(0m, Quantity(await s.LotAsync(Output(original))));
        Assert.Equal(8m, Quantity(await s.LotAsync(Output(replacement))));
        var reversals = await ScalarAsync<long>("SELECT count(*) FROM ledger.postings WHERE reverses_posting_id IS NOT NULL");
        using var duplicate = await RawAsync(s.Operator, HttpMethod.Post, route, body);
        Assert.Contains(duplicate.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.Equal(reversals, await ScalarAsync<long>("SELECT count(*) FROM ledger.postings WHERE reverses_posting_id IS NOT NULL"));
        var history = (await GetAsync(s.Operator, $"/api/ledger/history?lotId={source}&pageSize=100")).GetProperty("items");
        foreach (var old in oldHistory.EnumerateArray()) Assert.Contains(history.EnumerateArray(), current => current.GetProperty("id").GetGuid() == old.GetProperty("id").GetGuid());
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task Explicit_same_product_correction_reuses_code_with_new_lot_identity()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var originalLotId = Output(original);
        var originalCode = original.GetProperty("outputs")[0].GetProperty("code").GetString();
        var correctionId = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new
        {
            id = correctionId, operationId = original.GetProperty("id").GetGuid(), reason = "Correct source observation"
        });
        var replacementId = Guid.NewGuid();
        var replacementLotId = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", new
        {
            id = replacementId, ownerId = s.OwnerId, facilityId = s.FacilityId, kind = "DRYING",
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), equipmentId = s.EquipmentId,
            inputs = new[] { new { lotId = source, locationId = s.LocationId, quantityKg = "10" } },
            outputs = new[] { new { id = replacementLotId, originalLotId, code = "", commodityId = s.CommodityId,
                stage = "DRIED_PADDY", productIdentity = "DRIED_PADDY", locationId = s.LocationId, quantityKg = "10" } },
            declaredLossKg = "0"
        });
        var replacement = await GetAsync(s.Operator, $"/api/ledger/operations/{replacementId}");
        Assert.Equal(originalCode, replacement.GetProperty("outputs")[0].GetProperty("code").GetString());
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correctionId}/reconcile", new
        {
            evidenceId = s.EvidenceId,
            reconciliation = new[] { new { lotId = originalLotId, locationId = s.LocationId, quantityKg = "10" } },
            replacementOperationId = replacementId,
            replacementVersion = replacement.GetProperty("version").GetString(),
            reason = "Verified physical output"
        });
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/corrections/{correctionId}/commit",
            new { replacementOperationId = replacementId });
        Assert.NotEqual(originalLotId, replacementLotId);
        Assert.Equal(originalCode, (await s.LotAsync(replacementLotId)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Draft_retry_preserves_assigned_output_code_and_rejects_client_code()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        var operationId = Guid.NewGuid();
        var body = s.OperationBody(operationId, "DRYING", [(source, "10")], [("DRIED_PADDY", "10")]);
        var first = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var retried = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var code = first.GetProperty("outputs")[0].GetProperty("code").GetString();
        Assert.Equal(code, retried.GetProperty("outputs")[0].GetProperty("code").GetString());
        Assert.Matches(@"^DRY-\d{8}-\d{6,}$", code!);
        Assert.Equal(1L, await ScalarAsync<long>(
            "SELECT count(*) FROM ledger.lot_code_reservations WHERE context_id=@id", operationId));
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", new
        {
            id = Guid.NewGuid(), ownerId = s.OwnerId, facilityId = s.FacilityId, kind = "DRYING",
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), equipmentId = s.EquipmentId,
            inputs = new[] { new { lotId = source, locationId = s.LocationId, quantityKg = "10" } },
            outputs = new[] { new { id = Guid.NewGuid(), code = "MANUAL", commodityId = s.CommodityId,
                stage = "DRIED_PADDY", productIdentity = "DRIED_PADDY", locationId = s.LocationId,
                quantityKg = "10" } }, declaredLossKg = "0"
        });
    }

    [Fact]
    public async Task Runtime_cannot_bypass_code_issuer_or_call_legacy_command()
    {
        using var s = await CreateAsync(factory);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        foreach (var sql in new[]
        {
            "SELECT nextval('ledger.lot_code_serial')",
            "SELECT ledger.reserve_lot_code_internal($1,$2,'OPERATION',$3,'DRIED_PADDY','DRIED_PADDY')",
            "SELECT ledger.command_legacy($1,'draft','{}'::jsonb)"
        })
        {
            await using var command = new NpgsqlCommand(sql, runtime);
            if (sql.Contains("$1", StringComparison.Ordinal))
                command.Parameters.AddWithValue(s.Accounts["operator"].User.Id);
            if (sql.Contains("$2", StringComparison.Ordinal))
                command.Parameters.AddWithValue(s.OwnerId);
            if (sql.Contains("$3", StringComparison.Ordinal))
                command.Parameters.AddWithValue(Guid.NewGuid());
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
    }

    [Fact]
    public async Task Correction_after_transfer_requires_actual_location_reconciliation()
    {
        using var s = await CreateAsync(factory);
        var raw = Lots(await s.ReceiptAsync("10"))[0];
        var original = await s.CommitAsync(await s.DraftAsync("DRYING", [(raw, "10")], [("DRIED_PADDY", "10")]));
        await s.TransferAsync(Output(original), "10");
        var correction = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new { id = correction, operationId = original.GetProperty("id").GetGuid(), reason = "Transferred output correction" });
        var replacementId = Guid.NewGuid();
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", s.OperationBody(replacementId, "DRYING", [(raw, "10")], [("DRIED_PADDY", "10")], outputLocationId: s.SecondLocationId));
        var replacement = await GetAsync(s.Operator, $"/api/ledger/operations/{replacementId}");
        var replacementVersion = replacement.GetProperty("version").GetString();
        await RejectedAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correction}/reconcile", new { evidenceId = s.EvidenceId, replacementOperationId = replacementId, replacementVersion, reconciliation = new[] { new { lotId = Output(original), locationId = s.LocationId, quantityKg = "10" } }, reason = "Wrong physical location" });
        await SendAsync(s.Manager, HttpMethod.Post, $"/api/ledger/corrections/{correction}/reconcile", new { evidenceId = s.EvidenceId, replacementOperationId = replacementId, replacementVersion, reconciliation = new[] { new { lotId = Output(original), locationId = s.SecondLocationId, quantityKg = "10" } }, reason = "Synthetic verified actual stock" });
        Assert.Equal(10m, Quantity(await s.LotAsync(Output(original))));
        Assert.Equal(0m, Quantity(await s.LotAsync(Output(original)), "availableKg"));
    }


    [Fact]
    public async Task Facility_company_and_sensitive_record_scope_is_enforced_on_lists_search_details_and_mutations()
    {
        using var s = await CreateAsync(factory);
        var source = Lots(await s.ReceiptAsync("10"))[0];
        using var denied = await s.ForeignCustomer.GetAsync($"/api/ledger/lots/{source}"); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain((await GetAsync(s.ForeignCustomer, "/api/ledger/lots")).GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetGuid() == source);
        Assert.Empty((await GetAsync(s.Operator, $"/api/ledger/lots?facilityId={s.ForeignFacilityId}&search=SRC")).GetProperty("items").EnumerateArray());
        using var captureDenied = await s.Operator.GetAsync("/api/farmers"); Assert.Equal(HttpStatusCode.Forbidden, captureDenied.StatusCode);
        using var evidenceDenied = await s.ForeignCustomer.GetAsync($"/api/ledger/evidence/{s.EvidenceId}"); Assert.Equal(HttpStatusCode.Forbidden, evidenceDenied.StatusCode);
        var lot = await s.LotAsync(source);
        Assert.False(lot.TryGetProperty("phoneNumber", out _)); Assert.False(lot.TryGetProperty("farmer", out _)); Assert.False(lot.TryGetProperty("evidence", out _));
        Assert.False(lot.TryGetProperty("contributions", out _));
        var searched = await GetAsync(s.Operator, $"/api/ledger/lots?search={lot.GetProperty("code").GetString()}"); Assert.Contains(searched.GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetGuid() == source);
        var foreignSource = Lots(await s.ForeignReceiptAsync("10"))[0];
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", s.OperationBody(Guid.NewGuid(), "DRYING", [(source, "1"), (foreignSource, "1")], [("DRIED_PADDY", "2")]), HttpStatusCode.Forbidden);
        var hiddenSource = Lots(await s.ForeignReceiptAsync("10", s.ForeignLocationId))[0];
        using var hidden = await s.Operator.GetAsync($"/api/ledger/lots/{hiddenSource}"); Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.DoesNotContain((await GetAsync(s.Operator, "/api/ledger/lots")).GetProperty("items").EnumerateArray(), l => l.GetProperty("id").GetGuid() == hiddenSource);
        Assert.Empty((await GetAsync(s.Operator, "/api/ledger/lots?search=FOREIGN&facilityId=" + s.ForeignFacilityId)).GetProperty("items").EnumerateArray());
        var cross = s.OperationBody(Guid.NewGuid(), "DRYING", [(source, "10")], [("DRIED_PADDY", "10")], ownerId: s.ForeignOwnerId);
        using var created = await RawAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", cross);
        if (created.IsSuccessStatusCode)
        {
            var op = await created.Content.ReadFromJsonAsync<JsonElement>();
            await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{op.GetProperty("id").GetGuid()}/commit", new { }, HttpStatusCode.Forbidden);
        }
        else Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new { id = Guid.NewGuid(), lotId = source, fromLocationId = s.LocationId, toLocationId = s.ForeignLocationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow }, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/holds", new { id = Guid.NewGuid(), lotId = source, reason = "Administrator cannot hold" }, HttpStatusCode.Forbidden);
        Assert.Equal(10m, Quantity(await s.LotAsync(source)));
    }

    [Fact]
    public async Task Runtime_role_cannot_mutate_committed_ledger_or_call_private_posting_helpers()
    {
        using var s = await CreateAsync(factory);
        var lot = Lots(await s.ReceiptAsync("10"))[0];
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE ledger.positions SET quantity_kg=999 WHERE lot_id=@id",
            "DELETE FROM ledger.postings WHERE lot_id=@id",
            "UPDATE ledger.lots SET owner_id=NULL WHERE id=@id",
            "UPDATE ledger.evidence SET content=decode('00','hex')",
            "UPDATE ledger.policies SET tolerance_percent=100",
            "UPDATE ledger.operations SET status='COMMITTED'",
            "SELECT ledger.post(gen_random_uuid(),@id,gen_random_uuid(),1)"
        })
        {
            await using var command = new NpgsqlCommand(sql, runtime); command.Parameters.AddWithValue("id", lot);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }
        Assert.Equal(10m, Quantity(await s.LotAsync(lot)));
    }

    [Theory]
    [InlineData("0.0000001")]
    [InlineData("100000000000000")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Unsupported_precision_overflow_and_nonpositive_receipts_fail(string quantity)
    {
        using var s = await CreateAsync(factory);
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), [quantity]));
    }

    [Fact]
    public async Task Actual_milling_fractions_bagging_identity_and_fail_closed_stage_rules()
    {
        using var s = await CreateAsync(factory);
        var raw = Lots(await s.ReceiptAsync("20"))[0];
        var drying = await s.CommitAsync(await s.DraftAsync("DRYING", [(raw, "20")], [("DRIED_PADDY", "20")]));
        var milling = await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(drying), "20")], [("HEAD_RICE", "12"), ("BRAN", "8")]));
        Assert.Equal(2, milling.GetProperty("outputs").GetArrayLength());
        Assert.Equal(8m, Quantity(await s.LotAsync(Output(milling, 1))));
        var bagged = await s.CommitAsync(await s.DraftAsync("BAGGING", [(Output(milling), "5")], [("BAGGED_RICE", "5")], productIdentity: "HEAD_RICE"));
        var bag = await s.LotAsync(Output(bagged)); Assert.Equal("BAGGED_RICE", bag.GetProperty("stage").GetString()); Assert.Equal("HEAD_RICE", bag.GetProperty("productIdentity").GetString());
        foreach (var (kind, input, stage, identity) in new[]
        {
            ("DRYING", Output(drying), "DRIED_PADDY", "DRIED_PADDY"),
            ("MILLING", Output(milling), "HEAD_RICE", "HEAD_RICE"),
            ("BAGGING", Output(bagged), "BAGGED_RICE", "HEAD_RICE"),
            ("BLENDING", Output(milling), "BLENDED_RICE", "BLENDED_RICE"),
            ("BAGGING", Output(milling), "BAGGED_RICE", "BAGGED_RICE")
        })
        {
            if (stage == "BAGGED_RICE" && identity == "BAGGED_RICE")
            {
                await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations",
                    s.OperationBody(Guid.NewGuid(), kind, [(input, "1")], [(stage, "1")], productIdentity: identity));
                continue;
            }
            var invalid = await s.DraftAsync(kind, [(input, "1")], [(stage, "1")], productIdentity: identity);
            await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{invalid.GetProperty("id").GetGuid()}/commit", new { });
        }
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/dispatch", new { lotId = Output(milling, 1), quantityKg = "8" }, HttpStatusCode.NotFound);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/disposal", new { lotId = Output(milling, 1), quantityKg = "8" }, HttpStatusCode.NotFound);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/adjustments", new { lotId = Output(milling, 1), quantityKg = "-8" }, HttpStatusCode.NotFound);
        await AssertProjectionAsync();
    }

    [Fact]
    public async Task HOLD_and_recipient_notifications_commit_atomically()
    {
        using var s = await CreateAsync(factory);
        var lot = Lots(await s.ReceiptAsync("10"))[0]; var hold = Guid.NewGuid();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using (var command = new NpgsqlCommand("CREATE FUNCTION ledger.test_notification_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Test-induced notification failure'; END $$; CREATE TRIGGER test_notification_failure BEFORE INSERT ON ledger.notifications FOR EACH ROW EXECUTE FUNCTION ledger.test_notification_failure();", owner))
            await command.ExecuteNonQueryAsync();
        try
        {
            using var result = await RawAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new { id = hold, lotId = lot, reason = "Synthetic atomic HOLD" });
            Assert.False(result.IsSuccessStatusCode);
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.holds WHERE id=@id", hold));
            Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.notifications WHERE hold_id=@id", hold));
            Assert.Equal(10m, Quantity(await s.LotAsync(lot), "availableKg"));
        }
        finally
        {
            await using var command = new NpgsqlCommand("DROP TRIGGER test_notification_failure ON ledger.notifications; DROP FUNCTION ledger.test_notification_failure();", owner);
            await command.ExecuteNonQueryAsync();
        }
        await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds", new { id = hold, lotId = lot, reason = "Synthetic atomic HOLD" });
        Assert.True(await ScalarAsync<long>("SELECT count(*) FROM ledger.notifications WHERE hold_id=@id", hold) >= 2);
        Assert.Equal(0m, Quantity(await s.LotAsync(lot), "availableKg"));
    }

    private async Task AssertProjectionAsync()
    {
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM (SELECT lot_id,location_id,sum(quantity_kg) quantity FROM ledger.postings GROUP BY lot_id,location_id) p FULL JOIN ledger.positions s USING(lot_id,location_id) WHERE coalesce(p.quantity,0)<>coalesce(s.quantity_kg,0)"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.positions WHERE quantity_kg<0"));
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid? id = null)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); if (id is not null) command.Parameters.AddWithValue("id", id.Value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task WithPostingFailureAsync(Func<Task> action)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        // Reject 7 kg receipt credit after lot/event insertion; processing debit precedes output rejection.
        const string create = "CREATE FUNCTION ledger.test_posting_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.quantity_kg>6 THEN RAISE EXCEPTION 'Test-induced mid-posting failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER test_posting_failure BEFORE INSERT ON ledger.postings FOR EACH ROW EXECUTE FUNCTION ledger.test_posting_failure();";
        await using (var command = new NpgsqlCommand(create, owner)) await command.ExecuteNonQueryAsync();
        try { await action(); }
        finally { await using var command = new NpgsqlCommand("DROP TRIGGER test_posting_failure ON ledger.postings; DROP FUNCTION ledger.test_posting_failure();", owner); await command.ExecuteNonQueryAsync(); }
    }
}
