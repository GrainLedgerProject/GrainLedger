using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MixedBulkTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record MixedSite(LedgerScenario Scenario, Guid LocationId, Guid PolicyId, Guid CommodityId);
    private static object Review(JsonElement state, string action, Guid evidence) => new
    {
        operationId = Guid.NewGuid(),
        expectedReceivingVersion = state.GetProperty("receivingVersion").GetInt32(),
        measurementSetId = state.GetProperty("latestMeasurementSetId").GetGuid(),
        action,
        reason = "Physical evidence reviewed; immutable investigation record",
        evidenceId = evidence
    };

    private async Task<MixedSite> CreateMixedSiteAsync(string kind = "STORE")
    {
        var s = await CreateAsync(factory);
        var (locationId, policyId, commodityId) = await AddMixedLocationAsync(s, kind);
        return new MixedSite(s, locationId, policyId, commodityId);
    }

    private async Task<(Guid LocationId, Guid PolicyId, Guid CommodityId)> AddMixedLocationAsync(LedgerScenario s, string kind = "STORE")
    {
        var locationId = Guid.NewGuid();
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/locations", new
        {
            id = locationId, facilityId = s.FacilityId, name = "Synthetic mixed bulk store", kind, mixedBulk = true
        });
        var setup = await GetAsync(s.Admin, "/api/ledger/setup");
        var commodityId = setup.GetProperty("commodities").EnumerateArray()
            .Single(x => x.GetProperty("catalogId").GetGuid() == s.CatalogCommodityId).GetProperty("id").GetGuid();
        var policyId = Guid.NewGuid();
        await PolicyAsync(s, locationId, commodityId, "RAW_PADDY", "RAW_PADDY", policyId);
        return (locationId, policyId, commodityId);
    }
    private async Task<HttpClient> CreateIndependentOperatorAsync(LedgerScenario s)
    {
        var (user, session) = await factory.SeedUserAsync(Roles.MillOperator, s.Accounts["operator"].User.OrganizationId);
        var client = factory.CreateRealCookieClient(user, session);
        await CsrfAsync(client);
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/assignments", new { userId = user.Id, facilityId = s.FacilityId });
        return client;
    }

    private static Task<JsonElement> PolicyAsync(LedgerScenario s, Guid locationId, Guid commodityId, string stage, string product, Guid? policyId = null) =>
        SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/mixed-bulk-policies", new
        {
            id = policyId ?? Guid.NewGuid(), locationId, ownerId = s.OwnerId, commodityId, stage, productIdentity = product,
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = s.EvidenceId, identityScopeConfirmed = true
        });

    private async Task<decimal> PositionAsync(Guid lotId, Guid locationId)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COALESCE((SELECT quantity_kg FROM ledger.positions WHERE lot_id=$1 AND location_id=$2),0)", owner);
        command.Parameters.AddWithValue(lotId); command.Parameters.AddWithValue(locationId);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<JsonElement> ArriveAsync(MixedSite site, string quantity)
    {
        var receipt = await SendAsync(site.Scenario.Purchaser, HttpMethod.Post, "/api/procurements",
            site.Scenario.ReceiptBody(Guid.NewGuid(), [quantity], site.LocationId));
        await site.Scenario.ConfirmArrivalAsync(receipt);
        return receipt;
    }

    private async Task<(Guid LotId, long Revision)> PoolAsync(Guid locationId)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT lot_id,revision FROM ledger.mixed_bulk_pools WHERE location_id=$1", owner);
        command.Parameters.AddWithValue(locationId);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync(), "Mixed-bulk location must have current-pool state");
        return (row.IsDBNull(0) ? Guid.Empty : row.GetGuid(0), row.GetInt64(1));
    }

    private async Task<Dictionary<Guid, decimal>> SharesAsync(Guid lotId)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT source_lot_id,quantity_kg FROM ledger.contributions WHERE lot_id=$1 AND quantity_kg>0 ORDER BY source_lot_id", owner);
        command.Parameters.AddWithValue(lotId);
        await using var rows = await command.ExecuteReaderAsync();
        var shares = new Dictionary<Guid, decimal>();
        while (await rows.ReadAsync()) shares.Add(rows.GetGuid(0), rows.GetDecimal(1));
        return shares;
    }

    private static async Task<Guid> TransferAsync(MixedSite site, Guid id, Guid lotId, long revision, string quantity, Guid destination)
    {
        var response = await SendAsync(site.Scenario.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id, lotId, fromLocationId = site.LocationId, toLocationId = destination, quantityKg = quantity,
            poolRevision = revision, occurredAt = DateTimeOffset.UtcNow
        });
        return response.GetProperty("actualLotId").GetGuid();
    }

    private static Task<JsonElement> TraceAsync(HttpClient client, Guid lotId, string direction = "forward", string view = "current") =>
        SendAsync(client, HttpMethod.Post, "/api/ledger/trace", new { lotId, direction, view });
    private static Guid[] TraceIds(JsonElement trace) => trace.GetProperty("lots").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();

    [Fact]
    public async Task Trace_structural_zero_branches_do_not_claim_positive_source_paths_through_custody()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var first = Lots(await ArriveAsync(site, "0.000001")).Single();
        var second = Lots(await ArriveAsync(site, "0.000001")).Single();
        var (pool, revision) = await PoolAsync(site.LocationId);
        var aliquot = await TransferAsync(site, Guid.NewGuid(), pool, revision, "0.000001", s.LocationId);
        var retained = (await SharesAsync(aliquot)).Keys.Single();
        var absent = retained == first ? second : first;
        var trace = await TraceAsync(s.Customer, absent);
        // Structural relations stay visible, but source-specific evidence must not
        // traverse a shared pool onto an aliquot with zero share of this root.
        var lot = trace.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == aliquot);
        Assert.Empty(lot.GetProperty("positivePathSourceIds").EnumerateArray());
        var edge = trace.GetProperty("edges").EnumerateArray().Single(e => e.GetProperty("outputLotId").GetGuid() == aliquot);
        Assert.DoesNotContain(edge.GetProperty("sourceAllocations").EnumerateArray(), x => x.GetProperty("sourceLotId").GetGuid() == absent);
        Assert.Equal("PERSISTED_ALLOCATIONS", trace.GetProperty("positivePathCoverage").GetString());
        await s.HoldAsync(absent);
        var heldTrace = await TraceAsync(s.Customer, aliquot);
        Assert.False(heldTrace.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == aliquot).GetProperty("effectiveHold").GetBoolean());
    }

    [Fact]
    public async Task Trace_custody_preserves_generations_actual_processing_chronology_and_effective_HOLD()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var root = Lots(await ArriveAsync(site, "10")).Single();
        var (firstPool, revision) = await PoolAsync(site.LocationId);
        var aliquot = await TransferAsync(site, Guid.NewGuid(), firstPool, revision, "4", s.LocationId);
        var lateRoot = Lots(await ArriveAsync(site, "5")).Single();
        var (currentPool, _) = await PoolAsync(site.LocationId);
        var processed = await s.CommitAsync(await s.DraftAsync("DRYING", [(aliquot, "4")], [("DRIED_PADDY", "4")]));
        var output = Output(processed);
        var backward = await TraceAsync(s.Customer, output, "backward");
        Assert.Contains(root, TraceIds(backward)); Assert.Contains(firstPool, TraceIds(backward));
        Assert.Contains(aliquot, TraceIds(backward)); Assert.DoesNotContain(lateRoot, TraceIds(backward));
        Assert.DoesNotContain(currentPool, TraceIds(backward));
        Assert.Contains(output, TraceIds(await TraceAsync(s.Customer, root)));
        Assert.DoesNotContain(aliquot, TraceIds(await TraceAsync(s.Customer, lateRoot)));
        Assert.Equal(2, backward.GetProperty("schemaVersion").GetInt32());
        Assert.Contains(backward.GetProperty("edges").EnumerateArray(), e =>
            e.GetProperty("relationType").GetString() == "CUSTODY" && e.GetProperty("outputLotId").GetGuid() == aliquot);
        var hold = await s.HoldAsync(lateRoot);
        var held = await TraceAsync(s.Customer, currentPool, "backward");
        var pool = held.GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == currentPool);
        Assert.Contains("HOLD", pool.GetProperty("restrictions").EnumerateArray().Select(x => x.GetString()));
        Assert.False(pool.GetProperty("directHold").GetBoolean()); Assert.True(pool.GetProperty("effectiveHold").GetBoolean());
        Assert.All(pool.GetProperty("positions").EnumerateArray(), x => Assert.Equal("0.000000", x.GetProperty("availableKg").GetString()));
        var old = (await TraceAsync(s.Customer, aliquot)).GetProperty("lots").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == aliquot);
        Assert.False(old.GetProperty("effectiveHold").GetBoolean());
        var history = await TraceAsync(s.Customer, output, "backward", "history");
        Assert.Equal(TraceIds(backward).Order(), TraceIds(history).Order());
        using var denied = await s.ForeignCustomer.PostAsJsonAsync("/api/ledger/trace", new { lotId = currentPool });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, s.OwnerId);
        using var export = factory.CreateRealCookieClient(user, session); await CsrfAsync(export);
        var redacted = await TraceAsync(export, lateRoot);
        Assert.Contains(currentPool, TraceIds(redacted));
        Assert.All(redacted.GetProperty("edges").EnumerateArray(), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("sourceAllocations").ValueKind));
        Assert.Empty(redacted.GetProperty("sources").EnumerateArray());
        Assert.Contains(currentPool, TraceIds(await TraceAsync(s.Admin, currentPool, "backward")));
    }

    [Fact]
    public async Task Trace_processing_uses_resolved_aliquot_not_logical_pool_and_keeps_output_ingress()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var outputSite = await AddMixedLocationAsync(s);
        await PolicyAsync(s, outputSite.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        var root = Lots(await ArriveAsync(site, "10")).Single();
        var (pool, revision) = await PoolAsync(site.LocationId);
        var id = Guid.NewGuid();
        var draft = JsonSerializer.SerializeToNode(s.OperationBody(id, "DRYING", [(pool, "4")], [("DRIED_PADDY", "4")],
            inputLocationId: site.LocationId, outputLocationId: outputSite.LocationId))!.AsObject();
        draft["inputs"]![0]!["poolRevision"] = revision;
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", draft);
        var committed = await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        var (outputPool, _) = await PoolAsync(outputSite.LocationId);
        var backward = await TraceAsync(s.Customer, outputPool, "backward");
        Assert.Contains(root, TraceIds(backward)); Assert.Contains(pool, TraceIds(backward));
        var processing = Assert.Single(backward.GetProperty("edges").EnumerateArray(), x => x.GetProperty("relationType").GetString() == "PROCESSING");
        Assert.NotEqual(pool, processing.GetProperty("inputLotId").GetGuid());
        Assert.Equal(Output(committed), processing.GetProperty("outputLotId").GetGuid());
        Assert.Equal("RESOLVED_INPUTS", processing.GetProperty("basis").GetString());
        Assert.Equal("4.000000", processing.GetProperty("sourceAllocations")[0].GetProperty("quantityKg").GetString());
        Assert.Contains(backward.GetProperty("edges").EnumerateArray(), x => x.GetProperty("outputLotId").GetGuid() == outputPool);
        Assert.Equal(TraceIds(backward).Length, TraceIds(backward).Distinct().Count());
    }

    [Fact]
    public async Task Mixed_bulk_requires_explicit_scoped_policy_and_rejects_unauthorized_or_unconfirmed_policy()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var policy = new
        {
            id = Guid.NewGuid(), locationId = site.LocationId, ownerId = s.OwnerId, commodityId = site.CommodityId,
            stage = "RAW_PADDY", productIdentity = "RAW_PADDY", effectiveFrom = DateTimeOffset.UtcNow,
            evidenceId = s.EvidenceId, identityScopeConfirmed = true
        };
        await RejectedAsync(s.Purchaser, HttpMethod.Post, "/api/ledger/mixed-bulk-policies", policy, HttpStatusCode.Forbidden);
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/mixed-bulk-policies",
            new { policy.id, policy.locationId, policy.ownerId, policy.commodityId, policy.stage, policy.productIdentity,
                policy.effectiveFrom, policy.evidenceId, identityScopeConfirmed = false });
        var configured = await GetAsync(s.Admin, "/api/ledger/setup");
        var configuredLocation = configured.GetProperty("locations").EnumerateArray()
            .Single(location => location.GetProperty("id").GetGuid() == site.LocationId);
        Assert.True(configuredLocation.GetProperty("mixedBulk").GetBoolean());
        await RejectedAsync(s.Purchaser, HttpMethod.Put, $"/api/ledger/locations/{site.LocationId}",
            new { version = configuredLocation.GetProperty("version").GetString(), mixedBulk = false }, HttpStatusCode.Forbidden);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM ledger.mixed_bulk_policies WHERE location_id=$1", site.LocationId));

        using var noPolicy = await CreateAsync(factory);
        var location = Guid.NewGuid();
        await SendAsync(noPolicy.Admin, HttpMethod.Post, "/api/ledger/locations", new
            { id = location, facilityId = noPolicy.FacilityId, name = "Unconfigured mixed store", kind = "STORE", mixedBulk = true });
        var receipt = await SendAsync(noPolicy.Purchaser, HttpMethod.Post, "/api/procurements",
            noPolicy.ReceiptBody(Guid.NewGuid(), ["10"], location));
        using var failed = await RawAsync(noPolicy.Operator, HttpMethod.Post,
            $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", MeasurementBody(receipt));
        Assert.False(failed.IsSuccessStatusCode);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.custody_events WHERE location_id=$1", location));
        var source = await noPolicy.Purchaser.GetFromJsonAsync<SourceLotResponse>(
            $"/api/source-lots/{receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid()}");
        Assert.NotNull(source);
        Assert.Equal("0", source.UsableStockKg);
    }

    [Fact]
    public async Task Incompatible_processing_stage_cannot_enter_mixed_location_without_matching_policy()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var source = Lots(await s.ReceiptAsync("10")).Single();
        var operation = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "10")], [("DRIED_PADDY", "10")]));
        var output = Output(operation);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = output, fromLocationId = s.LocationId, toLocationId = site.LocationId,
            quantityKg = "10", occurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(10m, await PositionAsync(output, s.LocationId));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.custody_events WHERE location_id=$1", site.LocationId));
        Assert.Equal(Guid.Empty, (await PoolAsync(site.LocationId)).LotId);
    }
    [Fact]
    public async Task Bagged_rice_output_needs_exact_mixed_stage_and_product_policy()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await PolicyAsync(s, site.LocationId, site.CommodityId, "HEAD_RICE", "HEAD_RICE");
        await s.PolicyAsync("BAGGING", "0");
        var raw = Lots(await s.ReceiptAsync("20")).Single();
        var dried = Output(await s.CommitAsync(await s.DraftAsync("DRYING", [(raw, "20")], [("DRIED_PADDY", "20")])));
        var head = Output(await s.CommitAsync(await s.DraftAsync("MILLING", [(dried, "20")], [("HEAD_RICE", "20")])));
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = head, fromLocationId = s.LocationId, toLocationId = site.LocationId,
            quantityKg = "20", occurredAt = DateTimeOffset.UtcNow
        });
        var (pool, revision) = await PoolAsync(site.LocationId);
        var opId = Guid.NewGuid();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(
            s.OperationBody(opId, "BAGGING", [(pool, "10")], [("BAGGED_RICE", "10")],
                productIdentity: "HEAD_RICE", inputLocationId: site.LocationId, outputLocationId: site.LocationId))!.AsObject();
        body["inputs"]![0]!["poolRevision"] = revision;
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{opId}/commit", new { });
        Assert.Equal(20m, (await SharesAsync((await PoolAsync(site.LocationId)).LotId)).Values.Sum());
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.custody_events WHERE command_id=$1", opId));
    }

    [Fact]
    public async Task Cleared_mixed_compartment_accepts_first_owner_receipt_and_sets_location_owner()
    {
        var site = await CreateMixedSiteAsync("COMPARTMENT"); using var s = site.Scenario;
        await SendAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{site.LocationId}/clearance",
            new { evidenceId = s.EvidenceId, reason = "Synthetic empty compartment verified" });
        var receipt = await ArriveAsync(site, "10");
        Assert.Equal(10m, (await SharesAsync((await PoolAsync(site.LocationId)).LotId)).Values.Sum());
        var setup = await GetAsync(s.Admin, "/api/ledger/setup");
        var location = setup.GetProperty("locations").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == site.LocationId);
        Assert.Equal(s.OwnerId, location.GetProperty("ownerId").GetGuid());
        Assert.False(location.GetProperty("cleared").GetBoolean());
        Assert.Single(Lots(receipt));
    }


    [Fact]
    public async Task Within_threshold_and_manager_approved_shortage_arrivals_enter_pool_with_measured_mass()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var within = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["100"], site.LocationId));
        var measured = await SendAsync(s.Operator, HttpMethod.Post,
            $"/api/ledger/receiving/{within.GetProperty("id").GetGuid()}/measurements",
            MeasurementBody(within, actual: ["99"]));
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", measured.GetProperty("status").GetString());

        var shortage = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["60"], site.LocationId));
        var pending = await SendAsync(s.Operator, HttpMethod.Post,
            $"/api/ledger/receiving/{shortage.GetProperty("id").GetGuid()}/measurements",
            MeasurementBody(shortage, actual: ["55"]));
        Assert.Equal("AWAITING_MANAGER_REVIEW", pending.GetProperty("status").GetString());
        var accepted = await SendAsync(s.Manager, HttpMethod.Post,
            $"/api/ledger/receiving/{shortage.GetProperty("id").GetGuid()}/reviews",
            Review(pending, "APPROVE_SHORTAGE", s.EvidenceId));
        Assert.Equal("RECEIVED_SHORTAGE_APPROVED", accepted.GetProperty("status").GetString());

        var (pool, _) = await PoolAsync(site.LocationId);
        Assert.Equal(154m, (await SharesAsync(pool)).Values.Sum());
        Assert.Equal(99m, (await SharesAsync(pool))[Lots(within).Single()]);
        Assert.Equal(55m, (await SharesAsync(pool))[Lots(shortage).Single()]);
    }

    [Fact]
    public async Task Arrival_withdrawal_and_later_topup_conserve_source_shares_without_rewriting_aliquot()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipts = new[] { await ArriveAsync(site, "50000"), await ArriveAsync(site, "30000"), await ArriveAsync(site, "20000") };
        var roots = receipts.SelectMany(Lots).ToArray();
        var (initialPool, initialRevision) = await PoolAsync(site.LocationId);
        Assert.Equal(3, (await SharesAsync(initialPool)).Count);
        Assert.Equal(100000m, (await SharesAsync(initialPool)).Values.Sum());
        var initialShares = await SharesAsync(initialPool);
        Assert.Equal(new decimal[] { 20000, 30000, 50000 }, initialShares.Values.Order().ToArray());

        var aliquot = await TransferAsync(site, Guid.NewGuid(), initialPool, initialRevision, "40000", s.SecondLocationId);
        var (remainderPool, revision) = await PoolAsync(site.LocationId);
        Assert.NotEqual(initialPool, remainderPool);
        Assert.Equal(60000m, (await SharesAsync(remainderPool)).Values.Sum());
        Assert.Equal(40000m, (await SharesAsync(aliquot)).Values.Sum());
        var aliquotShares = await SharesAsync(aliquot);
        Assert.Equal(new decimal[] { 8000, 12000, 20000 }, aliquotShares.Values.Order().ToArray());
        Assert.Equal(new decimal[] { 12000, 18000, 30000 }, (await SharesAsync(remainderPool)).Values.Order().ToArray());

        var fourth = await ArriveAsync(site, "40000");
        var fourthRoot = Lots(fourth).Single();
        var (finalPool, finalRevision) = await PoolAsync(site.LocationId);
        Assert.True(finalRevision > revision);
        Assert.Equal(100000m, (await SharesAsync(finalPool)).Values.Sum());
        Assert.Equal(new decimal[] { 12000, 18000, 30000, 40000 }, (await SharesAsync(finalPool)).Values.Order().ToArray());
        Assert.DoesNotContain(fourthRoot, aliquotShares.Keys);
        Assert.Equal(roots.Length, initialShares.Count);
        var procurementPage = await GetAsync(s.Purchaser, "/api/procurements?page=1&pageSize=50");
        foreach (var (receipt, expected) in receipts.Zip(new[] { 50000m, 30000m, 20000m }))
        {
            var sourceId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
            var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sourceId}");
            Assert.NotNull(source);
            Assert.Equal(expected, source.CurrentStock.Sum(x => decimal.Parse(x.OnHandKg, CultureInfo.InvariantCulture)));
            Assert.Equal(100000m, source.CurrentStock.Where(x => x.IsMixedBulk).Select(x =>
                decimal.Parse(x.PoolTotalKg, CultureInfo.InvariantCulture)).Distinct().Single());
            Assert.Single(source.CurrentStock, x => x.IsMixedBulk);
            Assert.Contains(source.Lineage, x => x.IsCurrentPool && x.LotId == finalPool);
            var summary = procurementPage.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == receipt.GetProperty("id").GetGuid());
            Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), summary.GetProperty("totalNetMassKg").GetString());
            Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), summary.GetProperty("remainingStockKg").GetString());
            Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), summary.GetProperty("availableStockKg").GetString());
        }
    }

    [Fact]
    public async Task Microkg_withdrawal_uses_exact_source_complement()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await ArriveAsync(site, "0.000003"); await ArriveAsync(site, "0.000002"); await ArriveAsync(site, "0.000004");
        var (pool, revision) = await PoolAsync(site.LocationId);
        var before = await SharesAsync(pool);
        Assert.Equal(0.000009m, before.Values.Sum());
        var aliquot = await TransferAsync(site, Guid.NewGuid(), pool, revision, "0.000004", s.SecondLocationId);
        var (remainder, _) = await PoolAsync(site.LocationId);
        var withdrawn = await SharesAsync(aliquot); var remaining = await SharesAsync(remainder);
        Assert.Equal(0.000004m, withdrawn.Values.Sum());
        Assert.Equal(0.000005m, remaining.Values.Sum());
        Assert.Equal(before.Keys.Order().ToArray(), withdrawn.Keys.Concat(remaining.Keys).Distinct().Order().ToArray());
        foreach (var root in before.Keys)
            Assert.Equal(before[root], withdrawn.GetValueOrDefault(root) + remaining.GetValueOrDefault(root));
        Assert.All(withdrawn.Values.Concat(remaining.Values), share => Assert.True(share > 0));
    }

    [Fact]
    public async Task Concurrent_withdrawals_from_one_revision_never_overspend_pool()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await ArriveAsync(site, "10");
        var (pool, revision) = await PoolAsync(site.LocationId);
        using var firstClient = await CreateIndependentOperatorAsync(s);
        using var secondClient = await CreateIndependentOperatorAsync(s);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> WithdrawAsync(HttpClient client)
        {
            await start.Task;
            return await RawAsync(client, HttpMethod.Post, "/api/ledger/transfers",
                new { id = Guid.NewGuid(), lotId = pool, fromLocationId = site.LocationId,
                    toLocationId = s.SecondLocationId, quantityKg = "7", poolRevision = revision, occurredAt = DateTimeOffset.UtcNow });
        }
        var first = WithdrawAsync(firstClient);
        var second = WithdrawAsync(secondClient);
        start.SetResult(true);
        var responses = await Task.WhenAll(first, second);
        try
        {
            var success = Assert.Single(responses, response => response.IsSuccessStatusCode);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var result = JsonDocument.Parse(await success.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(7m, (await SharesAsync(result.GetProperty("actualLotId").GetGuid())).Values.Sum());
            var (currentPool, _) = await PoolAsync(site.LocationId);
            Assert.Equal(3m, (await SharesAsync(currentPool)).Values.Sum());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Matching_transfer_retry_is_idempotent_conflict_is_rejected_and_stale_revision_does_not_spend_stock()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await ArriveAsync(site, "100"); await ArriveAsync(site, "20");
        var (pool, revision) = await PoolAsync(site.LocationId); var id = Guid.NewGuid();
        var body = new { id, lotId = pool, fromLocationId = site.LocationId, toLocationId = s.SecondLocationId, quantityKg = "10", poolRevision = revision, occurredAt = DateTimeOffset.UtcNow };
        var first = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", body);
        var replay = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", body);
        Assert.Equal(first.GetProperty("actualLotId").GetGuid(), replay.GetProperty("actualLotId").GetGuid());
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id, lotId = pool, fromLocationId = site.LocationId, toLocationId = s.SecondLocationId,
            quantityKg = "11", poolRevision = revision, occurredAt = body.occurredAt
        }, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        var current = await PoolAsync(site.LocationId);
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = pool, fromLocationId = site.LocationId, toLocationId = s.SecondLocationId,
            quantityKg = "10", poolRevision = revision, occurredAt = DateTimeOffset.UtcNow
        }, HttpStatusCode.Conflict);
        Assert.Equal(current.LotId, (await PoolAsync(site.LocationId)).LotId);
        Assert.Equal(110m, (await SharesAsync(current.LotId)).Values.Sum());
    }

    [Fact]
    public async Task Selected_ordinary_position_enters_pool_without_sweeping_remote_stock_and_pool_to_pool_updates_both()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var second = await AddMixedLocationAsync(s);
        var receipt = await s.ReceiptAsync("100");
        var source = Lots(receipt).Single();
        await s.TransferAsync(source, "30");
        Assert.Equal(70m, await PositionAsync(source, s.LocationId));
        Assert.Equal(30m, await PositionAsync(source, s.SecondLocationId));

        var intoPool = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = source, fromLocationId = s.SecondLocationId, toLocationId = site.LocationId,
            quantityKg = "10", occurredAt = DateTimeOffset.UtcNow
        });
        Assert.NotEqual(source, intoPool.GetProperty("actualLotId").GetGuid());
        Assert.Equal(70m, await PositionAsync(source, s.LocationId));
        Assert.Equal(0m, await PositionAsync(source, s.SecondLocationId));
        var sourceId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        var sourceSummary = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sourceId}");
        Assert.NotNull(sourceSummary);
        Assert.Equal("100", sourceSummary.InitialNetMassKg);
        Assert.Equal(100m, sourceSummary.CurrentStock.Sum(x => decimal.Parse(x.OnHandKg, CultureInfo.InvariantCulture)));
        Assert.Equal(20m, sourceSummary.CurrentStock
            .Where(x => x.LocationId == s.SecondLocationId && !x.IsMixedBulk)
            .Sum(x => decimal.Parse(x.OnHandKg, CultureInfo.InvariantCulture)));
        var (firstPool, firstRevision) = await PoolAsync(site.LocationId);
        Assert.Equal(firstPool, intoPool.GetProperty("currentPoolLotId").GetGuid());
        Assert.Equal(10m, (await SharesAsync(firstPool)).Values.Sum());
        var poolPosition = Assert.Single(sourceSummary.CurrentStock, x => x.IsMixedBulk);
        Assert.Equal("10", poolPosition.OnHandKg);
        Assert.Equal("10", poolPosition.AvailableKg);
        Assert.Contains(sourceSummary.Lineage, x => x.IsCurrentPool && x.LotId == firstPool);

        await TransferAsync(site, Guid.NewGuid(), firstPool, firstRevision, "5", second.LocationId);
        var (firstRemainder, _) = await PoolAsync(site.LocationId);
        var (secondPool, _) = await PoolAsync(second.LocationId);
        Assert.Equal(5m, (await SharesAsync(firstRemainder)).Values.Sum());
        Assert.Equal(5m, (await SharesAsync(secondPool)).Values.Sum());
    }

    [Fact]

    public async Task Processing_uses_pool_revision_and_ingresses_output_without_rewriting_logical_draft()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var outputSite = await AddMixedLocationAsync(s);
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        await PolicyAsync(s, outputSite.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        await s.PolicyAsync("DRYING", "2");
        await ArriveAsync(site, "100");
        var (pool, revision) = await PoolAsync(site.LocationId);
        var invalidId = Guid.NewGuid();
        var invalid = System.Text.Json.JsonSerializer.SerializeToNode(
            s.OperationBody(invalidId, "DRYING", [(pool, "40")], [("DRIED_PADDY", "39")], "0",
                inputLocationId: site.LocationId, outputLocationId: outputSite.LocationId))!.AsObject();
        invalid["inputs"]![0]!["poolRevision"] = revision;
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", invalid);
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{invalidId}/commit", new { });
        Assert.Equal(100m, (await SharesAsync(pool)).Values.Sum());
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.events WHERE operation_id=$1 AND kind='DRYING'", invalidId));
        var id = Guid.NewGuid();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(
            s.OperationBody(id, "DRYING", [(pool, "40")], [("DRIED_PADDY", "40")],
                inputLocationId: site.LocationId, outputLocationId: outputSite.LocationId))!.AsObject();
        body["inputs"]![0]!["poolRevision"] = revision;
        var draft = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var committed = await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{id}/commit", new { });
        var saved = await GetAsync(s.Operator, $"/api/ledger/operations/{id}");
        Assert.Equal(pool, saved.GetProperty("inputs")[0].GetProperty("lotId").GetGuid());
        Assert.Equal(draft.GetProperty("inputs")[0].GetProperty("lotId").GetGuid(), pool);
        var resolved = await ScalarJsonAsync("SELECT details->'resolvedInputs' FROM ledger.events WHERE operation_id=$1 AND kind='DRYING'", id);
        Assert.Equal(pool, resolved[0].GetProperty("logicalLotId").GetGuid());
        var actualLot = resolved[0].GetProperty("actualLotId").GetGuid();
        Assert.NotEqual(pool, actualLot);
        Assert.Equal(actualLot, await ScalarAsync<Guid>(
            "SELECT input_lot_id FROM ledger.custody_processing_allocations WHERE operation_id=$1", id));
        var responseResolution = committed.GetProperty("resolvedInputs")[0];
        Assert.Equal(pool, responseResolution.GetProperty("logicalLotId").GetGuid());
        Assert.Equal(actualLot, responseResolution.GetProperty("actualLotId").GetGuid());
        Assert.Equal(actualLot, responseResolution.GetProperty("lotId").GetGuid());
        Assert.Equal(60m, (await SharesAsync((await PoolAsync(site.LocationId)).LotId)).Values.Sum());
        var poolOutput = committed.GetProperty("poolOutputs").EnumerateArray().Single();
        Assert.Equal(outputSite.LocationId, poolOutput.GetProperty("locationId").GetGuid());
        Assert.Equal(40m, (await SharesAsync(poolOutput.GetProperty("currentPoolLotId").GetGuid())).Values.Sum());
    }

    [Fact]
    public async Task Processing_draft_rejects_superseded_pool_revision_without_rewriting_or_spending()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        await ArriveAsync(site, "50");
        var (selectedPool, revision) = await PoolAsync(site.LocationId);
        var opId = Guid.NewGuid();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(
            s.OperationBody(opId, "DRYING", [(selectedPool, "10")], [("DRIED_PADDY", "10")],
                inputLocationId: site.LocationId))!.AsObject();
        body["inputs"]![0]!["poolRevision"] = revision;
        var draft = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var logicalInput = draft.GetProperty("inputs")[0].GetProperty("lotId").GetGuid();
        await ArriveAsync(site, "5");
        await RejectedAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{opId}/commit", new { },
            HttpStatusCode.Conflict);
        var saved = await GetAsync(s.Operator, $"/api/ledger/operations/{opId}");
        Assert.Equal(logicalInput, saved.GetProperty("inputs")[0].GetProperty("lotId").GetGuid());
        Assert.Equal(55m, (await SharesAsync((await PoolAsync(site.LocationId)).LotId)).Values.Sum());
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.events WHERE operation_id=$1 AND kind='DRYING'", opId));
    }

    [Fact]
    public async Task Zero_share_root_hold_does_not_restrict_one_micro_kg_aliquot()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var first = await ArriveAsync(site, "0.000001"); var second = await ArriveAsync(site, "0.000001");
        var roots = new[] { Lots(first).Single(), Lots(second).Single() };
        var (pool, revision) = await PoolAsync(site.LocationId);
        var aliquot = await TransferAsync(site, Guid.NewGuid(), pool, revision, "0.000001", s.SecondLocationId);
        var aliquotSources = (await SharesAsync(aliquot)).Keys.ToHashSet();
        var zeroSource = roots.Single(root => !aliquotSources.Contains(root));
        Assert.Single(aliquotSources);
        Assert.Equal(await ScalarAsync<Guid>(
            "SELECT source_lot_id FROM ledger.contributions WHERE lot_id=$1 ORDER BY source_lot_id LIMIT 1", pool),
            aliquotSources.Single());
        await s.HoldAsync(zeroSource);
        Assert.Equal(0.000001m, Quantity(await s.LotAsync(aliquot), "availableKg"));
        Assert.Equal(0.000001m, Quantity(await s.LotAsync(aliquot)));
    }
    [Fact]
    public async Task Zero_share_prior_pool_hold_does_not_restrict_later_b_only_aliquot()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipts = new[]
        {
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["0.000001"], site.LocationId)),
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["0.000001"], site.LocationId))
        };
        var ordered = receipts.OrderBy(x => Lots(x).Single().ToString("N"), StringComparer.Ordinal).ToArray();
        var bSource = Lots(ordered[0]).Single();
        var firstPoolReceipt = ordered[1];
        var firstPoolRoot = Lots(firstPoolReceipt).Single();
        await s.ConfirmArrivalAsync(firstPoolReceipt);
        var (p1, _) = await PoolAsync(site.LocationId);
        Assert.Equal(new Dictionary<Guid, decimal> { [firstPoolRoot] = 0.000001m }, await SharesAsync(p1));
        await s.ConfirmArrivalAsync(ordered[0]);
        var (p2, revision) = await PoolAsync(site.LocationId);
        Assert.NotEqual(p1, p2);
        Assert.Equal(new[] { 0.000001m, 0.000001m }, (await SharesAsync(p2)).Values.Order().ToArray());
        var aliquot = await TransferAsync(site, Guid.NewGuid(), p2, revision, "0.000001", s.SecondLocationId);
        Assert.Equal(new Dictionary<Guid, decimal> { [bSource] = 0.000001m }, await SharesAsync(aliquot));
        await s.HoldAsync(p1);
        Assert.Equal(0.000001m, Quantity(await s.LotAsync(aliquot), "availableKg"));
    }


    [Fact]
    public async Task Late_root_hold_reaches_pool_after_ordinary_processing_and_transfer()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        var source = Lots(await s.ReceiptAsync("20")).Single();
        var operationId = Guid.NewGuid();
        var body = s.OperationBody(operationId, "DRYING", [(source, "20")], [("DRIED_PADDY", "20")],
            inputLocationId: s.LocationId, outputLocationId: s.LocationId);
        var operation = await s.CommitAsync(await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body));
        var intermediate = Output(operation);
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = intermediate, fromLocationId = s.LocationId, toLocationId = site.LocationId,
            quantityKg = "20", occurredAt = DateTimeOffset.UtcNow
        });
        var (pool, _) = await PoolAsync(site.LocationId);
        await s.HoldAsync(source);
        Assert.Equal(0m, Quantity(await s.LotAsync(pool), "availableKg"));
    }

    [Fact]
    public async Task Hold_on_ordinary_intermediate_blocks_later_mixed_ingress_but_not_ordinary_descendant()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        await PolicyAsync(s, site.LocationId, site.CommodityId, "HEAD_RICE", "HEAD_RICE");
        var source = Lots(await s.ReceiptAsync("20")).Single();
        var drying = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "20")], [("DRIED_PADDY", "20")]));
        var intermediate = Output(drying);
        var milling = await s.CommitAsync(await s.DraftAsync("MILLING", [(intermediate, "20")], [("HEAD_RICE", "20")]));
        var descendant = Output(milling);

        await s.HoldAsync(intermediate);
        Assert.Equal(20m, Quantity(await s.LotAsync(descendant), "availableKg"));
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = descendant, fromLocationId = s.LocationId, toLocationId = site.LocationId,
            quantityKg = "20", occurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(20m, await PositionAsync(descendant, s.LocationId));
        Assert.Equal(Guid.Empty, (await PoolAsync(site.LocationId)).LotId);
        Assert.Equal(20m, Quantity(await s.LotAsync(descendant), "availableKg"));
    }

    [Fact]
    public async Task Concurrent_root_hold_and_withdrawal_never_leaves_unrestricted_aliquot()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipt = await ArriveAsync(site, "10");
        var root = Lots(receipt).Single();
        var (pool, revision) = await PoolAsync(site.LocationId);
        using var transferClient = await CreateIndependentOperatorAsync(s);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<JsonElement> CreateHoldAsync()
        {
            await start.Task;
            return await SendAsync(s.Customer, HttpMethod.Post, "/api/ledger/holds",
                new { id = Guid.NewGuid(), lotId = root, reason = "Synthetic concurrent HOLD" });
        }
        async Task<HttpResponseMessage> TransferAsync()
        {
            await start.Task;
            return await RawAsync(transferClient, HttpMethod.Post, "/api/ledger/transfers", new
            {
                id = Guid.NewGuid(), lotId = pool, fromLocationId = site.LocationId, toLocationId = s.SecondLocationId,
                quantityKg = "4", poolRevision = revision, occurredAt = DateTimeOffset.UtcNow
            });
        }
        var holdTask = CreateHoldAsync();
        var transferTask = TransferAsync();
        start.SetResult(true);
        await Task.WhenAll(holdTask, transferTask);
        using var transfer = await transferTask;
        if (transfer.IsSuccessStatusCode)
        {
            using var resultDocument = JsonDocument.Parse(await transfer.Content.ReadAsStringAsync());
            Assert.Equal(0m, Quantity(await s.LotAsync(resultDocument.RootElement.GetProperty("actualLotId").GetGuid()), "availableKg"));
        }
        else Assert.Contains(transfer.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest });
        Assert.Equal(0m, Quantity(await s.LotAsync((await PoolAsync(site.LocationId)).LotId), "availableKg"));
        var sourceId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sourceId}");
        Assert.NotNull(source);
        Assert.Equal(10m, source.CurrentStock.Sum(x => decimal.Parse(x.OnHandKg, CultureInfo.InvariantCulture)));
        Assert.Equal(0m, Quantity(await s.LotAsync(root), "availableKg"));
    }

    [Fact]
    public async Task Sample_before_pooling_and_late_report_keep_original_source_identity_and_not_assessed_state()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements",
            s.ReceiptBody(Guid.NewGuid(), ["25"], site.LocationId));
        var sourceId = receipt.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid();
        var sampleId = Guid.NewGuid(); var sampleCode = $"S-{sampleId:N}"; var seal = $"SEAL-{sampleId:N}";
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/samples", CaptureCommand(new
        {
            entityId = sampleId, sourceLotId = sourceId, code = sampleCode, sealCode = seal,
            collectedAt = DateTimeOffset.UtcNow, materialDescription = "Synthetic pre-pooling sample"
        }));
        await site.Scenario.ConfirmArrivalAsync(receipt);
        var reportId = Guid.NewGuid();
        await SendAsync(s.Customer, HttpMethod.Post, "/api/lab-reports", CaptureCommand(new
        {
            entityId = reportId, laboratoryName = "Synthetic Laboratory", reportNumber = $"LAB-{reportId:N}",
            issuedOn = "2026-10-19", receivedAt = DateTimeOffset.UtcNow, reportKind = "FINAL",
            relatedReportId = (Guid?)null, relationshipReason = (string?)null, fileName = "synthetic.pdf",
            contentType = "application/pdf", contentBase64 = Convert.ToBase64String([0x25, 0x50, 0x44, 0x46]),
            companyOnlyConfirmed = true, laboratoryConclusion = "Laboratory states pass",
            statedStandard = "Reference only", resultSets = new[]
            {
                new { entityId = Guid.NewGuid(), sampleId = (Guid?)null, laboratorySampleReference = (string?)null,
                    statedSampleCode = sampleCode, statedSealCode = seal, sectionReference = "page 1",
                    results = new[] { new { testLabel = "Moisture", substanceId = (Guid?)null,
                        originalResultText = "22.5", resultKind = "NUMERIC", numericValue = (string?)"22.5",
                        unit = "%", lod = (string?)null, loq = (string?)null, method = "Synthetic",
                        uncertainty = (string?)null, reference = "page 1" } } }
            }
        }));
        var draft = await GetAsync(s.Customer, $"/api/lab-reports/{reportId}");
        await SendAsync(s.Customer, HttpMethod.Post, $"/api/lab-reports/{reportId}/confirm", CaptureCommand(new
        {
            identityChecked = true, transcriptionChecked = true,
            matches = new[]
            {
                new { resultSetId = draft.GetProperty("resultSets")[0].GetProperty("id").GetGuid(),
                    sampleId, identityBasis = "SAMPLE_CODE", evidenceRef = (string?)null }
            }
        }));
        var confirmed = await GetAsync(s.Customer, $"/api/lab-reports/{reportId}");
        Assert.Equal(sourceId, confirmed.GetProperty("resultSets")[0].GetProperty("sourceLotId").GetGuid());
        Assert.Equal(sampleId, confirmed.GetProperty("resultSets")[0].GetProperty("confirmedSampleId").GetGuid());
        Assert.Equal("NOT_ASSESSED", confirmed.GetProperty("assessmentStatus").GetString());
        var source = await s.Purchaser.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{sourceId}");
        Assert.NotNull(source);
        Assert.Equal(sampleId, Assert.Single(source.SampleIds));
        Assert.Equal(25m, source.CurrentStock.Sum(x => decimal.Parse(x.OnHandKg, CultureInfo.InvariantCulture)));
        var (pool, _) = await PoolAsync(site.LocationId);
        Assert.Equal(25m, Quantity(await s.LotAsync(pool), "availableKg"));
    }

    [Fact]
    public async Task Holding_resolved_processing_intermediate_blocks_derived_output_but_ordinary_children_keep_direct_hold_scope()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        await ArriveAsync(site, "20");
        var (pool, revision) = await PoolAsync(site.LocationId);
        var opId = Guid.NewGuid();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(
            s.OperationBody(opId, "DRYING", [(pool, "10")], [("DRIED_PADDY", "10")],
                inputLocationId: site.LocationId))!.AsObject();
        body["inputs"]![0]!["poolRevision"] = revision;
        await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var committed = await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/operations/{opId}/commit", new { });
        var resolved = await ScalarJsonAsync("SELECT details->'resolvedInputs' FROM ledger.events WHERE operation_id=$1 AND kind='DRYING'", opId);
        var intermediate = resolved[0].GetProperty("actualLotId").GetGuid();
        var output = Output(committed);
        await s.HoldAsync(intermediate);
        Assert.Equal(0m, Quantity(await s.LotAsync(output), "availableKg"));

        var ordinary = Lots(await s.ReceiptAsync("20")).Single();
        var ordinaryOp = await s.CommitAsync(await s.DraftAsync("DRYING", [(ordinary, "10")], [("DRIED_PADDY", "10")]));
        await s.HoldAsync(ordinary);
        Assert.Equal(10m, Quantity(await s.LotAsync(Output(ordinaryOp)), "availableKg"));
    }

    [Fact]
    public async Task Mixed_custody_correction_fails_closed_without_reversing_or_unmixing_stock()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        await ArriveAsync(site, "20");
        var (pool, revision) = await PoolAsync(site.LocationId);
        var opId = Guid.NewGuid();
        var body = System.Text.Json.JsonSerializer.SerializeToNode(
            s.OperationBody(opId, "DRYING", [(pool, "10")], [("DRIED_PADDY", "10")],
                inputLocationId: site.LocationId))!.AsObject();
        body["inputs"]![0]!["poolRevision"] = revision;
        var original = await s.CommitAsync(await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body));
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/corrections", new
        {
            id = Guid.NewGuid(), operationId = opId, reason = "Synthetic mixed-custody correction must fail closed"
        });
        Assert.Equal(10m, Quantity(await s.LotAsync((await PoolAsync(site.LocationId)).LotId)));
        Assert.Equal(10m, Quantity(await s.LotAsync(Output(original))));
    }

    private async Task<JsonElement> ScalarJsonAsync(string sql, Guid id)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner); command.Parameters.AddWithValue(id);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!).RootElement.Clone();
    }


    [Fact]
    public async Task Ordinary_storage_transfer_keeps_source_lot_identity()
    {
        using var s = await CreateAsync(factory);
        var root = Lots(await s.ReceiptAsync("10")).Single();
        var other = Lots(await s.ReceiptAsync("10")).Single();
        Assert.NotEqual(root, other);
        await s.TransferAsync(root, "4");
        Assert.Equal(10m, Quantity(await s.LotAsync(root)));
        Assert.Equal(6m, await PositionAsync(root, s.LocationId));
        Assert.Equal(4m, await PositionAsync(root, s.SecondLocationId));
    }

    [Fact]
    public async Task Concurrent_compatible_arrivals_serialize_into_one_current_generation()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipts = new[]
        {
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["17"], site.LocationId)),
            await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["23"], site.LocationId))
        };
        using var firstClient = await CreateIndependentOperatorAsync(s);
        using var secondClient = await CreateIndependentOperatorAsync(s);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = receipts.Select((r, i) => SendArrivalAsync(r, i)).ToArray();
        async Task<HttpResponseMessage> SendArrivalAsync(JsonElement receipt, int index)
        {
            await start.Task;
            return await RawAsync(index == 0 ? firstClient : secondClient, HttpMethod.Post,
                $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", MeasurementBody(receipt));
        }
        start.SetResult(true);
        var responses = await Task.WhenAll(arrivals);
        try
        {
            foreach (var response in responses)
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
        var (pool, _) = await PoolAsync(site.LocationId);
        Assert.Equal(40m, (await SharesAsync(pool)).Values.Sum());
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM ledger.positions WHERE location_id=$1 AND quantity_kg>0", site.LocationId));
    }

    [Fact]
    public async Task Full_drain_clears_pointer_refill_starts_fresh_and_occupied_mode_change_rejects()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var first = await ArriveAsync(site, "25");
        var source = Lots(first).Single();
        var (pool, revision) = await PoolAsync(site.LocationId);
        var mode = (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("locations").EnumerateArray()
            .Single(location => location.GetProperty("id").GetGuid() == site.LocationId);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/locations/{site.LocationId}",
            new { version = mode.GetProperty("version").GetString(), mixedBulk = false }, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        await RejectedAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{site.LocationId}/clearance",
            new { evidenceId = s.EvidenceId, reason = "Cannot clear occupied mixed pool" }, HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        var evacuated = await TransferAsync(site, Guid.NewGuid(), pool, revision, "25", s.SecondLocationId);
        var drained = await PoolAsync(site.LocationId);
        Assert.Equal(Guid.Empty, drained.LotId);
        var foreignReceipt = await s.ForeignReceiptAsync("1", s.SecondLocationId);
        var foreignLot = Lots(foreignReceipt).Single();
        var foreignIdentity = await s.LotAsync(foreignLot, s.ForeignCustomer);
        var foreignEvidenceId = Guid.NewGuid();
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/evidence", new
        {
            id = foreignEvidenceId, ownerId = s.ForeignOwnerId, facilityId = s.FacilityId,
            fileName = "synthetic-foreign-owner-policy.txt", contentType = "text/plain",
            contentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Synthetic test evidence only"))
        });
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/mixed-bulk-policies", new
        {
            id = Guid.NewGuid(), locationId = site.LocationId, ownerId = s.ForeignOwnerId,
            commodityId = foreignIdentity.GetProperty("commodityId").GetGuid(),
            stage = foreignIdentity.GetProperty("stage").GetString(),
            productIdentity = foreignIdentity.GetProperty("productIdentity").GetString(),
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = foreignEvidenceId, identityScopeConfirmed = true
        });
        var foreignTransferId = Guid.NewGuid();
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = foreignTransferId, lotId = foreignLot, fromLocationId = s.SecondLocationId,
            toLocationId = site.LocationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(1m, await PositionAsync(foreignLot, s.SecondLocationId));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.custody_events WHERE command_id=$1", foreignTransferId));
        await PolicyAsync(s, site.LocationId, site.CommodityId, "DRIED_PADDY", "DRIED_PADDY");
        var productChangeId = Guid.NewGuid();
        var productChange = s.OperationBody(productChangeId, "DRYING", [(evacuated, "25")], [("DRIED_PADDY", "25")],
            inputLocationId: s.SecondLocationId, outputLocationId: s.SecondLocationId);
        var productChangeResult = await s.CommitAsync(await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", productChange));
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = Output(productChangeResult),
            fromLocationId = s.SecondLocationId, toLocationId = site.LocationId, quantityKg = "25",
            occurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(Guid.Empty, (await PoolAsync(site.LocationId)).LotId);
        var drainedMode = (await GetAsync(s.Admin, "/api/ledger/setup")).GetProperty("locations").EnumerateArray()
            .Single(location => location.GetProperty("id").GetGuid() == site.LocationId);
        await RejectedAsync(s.Admin, HttpMethod.Put, $"/api/ledger/locations/{site.LocationId}",
            new { version = drainedMode.GetProperty("version").GetString(), mixedBulk = false },
            HttpStatusCode.Conflict, HttpStatusCode.BadRequest);
        await SendAsync(s.Admin, HttpMethod.Post, $"/api/ledger/locations/{site.LocationId}/clearance",
            new { evidenceId = s.EvidenceId, reason = "Synthetic full drain verified before clearance" });
        var refill = await ArriveAsync(site, "7");
        var (fresh, freshRevision) = await PoolAsync(site.LocationId);
        Assert.True(freshRevision > drained.Revision);
        Assert.NotEqual(pool, fresh);
        Assert.DoesNotContain(source, (await SharesAsync(fresh)).Keys);
        Assert.Equal(7m, (await SharesAsync(fresh)).Values.Sum());
        Assert.Equal(0m, await PositionAsync(pool, site.LocationId));
        Assert.Single(Lots(refill));
    }

    [Fact]
    public async Task Future_source_hold_blocks_current_pool_but_not_prior_aliquot()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await ArriveAsync(site, "50"); await ArriveAsync(site, "30"); await ArriveAsync(site, "20");
        var (pool, revision) = await PoolAsync(site.LocationId);
        var aliquot = await TransferAsync(site, Guid.NewGuid(), pool, revision, "40", s.SecondLocationId);
        var newReceipt = await ArriveAsync(site, "40");
        var futureRoot = Lots(newReceipt).Single();
        await s.HoldAsync(futureRoot);
        var (heldPool, _) = await PoolAsync(site.LocationId);
        Assert.Equal(0m, Quantity(await s.LotAsync(heldPool), "availableKg"));
        Assert.Equal(40m, Quantity(await s.LotAsync(aliquot), "availableKg"));
        await RejectedAsync(s.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = heldPool, fromLocationId = site.LocationId, toLocationId = s.SecondLocationId,
            quantityKg = "1", poolRevision = (await PoolAsync(site.LocationId)).Revision, occurredAt = DateTimeOffset.UtcNow
        });
    }

    [Fact]
    public async Task Matching_effective_hold_allows_only_manager_isolation_and_does_not_release_hold()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipt = await ArriveAsync(site, "10");
        var root = Lots(receipt).Single(); var hold = await s.HoldAsync(root);
        var (pool, revision) = await PoolAsync(site.LocationId);
        var isolated = await SendAsync(s.Manager, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = pool, fromLocationId = site.LocationId, toLocationId = s.IsolationLocationId,
            quantityKg = "4", poolRevision = revision, holdId = hold, occurredAt = DateTimeOffset.UtcNow
        });
        Assert.NotEqual(pool, isolated.GetProperty("actualLotId").GetGuid());
        Assert.Equal(0m, Quantity(await s.LotAsync(isolated.GetProperty("actualLotId").GetGuid()), "availableKg"));
        Assert.Equal(0m, Quantity(await s.LotAsync((await PoolAsync(site.LocationId)).LotId), "availableKg"));
    }

    [Fact]
    public async Task Restricted_runtime_cannot_read_or_mutate_mixed_bulk_private_tables_or_helpers()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        await ArriveAsync(site, "10");
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var sql in new[]
        {
            "INSERT INTO ledger.mixed_bulk_policies DEFAULT VALUES",
            "DELETE FROM ledger.mixed_bulk_policies",
            "INSERT INTO ledger.mixed_bulk_pools(location_id) VALUES(gen_random_uuid())",
            "UPDATE ledger.mixed_bulk_pools SET revision=revision+1",
            "DELETE FROM ledger.custody_events",
            "INSERT INTO ledger.custody_events DEFAULT VALUES",
            "INSERT INTO ledger.custody_edges(event_id,input_lot_id,output_lot_id,quantity_kg) VALUES(gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),1)",
            "DELETE FROM ledger.custody_edges",
            "INSERT INTO ledger.custody_allocations(event_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg) VALUES(gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),1)",
            "DELETE FROM ledger.custody_allocations",
            "INSERT INTO ledger.custody_processing_allocations(event_id,operation_id,input_lot_id,output_lot_id,source_lot_id,quantity_kg) VALUES(gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),1)",
            "DELETE FROM ledger.custody_processing_allocations",
            "SELECT ledger.pool_ingress(gen_random_uuid(),gen_random_uuid(),'TEST',gen_random_uuid(),gen_random_uuid(),1)",
            "SELECT ledger.pool_withdraw(gen_random_uuid(),gen_random_uuid(),'TEST',gen_random_uuid(),gen_random_uuid(),1,NULL)"
        })
        {
            await using var command = new NpgsqlCommand(sql, runtime);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        var tables = new[]
        {
            "ledger.mixed_bulk_policies", "ledger.mixed_bulk_pools", "ledger.custody_events",
            "ledger.custody_edges", "ledger.custody_allocations", "ledger.custody_processing_allocations",
            "ledger.mixed_bulk_command_results"
        };
        foreach (var table in tables)
        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "REFERENCES" })
        {
            const string sql = """
                SELECT NOT has_table_privilege(current_user,$1,$2)
                   AND NOT EXISTS (
                       SELECT 1 FROM pg_attribute a
                       WHERE a.attrelid=to_regclass($1) AND a.attnum>0 AND NOT a.attisdropped
                         AND has_column_privilege(current_user,a.attrelid,a.attname,$2))
                """;
            await using var command = new NpgsqlCommand(sql, runtime);
            command.Parameters.AddWithValue(table);
            command.Parameters.AddWithValue(privilege);
            Assert.True((bool)(await command.ExecuteScalarAsync())!, $"{privilege} privilege remains on {table}");
        }
        await using var functionCheck = new NpgsqlCommand("""
            SELECT count(*) >= 4 AND bool_and(NOT has_function_privilege(current_user,p.oid,'EXECUTE'))
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='ledger' AND p.proname=ANY($1)
            """, runtime);
        functionCheck.Parameters.AddWithValue(new[] { "pool_ingress", "pool_withdraw", "command_pre_mixed", "read_before_mixed_bulk" });
        Assert.True((bool)(await functionCheck.ExecuteScalarAsync())!, "Runtime role retains protected function EXECUTE");
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM ledger.custody_events WHERE location_id=$1", site.LocationId));
    }
    [Fact]
    public async Task Runtime_validation_rejects_inherited_mixed_bulk_column_and_helper_privileges()
    {
        var group = "mixed_bulk_column_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE {group} NOLOGIN", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            await using (var grant = new NpgsqlCommand($"""
                GRANT UPDATE(revision) ON ledger.mixed_bulk_pools TO {group};
                GRANT EXECUTE ON FUNCTION ledger.pool_ingress(uuid,uuid,text,uuid,uuid,numeric) TO {group};
                GRANT {group} TO grainledger_test_runtime WITH INHERIT TRUE
                """, owner))
                await grant.ExecuteNonQueryAsync();
            await using (var effective = new NpgsqlCommand($"""
                SELECT has_column_privilege('grainledger_test_runtime','ledger.mixed_bulk_pools','revision','UPDATE')
                   AND has_function_privilege('grainledger_test_runtime',
                       'ledger.pool_ingress(uuid,uuid,text,uuid,uuid,numeric)','EXECUTE')
                """, owner))
                Assert.Equal(true, await effective.ExecuteScalarAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime"));
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand($"""
                REVOKE {group} FROM grainledger_test_runtime;
                REVOKE UPDATE(revision) ON ledger.mixed_bulk_pools FROM {group};
                REVOKE EXECUTE ON FUNCTION ledger.pool_ingress(uuid,uuid,text,uuid,uuid,numeric) FROM {group};
                DROP ROLE {group}
                """, owner);
            await cleanup.ExecuteNonQueryAsync();
        }
    }


    [Fact]
    public async Task Arrival_transaction_rolls_back_pool_and_receipt_when_posting_fails_then_same_observation_can_retry()
    {
        var site = await CreateMixedSiteAsync(); using var s = site.Scenario;
        var receipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["10"], site.LocationId));
        var request = MeasurementBody(receipt);
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var trigger = new NpgsqlCommand("CREATE FUNCTION ledger.test_mixed_bulk_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF EXISTS(SELECT 1 FROM ledger.events WHERE id=NEW.event_id AND kind='POOL_INGRESS') THEN RAISE EXCEPTION 'Test-induced mixed-bulk posting failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER test_mixed_bulk_failure BEFORE INSERT ON ledger.postings FOR EACH ROW EXECUTE FUNCTION ledger.test_mixed_bulk_failure();", owner);
            await trigger.ExecuteNonQueryAsync();
        }
        try
        {
            using var failed = await RawAsync(s.Operator, HttpMethod.Post, $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", request);
            Assert.False(failed.IsSuccessStatusCode);
        }
        finally
        {
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
            await using var remove = new NpgsqlCommand("DROP TRIGGER test_mixed_bulk_failure ON ledger.postings; DROP FUNCTION ledger.test_mixed_bulk_failure()", owner);
            await remove.ExecuteNonQueryAsync();
        }
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.custody_events WHERE location_id=$1", site.LocationId));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM ledger.lots WHERE id=$1", Lots(receipt).Single()));
        await SendAsync(s.Operator, HttpMethod.Post, $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", request);
        var (pool, _) = await PoolAsync(site.LocationId);
        Assert.Equal(10m, (await SharesAsync(pool)).Values.Sum());
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var command = new NpgsqlCommand(sql, owner); command.Parameters.AddWithValue(id);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
