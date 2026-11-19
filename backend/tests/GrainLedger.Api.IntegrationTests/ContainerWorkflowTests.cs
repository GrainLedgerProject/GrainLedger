using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Features.Identity;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ContainerWorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Route(ContainerExecutionScenario f) => $"/api/export-containers/{f.ContainerId}/workflow";
    private static string Text(decimal kg) => kg.ToString("0.000000", CultureInfo.InvariantCulture);
    private static async Task<int> Version(ContainerExecutionScenario f) => (await f.Detail()).GetProperty("container").GetProperty("version").GetInt32();
    private static Task<JsonElement> Data(ContainerExecutionScenario f) => GetAsync(f.Stock.Customer, Route(f));
    private static Task<JsonElement> Quality(ContainerExecutionScenario f, Guid lot, int version = 1, string value = "13") => SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/planning-quality-observations", new
    {
        id = Guid.NewGuid(), ownerId = f.Stock.OwnerId, facilityId = f.Stock.FacilityId, lotId = lot, attributeId = f.QualityAttributeId,
        versionNo = version, measuredValue = value, unit = "%", measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2), evidenceId = f.Stock.EvidenceId,
        reportReference = "Synthetic S2 quality", reviewedBy = "Synthetic reviewer", reviewAuthority = "Isolated container workflow test", reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1), subjectConfirmed = true
    });
    private static async Task<JsonElement> Review(ContainerExecutionScenario f, string scope = "OUTPUT_ONLY", Guid? proposal = null) =>
        await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/reviews", new ContainerScopeReviewRequest(Guid.NewGuid(), await Version(f), proposal ?? f.ProposalId, scope, "APPROVED", "Synthetic scope review against current evidence"));
    private static async Task<JsonElement> Load(ContainerExecutionScenario f, params ActualLoadLine[] lines)
    {
        var detail = (await f.Detail()).GetProperty("container"); var approval = detail.GetProperty("approval");
        return await f.Post("loads", new ActualLoadRequest(Guid.NewGuid(), detail.GetProperty("version").GetInt32(), approval.GetProperty("proposalId").GetGuid(), approval.GetProperty("decisionVersion").GetInt32(), DateTimeOffset.UtcNow, lines));
    }
    private static async Task Close(ContainerExecutionScenario f)
    {
        var metadata = await f.Post("metadata", new ContainerMetadataRequest(Guid.NewGuid(), await Version(f), "SYNTHETIC-S2", "SYNTHETIC-SEAL"));
        var readiness = (await f.Detail()).GetProperty("readiness"); Assert.True(readiness.GetProperty("assessmentStatus").GetString() == "PASS", readiness.GetRawText());
        var closed = await f.Post("close", new ContainerCloseRequest(Guid.NewGuid(), metadata.GetProperty("version").GetInt32()));
        Assert.Equal("CLOSED", closed.GetProperty("status").GetString());
    }
    private async Task<decimal> Scalar(string sql, params object[] args)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection); foreach (var value in args) command.Parameters.AddWithValue(value);
        return Convert.ToDecimal(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Partial_loading_stales_and_releases_only_unspent_quota_then_new_review_continues_to_immutable_close()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "4"));
        await Quality(f, f.LotId, 2, "13.1");
        var stale = await f.Detail(); Assert.Equal("INCOMPLETE", stale.GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        Assert.Equal("4.000000", stale.GetProperty("container").GetProperty("loadedKg").GetString());
        Assert.Equal(0m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId, f.LotId, f.LoadingLocationId));
        Assert.Equal(6m, Quantity(await f.Stock.LotAsync(f.LotId)));
        var request = new ContainerScopeReviewRequest(Guid.NewGuid(), await Version(f), f.ProposalId, "OUTPUT_ONLY", "APPROVED", "Synthetic revised evidence reviewed");
        var reviewed = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/reviews", request);
        Assert.Equal(reviewed.GetRawText(), (await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/reviews", request)).GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Customer.PostAsJsonAsync(Route(f) + "/reviews", request with { Reason = "Different payload" })).StatusCode);
        Assert.Equal(6m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId, f.LotId, f.LoadingLocationId));
        await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "6")); await Close(f);
        var dossier = await GetAsync(f.Stock.Customer, f.Route + "/dossier"); Assert.Equal(2, dossier.GetProperty("loads").GetArrayLength());
        Assert.Equal(0m, Quantity(await f.Stock.LotAsync(f.LotId)));
        Assert.Equal(1m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_scope_reviews WHERE container_id=$1 AND decision='STALE'", f.ContainerId));
    }

    [Fact]
    public async Task P1_and_P2_keep_original_load_identity_and_close_reassesses_both_versions()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "4")); await Quality(f, f.LotId, 2, "13.1"); await f.Detail();
        var container = (await f.Detail()).GetProperty("container"); var order = await GetAsync(f.Export, $"/api/export-orders/{container.GetProperty("orderId").GetGuid()}");
        var draft = await SendAsync(f.Export, HttpMethod.Post, $"/api/export-containers/{f.ContainerId}/blend-plans/drafts", new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.GetProperty("version").GetInt32(), container.GetProperty("version").GetInt32(), "HEAD_RICE"));
        Assert.Equal(6m, decimal.Parse(draft.GetProperty("targetMassKg").GetString()!, CultureInfo.InvariantCulture));
        var plan = draft.GetProperty("id").GetGuid();
        var candidates = await GetAsync(f.Export, $"/api/export-containers/{f.ContainerId}/candidates?facilityId={f.Stock.FacilityId}&planId={plan}");
        var preview = await SendAsync(f.Export, HttpMethod.Post, $"/api/blend-plans/{plan}/preview", new PreviewBlendPlanRequest(f.Stock.FacilityId, candidates.GetProperty("scopeRevision").GetString()!));
        Assert.Equal("PASS", preview.GetProperty("assessmentStatus").GetString());
        var proposal = await SendAsync(f.Export, HttpMethod.Post, $"/api/blend-plans/{plan}/propose", new ProposeBlendPlanRequest(Guid.NewGuid(), preview.GetProperty("previewReference").GetGuid()));
        var second = proposal.GetProperty("id").GetGuid(); await Review(f, "ALL_REMAINING", second);
        await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "6"));
        var readiness = (await f.Detail()).GetProperty("readiness");
        Assert.Contains(readiness.GetProperty("findings").EnumerateArray(), x => x.GetString() == "CLOSE_SCOPE_REVIEW_REQUIRED");
        // Re-review old committed material without reserving its obsolete, unused six kg again.
        await Review(f); Assert.Equal(0m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId, f.LotId, f.LoadingLocationId));
        await Close(f);
        var dossier = await GetAsync(f.Stock.Customer, f.Route + "/dossier");
        Assert.Equal(new[] { f.ProposalId, second }, dossier.GetProperty("loads").EnumerateArray().Select(x => x.GetProperty("proposal_id").GetGuid()).ToArray());
        Assert.Equal(2, dossier.GetProperty("assessment").GetProperty("reviewedScopes").GetArrayLength());
        Assert.Equal(0m, Quantity(await f.Stock.LotAsync(f.LotId)));
    }

    [Fact]
    public async Task Revised_specification_after_partial_production_requires_current_output_review_and_keeps_old_demand()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var batch = await Batch(f, "10", first: true); await Quality(f, batch.Output);
        await Review(f, "ALL_REMAINING"); await Load(f, new ActualLoadLine(batch.Output, f.Stock.SecondLocationId, "4"));
        var before = (await f.Detail()).GetProperty("container"); var orderId = before.GetProperty("orderId").GetGuid();
        using var originalResponse = await f.Export.GetAsync($"/api/export-orders/{orderId}");
        var original = await originalResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var change = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{orderId}/specifications")
        { Content = JsonContent.Create(new AddExportSpecificationRequest(Guid.NewGuid(), [new(f.QualityAttributeId, null, "13.5")])) };
        change.Headers.TryAddWithoutValidation("If-Match", originalResponse.Headers.ETag!.ToString());
        using var changed = await f.Export.SendAsync(change);
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        var current = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(original.GetProperty("specificationVersion").GetInt32()+1, current.GetProperty("specificationVersion").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await f.Detail()).GetProperty("container").GetProperty("approval").ValueKind);
        Assert.Equal(0m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId, batch.Output, f.Stock.SecondLocationId));
        await Review(f);
        await Load(f, new ActualLoadLine(batch.Output, f.Stock.SecondLocationId, "6"));
        Assert.Equal("10.000000", (await f.Detail()).GetProperty("container").GetProperty("loadedKg").GetString());
        Assert.Equal(10m, await Scalar("SELECT sum(quantity_kg) FROM grainledger.ledger_container_batches WHERE container_id=$1", f.ContainerId));
        Assert.Equal("20", (await GetAsync(f.Stock.Customer, $"/api/blend-plans/{f.PlanId}")).GetProperty("targetMassKg").GetString());
        Assert.Equal(0m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId,
            (await GetAsync(f.Stock.Customer, $"/api/export-containers/{f.ContainerId}/production")).GetProperty("inputs")[0].GetProperty("lotId").GetGuid(), f.Stock.LocationId));
    }

    private static async Task<(Guid Output, JsonElement Task)> Batch(ContainerExecutionScenario f, string kg, bool first)
    {
        var route = $"/api/export-containers/{f.ContainerId}/production";
        JsonElement task;
        if (first) task = await SendAsync(f.Export, HttpMethod.Post, route + "/request", new ProductionRequest(Guid.NewGuid(), await Version(f), f.ProposalId));
        else
        {
            var current = await GetAsync(f.Stock.Operator, route);
            task = await SendAsync(f.Stock.Operator, HttpMethod.Post, route + "/next", new ProductionCommitRequest(Guid.NewGuid(), current.GetProperty("version").GetInt32()));
        }
        var output = Guid.NewGuid(); var amount = decimal.Parse(kg, CultureInfo.InvariantCulture); var planned = decimal.Parse(task.GetProperty("plannedKg").GetString()!, CultureInfo.InvariantCulture);
        var draft = new
        {
            id = Guid.NewGuid(), ownerId = f.Stock.OwnerId, facilityId = f.Stock.FacilityId, kind = "BLENDING", occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1), equipmentId = f.Stock.EquipmentId,
            inputs = task.GetProperty("inputs").EnumerateArray().Select(x => new { lotId = x.GetProperty("lotId").GetGuid(), locationId = x.GetProperty("locationId").GetGuid(), quantityKg = Text(decimal.Parse(x.GetProperty("approvedKg").GetString()!, CultureInfo.InvariantCulture) * amount / planned) }).ToArray(),
            outputs = new[] { new { id = output, code = "", commodityId = f.Stock.CommodityId, stage = "BLENDED_RICE", productIdentity = "BLENDED_RICE", locationId = f.Stock.SecondLocationId, quantityKg = kg } }, declaredLossKg = "0"
        };
        var saved = await SendAsync(f.Stock.Operator, HttpMethod.Post, route + "/draft", new ProductionDraftRequest(Guid.NewGuid(), task.GetProperty("version").GetInt32(), JsonSerializer.SerializeToElement(draft)));
        var commit = new ProductionCommitRequest(Guid.NewGuid(), saved.GetProperty("version").GetInt32());
        var produced = await SendAsync(f.Stock.Operator, HttpMethod.Post, route + "/commit", commit);
        Assert.Equal(produced.GetRawText(), (await SendAsync(f.Stock.Operator, HttpMethod.Post, route + "/commit", commit)).GetRawText());
        return (output, produced);
    }

    [Fact]
    public async Task Two_partial_batches_and_output_only_review_never_consume_spent_inputs_again()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, production: true);
        var first = await Batch(f, "10", first: true); await Quality(f, first.Output);
        await Review(f, "ALL_REMAINING"); await Load(f, new ActualLoadLine(first.Output, f.Stock.SecondLocationId, "4.000001"));
        var second = await Batch(f, "10", first: false);
        var pending = (await f.Detail()).GetProperty("readiness"); Assert.Equal("INCOMPLETE", pending.GetProperty("assessmentStatus").GetString());
        await Quality(f, second.Output); await Review(f);
        await Load(f, new ActualLoadLine(first.Output, f.Stock.SecondLocationId, "5.999999"), new(second.Output, f.Stock.SecondLocationId, "10"));
        await Close(f);
        Assert.Equal(2m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_batches WHERE container_id=$1", f.ContainerId));
        Assert.Equal(20m, await Scalar("SELECT sum(e.quantity_kg) FROM grainledger.ledger_blend_reservation_events e WHERE e.proposal_id=$1 AND e.action='CONSUME' AND e.lot_id NOT IN (SELECT output_id FROM grainledger.ledger_container_batches WHERE container_id=$2)", f.ProposalId, f.ContainerId));
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.Equal(2, trace.GetProperty("sourceContributions").GetArrayLength());
        Assert.All(trace.GetProperty("sourceContributions").EnumerateArray(), x => Assert.Equal("10.000000", x.GetProperty("quantityKg").GetString()));
        Assert.Equal(0m, Quantity(await f.Stock.LotAsync(first.Output)) + Quantity(await f.Stock.LotAsync(second.Output)));
    }

    private static async Task<Guid> Handoff(ContainerExecutionScenario f, Guid lot, Guid location, string quantity, string kind = "TRANSFER", long? poolRevision = null)
    {
        var operation = Guid.NewGuid();
        var draft = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/handoffs", new ContainerHandoffDraftRequest(operation, await Version(f), f.ProposalId, kind,
            new(lot, location, f.Stock.SecondLocationId, quantity, DateTimeOffset.UtcNow.AddMinutes(-1), poolRevision, kind == "BAGGING" ? f.Stock.EquipmentId : null, kind == "BAGGING" ? quantity : null, kind == "BAGGING" ? "0" : null)));
        var handoff = draft.GetProperty("handoffs").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == operation);
        var approved = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/handoffs/approve", new ContainerHandoffReviewRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32(), operation, handoff.GetProperty("version").GetInt32(), "Synthetic approved INPUT handoff"));
        handoff = approved.GetProperty("handoffs").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == operation);
        var request = new ContainerHandoffCommitRequest(Guid.NewGuid(), approved.GetProperty("version").GetInt32(), operation, handoff.GetProperty("version").GetInt32());
        var committed = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/handoffs/commit", request);
        Assert.Equal(committed.GetRawText(), (await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/handoffs/commit", request)).GetRawText());
        return operation;
    }

    [Theory]
    [InlineData("TRANSFER")]
    [InlineData("BAGGING")]
    public async Task Reviewed_handoff_after_partial_loading_retains_exact_source_complement_and_needs_new_output_review(string kind)
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, preBlended: true, baggingRule: kind == "BAGGING");
        await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "4.000001"));
        var operation = await Handoff(f, f.LotId, f.LoadingLocationId, "5.999999", kind);
        var data = await Data(f); var output = data.GetProperty("proposals").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == f.ProposalId).GetProperty("context").GetProperty("outputs").EnumerateArray()
            .Single(x => x.GetProperty("locationId").GetGuid() == f.Stock.SecondLocationId && decimal.Parse(x.GetProperty("remainingKg").GetString()!, CultureInfo.InvariantCulture) > 0);
        var lot = output.GetProperty("lotId").GetGuid();
        if (kind == "TRANSFER") Assert.Equal(f.LotId, lot);
        else { Assert.NotEqual(f.LotId, lot); await Quality(f, lot); }
        Assert.Equal("INCOMPLETE", (await f.Detail()).GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        await Review(f); await Load(f, new ActualLoadLine(lot, f.Stock.SecondLocationId, "5.999999")); await Close(f);
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.Equal(10m, trace.GetProperty("sourceContributions").EnumerateArray().Sum(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture)));
        Assert.Equal(2, trace.GetProperty("sourceContributions").GetArrayLength());
        Assert.Equal(1m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_claim_transitions WHERE handoff_id=$1 AND purpose='INPUT_AUTHORIZATION'", operation));
    }

    [Fact]
    public async Task Pool_stock_is_extracted_by_assigned_operator_and_only_actual_reviewed_aliquot_can_load()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, preBlended: true, mixedStock: true);
        Assert.Equal("INCOMPLETE", (await f.Detail()).GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        var data = await Data(f); var pool = data.GetProperty("proposals")[0].GetProperty("context").GetProperty("outputs")[0];
        await Handoff(f, f.LotId, f.LoadingLocationId, "10", poolRevision: pool.GetProperty("poolRevision").GetInt64());
        data = await Data(f);
        var actual = data.GetProperty("proposals")[0].GetProperty("context").GetProperty("outputs").EnumerateArray().Single(x => x.GetProperty("locationId").GetGuid() == f.Stock.SecondLocationId);
        var lot = actual.GetProperty("lotId").GetGuid(); Assert.NotEqual(f.LotId, lot);
        await Quality(f, lot); await Review(f); await Load(f, new ActualLoadLine(lot, f.Stock.SecondLocationId, "10")); await Close(f);
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/trace", new { });
        Assert.All(trace.GetProperty("sourceContributions").EnumerateArray(), x => Assert.Equal("5.000000", x.GetProperty("quantityKg").GetString()));
        Assert.Equal(10m, await Scalar("SELECT sum(p.quantity_kg) FROM grainledger.ledger_positions p WHERE p.location_id=$1", f.LoadingLocationId));
    }

    [Fact]
    public async Task Documents_and_supplements_append_versions_without_rewriting_dossier_and_are_company_scoped()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory); await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "10")); await Close(f);
        var original = await GetAsync(f.Stock.Customer, f.Route + "/dossier");
        var bytes = Encoding.UTF8.GetBytes("Synthetic shipment document — not a legal certificate");
        var request = new ContainerDocumentRequest(Guid.NewGuid(), await Version(f), "TEST_SHIPMENT", "synthetic-document.txt", "text/plain", Convert.ToBase64String(bytes), "Synthetic first version");
        var first = await SendAsync(f.Export, HttpMethod.Post, Route(f) + "/documents", request);
        Assert.Equal(first.GetRawText(), (await SendAsync(f.Export, HttpMethod.Post, Route(f) + "/documents", request)).GetRawText());
        var second = await SendAsync(f.Export, HttpMethod.Post, Route(f) + "/documents", request with { OperationId = Guid.NewGuid(), Version = first.GetProperty("version").GetInt32(), Reason = "Synthetic second version" });
        Assert.Equal(new[] { 1, 2 }, second.GetProperty("documents").EnumerateArray().Select(x => x.GetProperty("version").GetInt32()).ToArray());
        var path = Route(f) + $"/documents/{request.OperationId}/file";
        Assert.Equal(bytes, await f.Export.GetByteArrayAsync(path));
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Stock.ForeignCustomer.GetAsync(path)).StatusCode);
        var supplemented = await SendAsync(f.Export, HttpMethod.Post, Route(f) + "/supplements", new ContainerSupplementRequest(Guid.NewGuid(), second.GetProperty("version").GetInt32(), f.Stock.EvidenceId, "Synthetic later evidence"));
        Assert.Single(supplemented.GetProperty("supplements").EnumerateArray());
        Assert.Equal(original.GetRawText(), (await GetAsync(f.Stock.Customer, f.Route + "/dossier")).GetRawText());
        Assert.Equal("CLOSED", (await f.Detail()).GetProperty("container").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Export.PostAsJsonAsync(Route(f) + "/documents", request with { OperationId = Guid.NewGuid(), Version = supplemented.GetProperty("version").GetInt32(), Filename = "../bad.txt" })).StatusCode);
    }

    [Fact]
    public async Task Hold_notices_are_unique_retained_scoped_and_handling_never_releases_restrictions()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory); await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "4"));
        var request = new ContainerHoldRequest(Guid.NewGuid(), await Version(f), "Synthetic S2 HOLD");
        var held = await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/holds", request);
        Assert.Equal(held.GetRawText(), (await SendAsync(f.Stock.Customer, HttpMethod.Post, f.Route + "/holds", request)).GetRawText());
        var notices = await GetAsync(f.Export, "/api/container-notifications"); var notice = Assert.Single(notices.GetProperty("items").EnumerateArray());
        Assert.Equal(f.ContainerId, notice.GetProperty("containerId").GetGuid()); Assert.Equal(JsonValueKind.Null, notice.GetProperty("readAt").ValueKind);
        await SendAsync(f.Export, HttpMethod.Post, Route(f) + "/notifications/read", new ContainerNoticeReadRequest(Guid.NewGuid(), await Version(f), notice.GetProperty("id").GetGuid()));
        Assert.Equal(JsonValueKind.String, (await GetAsync(f.Export, "/api/container-notifications")).GetProperty("items")[0].GetProperty("readAt").ValueKind);
        Assert.Empty((await GetAsync(f.Stock.ForeignCustomer, "/api/container-notifications")).GetProperty("items").EnumerateArray());
        await SendAsync(f.Stock.Manager, HttpMethod.Post, Route(f) + "/hold-responses", new ContainerHoldResponseRequest(Guid.NewGuid(), await Version(f), request.OperationId, f.Stock.FacilityId, "RECORD_RESPONSE", f.Stock.EvidenceId, "Synthetic investigation recorded"));
        Assert.Equal("INCOMPLETE", (await f.Detail()).GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        Assert.Equal(4m, await Scalar("SELECT sum(l.quantity_kg) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=$1", f.ContainerId));
        Assert.Equal(1m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_holds WHERE container_id=$1", f.ContainerId));
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Stock.Manager.PostAsJsonAsync(Route(f) + "/hold-responses", new ContainerHoldResponseRequest(Guid.NewGuid(), await Version(f), request.OperationId, f.Stock.FacilityId, "RELEASE", f.Stock.EvidenceId, "Unsupported"))).StatusCode);
    }

    [Fact]
    public async Task Workflow_denies_wrong_roles_foreign_company_unknown_fields_and_direct_private_access()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var request = new ContainerScopeReviewRequest(Guid.NewGuid(), await Version(f), f.ProposalId, "OUTPUT_ONLY", "APPROVED", "Synthetic review");
        foreach (var client in new[] { f.Export, f.Stock.Operator, f.Stock.Manager, f.Stock.Admin, f.Stock.Purchaser, f.Stock.ForeignCustomer })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Route(f) + "/reviews", request)).StatusCode);
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, f.Stock.ForeignOwnerId); using var other = factory.CreateRealCookieClient(user, session); await CsrfAsync(other);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(Route(f))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Stock.Customer.PostAsJsonAsync(Route(f) + "/reviews", new { request.OperationId, request.Version, request.ProposalId, request.Scope, request.Decision, request.Reason, approved = true })).StatusCode);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT has_function_privilege($1,'grainledger.ledger_container_review_context(uuid)','EXECUTE') OR has_table_privilege($1,'grainledger.ledger_container_scope_reviews','SELECT,INSERT,UPDATE,DELETE')", owner);
        check.Parameters.AddWithValue(new NpgsqlConnectionStringBuilder(factory.RuntimeConnectionString).Username!);
        Assert.False((bool)(await check.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("LOAD", "MRL")]
    [InlineData("CLOSE", "MRL")]
    [InlineData("LOAD", "POLICY")]
    [InlineData("CLOSE", "POLICY")]
    public async Task Legal_or_policy_expiry_at_last_write_rolls_back_the_entire_load_or_close(string phase, string dependency)
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        if (phase == "CLOSE")
        {
            await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, "10"));
            await f.Post("metadata", new ContainerMetadataRequest(Guid.NewGuid(), await Version(f), "SYNTHETIC-EXPIRY", "SYNTHETIC-SEAL"));
        }
        var version = await Version(f); var trigger = "container_expiry_" + Guid.NewGuid().ToString("N");
        var table = dependency == "MRL" ? "catalog_mrl_versions" : "catalog_blend_assessment_policies";
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        // Only this disposable class database: let the fault hook advance the retained window
        // so the failure proves the final scientific recheck, not the catalog immutability trigger.
        await using var allowExpiry = new NpgsqlCommand($"ALTER TABLE grainledger.{table} DISABLE TRIGGER USER", owner);
        await allowExpiry.ExecuteNonQueryAsync();
        await using var install = new NpgsqlCommand($"""
            CREATE FUNCTION grainledger.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            IF NEW.container_id='{f.ContainerId}'::uuid AND NEW.kind='{phase}' THEN
              UPDATE grainledger.{table} SET effective_to=clock_timestamp()-interval '1 second'
              WHERE market_id=(SELECT market_id FROM grainledger.exports_export_orders WHERE id=(SELECT order_id FROM grainledger.exports_containers WHERE id=NEW.container_id));
            END IF; RETURN NEW; END $$;
            CREATE TRIGGER {trigger} AFTER INSERT ON grainledger.ledger_container_execution_events FOR EACH ROW EXECUTE FUNCTION grainledger.{trigger}();
            """, owner);
        await install.ExecuteNonQueryAsync();
        try
        {
            var response = phase == "LOAD" ? await f.Export.PostAsJsonAsync(f.Route + "/loads", f.Load(version, "4"))
                : await f.Export.PostAsJsonAsync(f.Route + "/close", new ContainerCloseRequest(Guid.NewGuid(), version));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(phase == "LOAD" ? 0m : 10m, await Scalar("SELECT coalesce(sum(l.quantity_kg),0) FROM grainledger.ledger_container_load_lines l JOIN grainledger.ledger_container_loads h ON h.id=l.load_id WHERE h.container_id=$1", f.ContainerId));
            Assert.Equal(phase == "LOAD" ? 10m : 0m, Quantity(await f.Stock.LotAsync(f.LotId)));
            Assert.Equal(0m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_closures WHERE container_id=$1", f.ContainerId));
            Assert.Equal(version, (await f.Detail()).GetProperty("container").GetProperty("version").GetInt32());
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER {trigger} ON grainledger.ledger_container_execution_events; DROP FUNCTION grainledger.{trigger}();", owner);
            await remove.ExecuteNonQueryAsync();
            await using var restore = new NpgsqlCommand($"ALTER TABLE grainledger.{table} ENABLE TRIGGER USER", owner);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Policy_expiry_returns_readiness_and_releases_only_unspent_authority(bool fullyLoaded)
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        await Load(f, new ActualLoadLine(f.LotId, f.LoadingLocationId, fullyLoaded ? "10" : "4"));
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var expire = new NpgsqlCommand("""
            ALTER TABLE grainledger.catalog_blend_assessment_policies DISABLE TRIGGER USER;
            UPDATE grainledger.catalog_blend_assessment_policies SET effective_to=clock_timestamp()-interval '1 second'
            WHERE market_id=(SELECT market_id FROM grainledger.exports_export_orders WHERE id=(SELECT order_id FROM grainledger.exports_containers WHERE id=$1));
            ALTER TABLE grainledger.catalog_blend_assessment_policies ENABLE TRIGGER USER;
            """, owner);
        // Separate parameterized statement from DDL, as Npgsql positional parameters prepare one statement.
        var sql = expire.CommandText; var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var statement in statements)
        {
            expire.CommandText = statement; expire.Parameters.Clear();
            if (statement.Contains("$1", StringComparison.Ordinal)) expire.Parameters.AddWithValue(f.ContainerId);
            await expire.ExecuteNonQueryAsync();
        }
        var detail = await f.Detail();
        Assert.Equal("INCOMPLETE", detail.GetProperty("readiness").GetProperty("assessmentStatus").GetString());
        Assert.Contains(detail.GetProperty("readiness").GetProperty("findings").EnumerateArray(), x => x.GetString() == "ASSESSMENT_POLICY_MISSING");
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("container").GetProperty("approval").ValueKind);
        Assert.Equal(0m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId, f.LotId, f.LoadingLocationId));
        Assert.Equal(fullyLoaded ? 0m : 6m, Quantity(await f.Stock.LotAsync(f.LotId)));
        Assert.Equal(fullyLoaded ? "10.000000" : "4.000000", detail.GetProperty("container").GetProperty("loadedKg").GetString());
        Assert.Equal(0m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_closures WHERE container_id=$1", f.ContainerId));
    }

    [Fact]
    public async Task Failed_handoff_after_posting_rolls_back_stock_reservations_claims_and_receipt()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory, preBlended: true);
        var operation = Guid.NewGuid();
        var draft = await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/handoffs", new ContainerHandoffDraftRequest(operation, await Version(f), f.ProposalId, "TRANSFER",
            new(f.LotId, f.LoadingLocationId, f.Stock.SecondLocationId, "10", DateTimeOffset.UtcNow.AddMinutes(-1))));
        var approved = await SendAsync(f.Stock.Customer, HttpMethod.Post, Route(f) + "/handoffs/approve", new ContainerHandoffReviewRequest(Guid.NewGuid(), draft.GetProperty("version").GetInt32(), operation, 1, "Synthetic handoff approval"));
        var version = approved.GetProperty("version").GetInt32();
        var trigger = "handoff_fault_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString); await owner.OpenAsync();
        await using var install = new NpgsqlCommand($"""
            CREATE FUNCTION grainledger.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
            IF NEW.container_id='{f.ContainerId}'::uuid THEN RAISE EXCEPTION 'Synthetic handoff rollback' USING ERRCODE='23514'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER {trigger} BEFORE INSERT ON grainledger.ledger_container_output_claims FOR EACH ROW EXECUTE FUNCTION grainledger.{trigger}();
            """, owner);
        await install.ExecuteNonQueryAsync();
        var request = new ContainerHandoffCommitRequest(Guid.NewGuid(), version, operation, 2);
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await f.Stock.Operator.PostAsJsonAsync(Route(f) + "/handoffs/commit", request)).StatusCode);
            Assert.Equal(20m, Quantity(await f.Stock.LotAsync(f.LotId)));
            Assert.Equal(10m, await Scalar("SELECT grainledger.ledger_container_outstanding($1,$2,$3)", f.ProposalId, f.LotId, f.LoadingLocationId));
            Assert.Equal(0m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_claim_transitions WHERE handoff_id=$1", operation));
            Assert.Equal(0m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_workflow_requests WHERE actor_id=$1 AND operation_id=$2", f.Stock.Accounts["operator"].User.Id, request.OperationId));
        }
        finally
        {
            await using var remove = new NpgsqlCommand($"DROP TRIGGER {trigger} ON grainledger.ledger_container_output_claims; DROP FUNCTION grainledger.{trigger}();", owner);
            await remove.ExecuteNonQueryAsync();
        }
        await SendAsync(f.Stock.Operator, HttpMethod.Post, Route(f) + "/handoffs/commit", request);
        Assert.Equal(10m, await Scalar("SELECT quantity_kg FROM grainledger.ledger_positions WHERE lot_id=$1 AND location_id=$2", f.LotId, f.Stock.SecondLocationId));
    }

    [Fact]
    public async Task Retained_actual_loading_without_capture_provenance_reports_incomplete_without_losing_source_kg()
    {
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        var legacy = Guid.NewGuid();
        var containers = await ContainerRetainedTraceFixture.SeedAsync(factory, f, legacy, 10m, 1,
            JsonSerializer.SerializeToElement(new { assessmentStatus = "INCOMPLETE", legacySyntheticHistory = true }), legacyWithoutCapture: true);
        var trace = await SendAsync(f.Stock.Customer, HttpMethod.Post, $"/api/export-containers/{containers[0]}/execution/trace", new { });
        Assert.False(trace.GetProperty("complete").GetBoolean()); Assert.Equal("10.000000", trace.GetProperty("loadedKg").GetString());
        var source = Assert.Single(trace.GetProperty("sourceContributions").EnumerateArray());
        Assert.Equal(legacy, source.GetProperty("sourceLotId").GetGuid()); Assert.Equal("10.000000", source.GetProperty("quantityKg").GetString());
        Assert.Equal("SOURCE_PROVENANCE_UNAVAILABLE", source.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, source.GetProperty("provenance").ValueKind);
        // The valid original source/approval was not changed to manufacture this missing-data case.
        Assert.Equal(10m, Quantity(await f.Stock.LotAsync(f.LotId))); Assert.Equal(0m, await Scalar("SELECT count(*) FROM grainledger.ledger_container_loads WHERE container_id=$1", f.ContainerId));
    }
}
