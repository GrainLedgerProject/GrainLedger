using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Api.Features.Exports;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ContainerProductionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Route(ContainerExecutionScenario f) => $"/api/export-containers/{f.ContainerId}/production";
    private static async Task<JsonElement> Request(ContainerExecutionScenario f) =>
        await SendAsync(f.Export, HttpMethod.Post, Route(f) + "/request", new ProductionRequest(Guid.NewGuid(), 1, f.ProposalId));
    private static async Task<(JsonElement Task, Guid Output)> Draft(ContainerExecutionScenario f, JsonElement task)
    {
        var output = Guid.NewGuid();
        var draft = new
        {
            id = Guid.NewGuid(),
            ownerId = f.Stock.OwnerId,
            facilityId = f.Stock.FacilityId,
            kind = "BLENDING",
            occurredAt = DateTimeOffset.UtcNow,
            equipmentId = f.Stock.EquipmentId,
            inputs = task.GetProperty("inputs").EnumerateArray().Select(x => new
            {
                lotId = x.GetProperty("lotId").GetGuid(),
                locationId = x.GetProperty("locationId").GetGuid(),
                quantityKg = x.GetProperty("quantityKg").GetString()
            }).ToArray(),
            outputs = new[] { new { id = output, code = "", commodityId = f.Stock.CommodityId, stage = "BLENDED_RICE", productIdentity = "BLENDED_RICE", locationId = f.Stock.SecondLocationId, quantityKg = "20" } },
            declaredLossKg = "0"
        };
        var result = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/draft", new ProductionDraftRequest(Guid.NewGuid(), task.GetProperty("version").GetInt32(), JsonSerializer.SerializeToElement(draft)));
        return (result, output);
    }
    private static async Task Quality(ContainerExecutionScenario f, Guid output, string value = "13", int version = 1) =>
        await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/planning-quality-observations", new
        {
            id = Guid.NewGuid(),
            ownerId = f.Stock.OwnerId,
            facilityId = f.Stock.FacilityId,
            lotId = output,
            attributeId = f.QualityAttributeId,
            versionNo = version,
            measuredValue = value,
            unit = "%",
            measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            evidenceId = f.Stock.EvidenceId,
            reportReference = "Synthetic output quality",
            reviewedBy = "Synthetic reviewer",
            reviewAuthority = "Isolated test",
            reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            subjectConfirmed = true
        });

    private async Task AssertNoOutput(Guid output)
    {
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT count(*) FROM grainledger.ledger_lots WHERE id=$1", owner);
        query.Parameters.AddWithValue(output);
        Assert.Equal(0L, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Approved_production_reviews_actual_output_then_loads_and_closes_without_double_consumption()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        var commit = new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32());
        var produced = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/commit", commit);
        Assert.Equal("PRODUCED", produced.GetProperty("state").GetString());
        Assert.Equal(produced.GetRawText(), (await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/commit", commit)).GetRawText());
        var detail = await f.Detail(); Assert.Equal("PLANNED", detail.GetProperty("container").GetProperty("status").GetString());
        Assert.Equal("0.000000", detail.GetProperty("container").GetProperty("loadedKg").GetString());
        foreach (var input in request.GetProperty("inputs").EnumerateArray())
            Assert.Equal(0m, Quantity(await f.Stock.LotAsync(input.GetProperty("lotId").GetGuid())));
        Assert.Equal(20m, Quantity(await f.Stock.LotAsync(output)));
        var review = new ProductionReviewRequest(Guid.NewGuid(), produced.GetProperty("version").GetInt32(), "Synthetic actual output review");
        Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Customer.PostAsJsonAsync(Route(f) + "/approve", review)).StatusCode);
        await Quality(f, output);
        var approved = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/approve", review);
        Assert.Equal("APPROVED", approved.GetProperty("state").GetString());
        var d = await f.Detail(); Assert.True(d.GetProperty("readiness").GetProperty("assessmentStatus").GetString() == "PASS", d.GetRawText());
        var allocations = d.GetProperty("container").GetProperty("approval").GetProperty("allocations");
        Assert.Equal(output, Assert.Single(allocations.EnumerateArray()).GetProperty("lotId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Export.PostAsJsonAsync(f.Route + "/loads", new ActualLoadRequest(Guid.NewGuid(), 1, f.ProposalId, 1, DateTimeOffset.UtcNow, [new(output, f.Stock.SecondLocationId, "1")]))).StatusCode);
        var load = new ActualLoadRequest(Guid.NewGuid(), approved.GetProperty("containerVersion").GetInt32(), f.ProposalId, 1, DateTimeOffset.UtcNow, [new(output, f.Stock.SecondLocationId, "4.000001")]);
        var loaded = await f.Post("loads", load);
        var full = await f.Post("loads", new ActualLoadRequest(Guid.NewGuid(), loaded.GetProperty("version").GetInt32(), f.ProposalId, 1, DateTimeOffset.UtcNow, [new(output, f.Stock.SecondLocationId, "15.999999")]));
        Assert.Equal("20.000000", full.GetProperty("loadedKg").GetString());
        var metadata = await f.Post("metadata", new ContainerMetadataRequest(Guid.NewGuid(), full.GetProperty("version").GetInt32(), "SYNTHETIC-PRODUCED", "SEAL-PRODUCED"));
        var closed = await f.Post("close", new ContainerCloseRequest(Guid.NewGuid(), metadata.GetProperty("version").GetInt32()));
        Assert.Equal("CLOSED", closed.GetProperty("status").GetString()); Assert.Equal(0m, Quantity(await f.Stock.LotAsync(output)));
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        var sources = trace.GetProperty("sourceContributions").EnumerateArray().ToArray(); Assert.Equal(2, sources.Length);
        Assert.All(sources, s => Assert.Equal("10.000000", s.GetProperty("quantityKg").GetString()));
        var dossier = await GetAsync(f.Stock.Customer, f.Route + "/dossier");
        Assert.Equal(produced.GetProperty("operationId").GetGuid(), dossier.GetProperty("assessment").GetProperty("outputAuthorization").GetProperty("operationId").GetGuid());

    }

    [Fact]
    public async Task Production_authority_quota_and_output_rejection_preserve_stock_and_history()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        var commit = new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Export.PostAsJsonAsync(Route(f) + "/commit", commit)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Stock.ForeignCustomer.GetAsync(Route(f))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Stock.Admin.GetAsync(Route(f))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/approve", new ProductionReviewRequest(Guid.NewGuid(), 2, "Spoofed review"))).StatusCode);
        var produced = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/commit", commit);
        var rejected = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/reject", new ProductionReviewRequest(Guid.NewGuid(), produced.GetProperty("version").GetInt32(), "Synthetic rejection"));
        Assert.Equal("REJECTED", rejected.GetProperty("state").GetString()); Assert.Equal(20m, Quantity(await f.Stock.LotAsync(output)));
        await Quality(f, output);
        var approved = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/approve", new ProductionReviewRequest(Guid.NewGuid(), rejected.GetProperty("version").GetInt32(), "Synthetic re-review before loading"));
        Assert.Equal("APPROVED", approved.GetProperty("state").GetString());
        Assert.Equal("PASS", (await f.Detail()).GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Customer.PostAsJsonAsync($"/api/blend-plans/{f.PlanId}/revoke", new RevokeBlendPlanRequest(Guid.NewGuid(), f.ProposalId, 1, "Cannot revoke actual production"))).StatusCode);
        Assert.Equal(20m, Quantity(await f.Stock.LotAsync(output)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_input_consume_or_output_creation_rolls_back_the_entire_execution(bool afterOutput)
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        var trigger = "production_fault_" + Guid.NewGuid().ToString("N");
        var table = afterOutput ? "ledger_blend_reservations" : "ledger_postings";
        var lot = afterOutput ? output : request.GetProperty("inputs")[0].GetProperty("lotId").GetGuid();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using (var fault = new NpgsqlCommand($"CREATE FUNCTION grainledger.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.lot_id='{lot}'::uuid THEN RAISE EXCEPTION 'Synthetic production fault'; END IF; RETURN NEW; END $$; CREATE TRIGGER {trigger} BEFORE INSERT ON grainledger.{table} FOR EACH ROW EXECUTE FUNCTION grainledger.{trigger}();", owner)) await fault.ExecuteNonQueryAsync();
        var commit = new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32());
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/commit", commit)).StatusCode);
            foreach (var input in request.GetProperty("inputs").EnumerateArray()) Assert.Equal(10m, Quantity(await f.Stock.LotAsync(input.GetProperty("lotId").GetGuid())));
            await AssertNoOutput(output);
            Assert.Equal("DRAFT", (await GetAsync(f.Stock.Operator, Route(f))).GetProperty("state").GetString());
            var decision = await GetAsync(f.Export, $"/api/blend-plans/{f.PlanId}/decision"); Assert.Equal("0", decision.GetProperty("reservation").GetProperty("consumedKg").GetString());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP TRIGGER {trigger} ON grainledger.{table}; DROP FUNCTION grainledger.{trigger}();", owner); await drop.ExecuteNonQueryAsync();
        }
        var produced = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/commit", commit);
        Assert.Equal("PRODUCED", produced.GetProperty("state").GetString()); Assert.Equal(20m, Quantity(await f.Stock.LotAsync(output)));
    }

    [Fact]
    public async Task Concurrent_production_has_one_winner_and_stale_output_evidence_blocks_after_partial_loading()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        var replies = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => f.Stock.Operator.PostAsJsonAsync(Route(f) + "/commit", new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32()))));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        var task = await GetAsync(f.Stock.Customer, Route(f)); await Quality(f, output);
        var review = new ProductionReviewRequest(Guid.NewGuid(), task.GetProperty("version").GetInt32(), "Synthetic current output review");
        var approved = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/approve", review);
        Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Customer.PostAsJsonAsync(Route(f) + "/reject", review)).StatusCode);
        var load = await f.Post("loads", new ActualLoadRequest(Guid.NewGuid(), approved.GetProperty("containerVersion").GetInt32(), f.ProposalId, 1, DateTimeOffset.UtcNow, [new(output, f.Stock.SecondLocationId, "4.000001")]));
        await Quality(f, output, "13.1", 2);
        var refused = await f.Export.PostAsJsonAsync(f.Route + "/loads", new ActualLoadRequest(Guid.NewGuid(), load.GetProperty("version").GetInt32(), f.ProposalId, 1, DateTimeOffset.UtcNow, [new(output, f.Stock.SecondLocationId, "1")]));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("APPROVAL_EVIDENCE_CHANGED", await refused.Content.ReadAsStringAsync());
        Assert.Equal(15.999999m, Quantity(await f.Stock.LotAsync(output)));
        Assert.Equal("4.000001", (await f.Detail()).GetProperty("container").GetProperty("loadedKg").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Customer.PostAsJsonAsync(Route(f) + "/approve", new ProductionReviewRequest(Guid.NewGuid(), approved.GetProperty("version").GetInt32(), "Partial revalidation deferred"))).StatusCode);
    }

    [Fact]
    public async Task Draft_cannot_substitute_inputs_or_output_mass_and_approval_freshness_is_checked_at_production()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        var operation = draft.GetProperty("operation");
        var altered = JsonNode.Parse(operation.GetRawText())!.AsObject();
        foreach (var key in new[] { "status", "confirmedVersion", "confirmedBy", "varianceKg", "allowedVarianceKg", "correctionId" }) altered.Remove(key);
        altered["outputs"]![0]!["quantityKg"] = "19";
        var invalid = await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/draft", new ProductionDraftRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32(), JsonSerializer.SerializeToElement(altered)));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        altered["outputs"]![0]!["quantityKg"] = 20;
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/draft", new ProductionDraftRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32(), JsonSerializer.SerializeToElement(altered)))).StatusCode);
        altered["outputs"]![0]!["quantityKg"] = "20";
        altered["declaredLossKg"] = 0;
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/draft", new ProductionDraftRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32(), JsonSerializer.SerializeToElement(altered)))).StatusCode);
        altered["declaredLossKg"] = "0";
        altered["outputs"]![0]!["quantityKg"] = "20"; altered["inputs"]![0]!["quantityKg"] = "9";
        Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/draft", new ProductionDraftRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32(), JsonSerializer.SerializeToElement(altered)))).StatusCode);
        var input = request.GetProperty("inputs")[0].GetProperty("lotId").GetGuid(); await Quality(f, input, "13.1", 2);
        var refused = await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/commit", new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode); Assert.Contains("APPROVAL_EVIDENCE_CHANGED", await refused.Content.ReadAsStringAsync());
        Assert.Equal(10m, Quantity(await f.Stock.LotAsync(input))); await AssertNoOutput(output);
    }
    [Fact]
    public async Task Revoked_operator_waiting_for_stock_lock_cannot_commit_production()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var tx = await owner.BeginTransactionAsync();
        await using (var locked = new NpgsqlCommand("SELECT grainledger.ledger_lock_writes()", owner, tx)) await locked.ExecuteNonQueryAsync();
        var pending = f.Stock.Operator.PostAsJsonAsync(Route(f) + "/commit", new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32()));
        await using var observer = new NpgsqlConnection(factory.OwnerConnectionString); await observer.OpenAsync();
        var waiting = false;
        for (var i = 0; i < 200; i++)
        {
            await using var probe = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE '%pg_advisory_xact_lock(714928352)%' AND wait_event='advisory')", observer);
            if (Equals(await probe.ExecuteScalarAsync(), true)) { waiting = true; break; }
            await Task.Delay(20);
        }
        Assert.True(waiting);
        await using (var revoke = new NpgsqlCommand("UPDATE grainledger.identity_users SET \"Role\"='MillManager' WHERE \"Id\"=$1", observer))
        { revoke.Parameters.AddWithValue(f.Stock.Accounts["operator"].User.Id); await revoke.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await pending).StatusCode);
        await AssertNoOutput(output);
        foreach (var input in request.GetProperty("inputs").EnumerateArray()) Assert.Equal(10m, Quantity(await f.Stock.LotAsync(input.GetProperty("lotId").GetGuid())));
    }

    [Fact]
    public async Task Legal_expiry_at_final_write_rolls_back_production_and_private_helpers_stay_denied()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var request = await Request(f); var (draft, output) = await Draft(f, request);
        var trigger = "production_expiry_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        // Deterministic fault at the last write, rather than sleeping for an arbitrary wall-clock boundary.
        await using (var fault = new NpgsqlCommand($"CREATE FUNCTION grainledger.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.container_id='{f.ContainerId}'::uuid AND NEW.action='commit' THEN UPDATE grainledger.catalog_mrl_versions SET effective_to=clock_timestamp()-interval '1 second' WHERE market_id=(SELECT market_id FROM grainledger.exports_export_orders WHERE id=(SELECT order_id FROM grainledger.exports_containers WHERE id=NEW.container_id)); END IF; RETURN NEW; END $$; CREATE TRIGGER {trigger} BEFORE INSERT ON grainledger.ledger_container_production_events FOR EACH ROW EXECUTE FUNCTION grainledger.{trigger}();", owner)) await fault.ExecuteNonQueryAsync();
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/commit", new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32()))).StatusCode);
            await AssertNoOutput(output);
            Assert.Equal("DRAFT", (await GetAsync(f.Stock.Customer, Route(f))).GetProperty("state").GetString());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP TRIGGER {trigger} ON grainledger.ledger_container_production_events; DROP FUNCTION grainledger.{trigger}();", owner); await drop.ExecuteNonQueryAsync();
        }
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var sql in new[] { "SELECT grainledger.ledger_production_assert_basis('{}'::jsonb,gen_random_uuid())", "SELECT * FROM grainledger.ledger_container_output_reviews" })
        {
            await using var denied = new NpgsqlCommand(sql, runtime);
            var error = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
        }
        var produced = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/commit", new ProductionCommitRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32()));
        Assert.Equal("PRODUCED", produced.GetProperty("state").GetString());
    }
}
