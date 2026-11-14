using GrainLedger.Api.Data;
using System.Data;
using System.Security.Claims;
using GrainLedger.Api.Features.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Features.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BlendPlanProposalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Confirmed_evidence_can_propose_and_approve_with_exact_reservation(int flow)
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(scenario);
        var receipt = await scenario.ReceiptAsync("20");
        var sample = await SampleAsync(scenario, receipt);
        var (_, report) = await f.ReportAsync(sample);
        var profile = f.RuleBody();
        ResidueScenario.AddStep(profile, "DRYING", "RAW_PADDY", "DRIED_PADDY");
        ResidueScenario.AddStep(profile, "MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5", "0.5");
        ResidueScenario.AddStep(profile, "MILLING", "DRIED_PADDY", "BROKEN_RICE", "0.5", "0.5");
        ResidueScenario.AddStep(profile, "BLENDING", "HEAD_RICE", "BLENDED_RICE");
        ResidueScenario.AddStep(profile, "BLENDING", "BROKEN_RICE", "BLENDED_RICE");
        await f.PublishRuleAsync(profile);
        await f.SelectAsync(sample, report);
        var drying = await scenario.DraftAsync("DRYING", [(sample.LotId, "20")],
            [("DRIED_PADDY", "20")]);
        await scenario.CommitAsync(drying);
        var milling = await scenario.DraftAsync("MILLING",
            [(LedgerScenario.Output(drying), "20")],
            [("HEAD_RICE", "10"), ("BROKEN_RICE", "10")]);
        await scenario.CommitAsync(milling);
        var head = LedgerScenario.Output(milling, 0);
        var marketId = Guid.NewGuid();
        var attributeId = Guid.NewGuid();
        var mrlId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Markets.Add(new Market { Id = marketId,
                Code = "M" + marketId.ToString("N"), Name = "Synthetic market" });
            db.QualityAttributes.Add(new QualityAttribute { Id = attributeId,
                Code = "Q" + attributeId.ToString("N"), Name = "Synthetic moisture", Unit = "%" });
            db.MrlVersions.Add(new MrlVersion { Id = mrlId, MarketId = marketId,
                CommodityId = scenario.CatalogCommodityId, VersionNo = 1,
                Status = "PUBLISHED", SourceRef = "Synthetic legal source",
                PublishedBy = scenario.Accounts["customer"].User.Id,
                PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-2) });
            await db.SaveChangesAsync();
            db.MrlLimits.Add(new MrlLimit { Id = Guid.NewGuid(), MrlVersionId = mrlId,
                SubstanceId = f.SubstanceId, LimitValue = 1m, Unit = "mg/kg" });
            await db.SaveChangesAsync();
        }
        var quality = await scenario.Customer.PostAsJsonAsync(
            "/api/ledger/planning-quality-observations", new
            {
                id = Guid.NewGuid(), ownerId = scenario.OwnerId,
                facilityId = scenario.FacilityId, lotId = head, attributeId,
                versionNo = 1, measuredValue = "13", unit = "%",
                measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                evidenceId = scenario.EvidenceId, reportReference = "SYNTHETIC-QC-1",
                reviewedBy = "Synthetic reviewer", reviewAuthority = "Synthetic authority",
                reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1), subjectConfirmed = true
            });
        Assert.Equal(HttpStatusCode.OK, quality.StatusCode);
        var policy = await scenario.Customer.PostAsJsonAsync("/api/blend-assessment-policies",
            new PublishBlendAssessmentPolicyRequest(Guid.NewGuid(), marketId,
                scenario.CatalogCommodityId, 1, DateTimeOffset.UtcNow.AddMinutes(-1),
                null, [f.SubstanceId], "0.1", "AS_RECEIVED",
                [new PublishedQualityPropagationRule(attributeId, "%", "MASS_WEIGHTED_MEAN")],
                "Synthetic approved policy", "Synthetic reviewer",
                "Synthetic authority", DateTimeOffset.UtcNow.AddMinutes(-2)));
        Assert.Equal(HttpStatusCode.Created, policy.StatusCode);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(staff, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN",
            (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf"))
                .GetProperty("token").GetString());
        var orderResponse = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId,
            commodityId = scenario.CatalogCommodityId, requiredMass = "10", unit = "KG",
            shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = (await orderResponse.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{order.Id}/containers"))!.Items);
        var draftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{container.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version,
                container.Version, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var candidatesUrl = $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}&planId={draft.Id}";
        var before = await writer.GetFromJsonAsync<JsonElement>(candidatesUrl);
        var previewResponse = await writer.PostAsJsonAsync($"/api/blend-plans/{draft.Id}/preview",
            new PreviewBlendPlanRequest(scenario.FacilityId,
                before.GetProperty("scopeRevision").GetString()!, 8));
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PASS", preview.GetProperty("assessmentStatus").GetString());
        var reference = preview.GetProperty("previewReference").GetGuid();
        async Task<(long Positions, decimal Mass, long Postings, long Holds)> StockAsync()
        {
            await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM grainledger.ledger_positions),
                       (SELECT coalesce(sum(quantity_kg), 0) FROM grainledger.ledger_positions),
                       (SELECT count(*) FROM grainledger.ledger_postings),
                       (SELECT count(*) FROM grainledger.ledger_holds)
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetInt64(0), reader.GetDecimal(1),
                reader.GetInt64(2), reader.GetInt64(3));
        }
        var stockBeforeProposal = await StockAsync();
        var proposeBody = new ProposeBlendPlanRequest(Guid.NewGuid(), reference);
        var proposeResponse = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{draft.Id}/propose", proposeBody);
        Assert.Equal(HttpStatusCode.Created, proposeResponse.StatusCode);
        var proposal = await proposeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var proposedDraft = await writer.GetFromJsonAsync<BlendPlanDraftDto>(
            $"/api/blend-plans/{draft.Id}");
        Assert.True(proposedDraft!.HasProposal);
        Assert.False(proposedDraft.CanCancel);
        var assessment = proposal.GetProperty("assessment");
        Assert.Equal("F14_ASSESSMENT_V1",
            assessment.GetProperty("calculationVersion").GetString());
        Assert.Equal(mrlId, assessment.GetProperty("evidence")
            .GetProperty("applicableMrl").GetProperty("versionId").GetGuid());
        Assert.Equal(1, Assert.Single(assessment.GetProperty("evidence")
            .GetProperty("qualityObservations").EnumerateArray().ToArray())
            .GetProperty("evidenceVersion").GetInt32());
        var retry = await writer.PostAsJsonAsync($"/api/blend-plans/{draft.Id}/propose",
            proposeBody);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(proposal.GetProperty("id").GetGuid(),
            (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(stockBeforeProposal, await StockAsync());
        var after = await writer.GetFromJsonAsync<JsonElement>(candidatesUrl);
        Assert.Equal(before.GetProperty("scopeRevision").GetString(),
            after.GetProperty("scopeRevision").GetString());
        var saved = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/blend-plans/{draft.Id}/proposal");
        Assert.Equal(proposal.GetProperty("id").GetGuid(), saved.GetProperty("id").GetGuid());
        Assert.False(saved.GetProperty("stockReserved").GetBoolean());
        Assert.Equal("CURRENT", saved.GetProperty("freshness").GetString());
        Assert.Equal(HttpStatusCode.OK, (await scenario.Customer.GetAsync(
            $"/api/blend-plans/{draft.Id}/proposal")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.ForeignCustomer.GetAsync(
            $"/api/blend-plans/{draft.Id}/proposal")).StatusCode);
        if (flow is 1 or 2 or 3)
        {
            async Task<BlendExecutionStartResult> StartAsync(Guid targetContainer, Guid targetProposal,
                int version, bool commit = true)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var context = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, staff.Id.ToString())], "Fixture"))
                };
                var accessor = services.GetRequiredService<IHttpContextAccessor>();
                var previous = accessor.HttpContext;
                accessor.HttpContext = context;
                try
                {
                    var db = services.GetRequiredService<AppDbContext>();
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                    var result = await ExportModule.BeginApprovedBlendExecutionAsync(targetContainer,
                        targetProposal, version, context, services.GetRequiredService<ICurrentAccess>(), db,
                        services.GetRequiredService<LedgerService>(), services.GetRequiredService<ResidueEvaluationService>(),
                        services.GetRequiredService<ApplicableMrlRead>(), services.GetRequiredService<BlendAssessmentPolicyRead>(),
                        services.GetRequiredService<TimeProvider>(), default);
                    if (commit && result.Authorization is not null) await transaction.CommitAsync();
                    return result;
                }
                finally { accessor.HttpContext = previous; }
            }

            var path = $"/api/blend-plans/{draft.Id}/approve";
            var body = new ApproveBlendPlanRequest(Guid.NewGuid(), proposal.GetProperty("id").GetGuid(), 0);
            var pendingStart = await StartAsync(container.Id, body.ProposalId, 0);
            Assert.Null(pendingStart.Authorization);
            Assert.Equal(409, (pendingStart.Failure as IStatusCodeHttpResult)?.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync(path, body)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scenario.ForeignCustomer.PostAsJsonAsync(path, body)).StatusCode);

            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            using var competingOrderResponse = await writer.PostAsJsonAsync("/api/export-orders", new
            {
                operationId = Guid.NewGuid(), marketId, commodityId = scenario.CatalogCommodityId,
                requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
                limits = new[] { new { attributeId, maxValue = "14" } },
                containers = new[] { new { targetMass = "10", unit = "KG" } }
            });
            Assert.Equal(HttpStatusCode.Created, competingOrderResponse.StatusCode);
            var competingOrder = (await competingOrderResponse.Content.ReadFromJsonAsync<ExportOrderDto>())!;
            var competingContainer = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
                $"/api/export-orders/{competingOrder.Id}/containers"))!.Items);
            using var competingDraftResponse = await writer.PostAsJsonAsync(
                $"/api/export-containers/{competingContainer.Id}/blend-plans/drafts",
                new CreateBlendPlanDraftRequest(Guid.NewGuid(), competingOrder.Version, competingContainer.Version, "HEAD_RICE"));
            Assert.Equal(HttpStatusCode.Created, competingDraftResponse.StatusCode);
            var competingDraft = (await competingDraftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
            var competingPath = $"/api/blend-plans/{competingDraft.Id}";
            var competingCandidates = await writer.GetFromJsonAsync<JsonElement>(
                $"/api/export-containers/{competingContainer.Id}/candidates?facilityId={scenario.FacilityId}&planId={competingDraft.Id}");
            using var competingPreviewResponse = await writer.PostAsJsonAsync(competingPath + "/preview",
                new PreviewBlendPlanRequest(scenario.FacilityId, competingCandidates.GetProperty("scopeRevision").GetString()!));
            var competingPreview = await competingPreviewResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PASS", competingPreview.GetProperty("assessmentStatus").GetString());
            using var competingProposalResponse = await writer.PostAsJsonAsync(competingPath + "/propose",
                new ProposeBlendPlanRequest(Guid.NewGuid(), competingPreview.GetProperty("previewReference").GetGuid()));
            Assert.Equal(HttpStatusCode.Created, competingProposalResponse.StatusCode);
            var competingProposal = await competingProposalResponse.Content.ReadFromJsonAsync<JsonElement>();

            // Force a failure after the decision and active approval inserts.
            await using var revoke = new NpgsqlCommand("""
                REVOKE EXECUTE ON FUNCTION grainledger.ledger_reserve_approved_blend(uuid,uuid) FROM grainledger_test_runtime
                """, owner);
            await revoke.ExecuteNonQueryAsync();
            try
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable,
                    (await scenario.Customer.PostAsJsonAsync(path, body)).StatusCode);
            }
            finally
            {
                await using var grant = new NpgsqlCommand("""
                    GRANT EXECUTE ON FUNCTION grainledger.ledger_reserve_approved_blend(uuid,uuid) TO grainledger_test_runtime
                    """, owner);
                await grant.ExecuteNonQueryAsync();
            }
            var pending = await writer.GetFromJsonAsync<JsonElement>($"/api/blend-plans/{draft.Id}/decision");
            Assert.Equal("PENDING", pending.GetProperty("status").GetString());

            var attempts = new[] { body, body with { OperationId = Guid.NewGuid() } };
            var replies = await Task.WhenAll(attempts.Select(attempt => scenario.Customer.PostAsJsonAsync(path, attempt)));
            var winner = Array.FindIndex(replies, reply => reply.StatusCode == HttpStatusCode.OK);
            Assert.True(winner >= 0, string.Join(" | ", await Task.WhenAll(replies.Select(async reply =>
                $"{(int)reply.StatusCode}: {await reply.Content.ReadAsStringAsync()}"))));
            Assert.Equal(HttpStatusCode.Conflict, replies[1 - winner].StatusCode);
            var approved = await replies[winner].Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("APPROVED", approved.GetProperty("status").GetString());
            var reservation = approved.GetProperty("reservation");
            Assert.Equal("10", reservation.GetProperty("reservedKg").GetString());
            Assert.Equal(head, Assert.Single(reservation.GetProperty("lines").EnumerateArray()).GetProperty("lotId").GetGuid());
            using var replay = await scenario.Customer.PostAsJsonAsync(path, attempts[winner]);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.True(JsonElement.DeepEquals(approved, await replay.Content.ReadFromJsonAsync<JsonElement>()));
            Assert.Equal(stockBeforeProposal, await StockAsync());

            var held = await writer.GetFromJsonAsync<JsonElement>(candidatesUrl + "&includeExcluded=true");
            Assert.Equal("0", held.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("lotId").GetGuid() == head).GetProperty("availableKg").GetString());
            using var competingApproval = await scenario.Customer.PostAsJsonAsync(competingPath + "/approve",
                new ApproveBlendPlanRequest(Guid.NewGuid(), competingProposal.GetProperty("id").GetGuid(), 0));
            Assert.Equal(HttpStatusCode.Conflict, competingApproval.StatusCode);
            var competingDecision = await writer.GetFromJsonAsync<JsonElement>(competingPath + "/decision");
            Assert.Equal("PENDING", competingDecision.GetProperty("status").GetString());
            Assert.Equal("0", competingDecision.GetProperty("reservation").GetProperty("reservedKg").GetString());

            using var transfer = await scenario.Operator.PostAsJsonAsync("/api/ledger/transfers", new
            {
                id = Guid.NewGuid(), lotId = head, fromLocationId = scenario.LocationId,
                toLocationId = scenario.SecondLocationId, quantityKg = "1", occurredAt = DateTimeOffset.UtcNow
            });
            Assert.Equal(HttpStatusCode.Conflict, transfer.StatusCode);
            Assert.Contains("protected by an export blend reservation", await transfer.Content.ReadAsStringAsync());
            Assert.Equal(stockBeforeProposal, await StockAsync());

            using var edit = new HttpRequestMessage(HttpMethod.Patch, $"/api/export-containers/{container.Id}")
            {
                Content = JsonContent.Create(new { operationId = Guid.NewGuid(), targetMass = "9", unit = "KG" })
            };
            edit.Headers.TryAddWithoutValidation("If-Match", $"\"{container.Version}\"");
            using var editResponse = await writer.SendAsync(edit);
            Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
            Assert.Equal("ACTIVE_BLEND_APPROVAL_EXISTS",
                (await editResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

            var history = await writer.GetFromJsonAsync<JsonElement>($"/api/blend-plans/{draft.Id}/history");
            Assert.Single(history.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("action").GetString() == "PLAN_APPROVED");
            var revokePath = $"/api/blend-plans/{draft.Id}/revoke";
            if (flow == 2)
            {
                // Verify revalidation uses its own 10 kg even though public availability is zero.
                var provisional = await StartAsync(container.Id, body.ProposalId, 1, commit: false);
                Assert.NotNull(provisional.Authorization);
                Assert.Equal(10m, Assert.Single(provisional.Authorization.Reservations).RemainingKg);
                Assert.Equal(container.Version + 1, provisional.Authorization.ContainerVersion);
                var rolledBack = await writer.GetFromJsonAsync<ExportContainerDto>($"/api/export-containers/{container.Id}");
                Assert.Equal("PLANNED", rolledBack!.Status);
                Assert.Equal(container.Version, rolledBack.Version);

                var startTask = StartAsync(container.Id, body.ProposalId, 1);
                var revokeTask = scenario.Customer.PostAsJsonAsync(revokePath,
                    new RevokeBlendPlanRequest(Guid.NewGuid(), body.ProposalId, 1, "Race test"));
                await Task.WhenAll(startTask, revokeTask);
                var start = await startTask;
                using var revokeResult = await revokeTask;
                var containerAfterRace = await writer.GetFromJsonAsync<ExportContainerDto>(
                    $"/api/export-containers/{container.Id}");
                var raceHistory = await writer.GetFromJsonAsync<JsonElement>($"/api/blend-plans/{draft.Id}/history");
                if (start.Authorization is not null)
                {
                    Assert.Equal(HttpStatusCode.Conflict, revokeResult.StatusCode);
                    Assert.Equal("LOADING", containerAfterRace!.Status);
                    Assert.Single(raceHistory.GetProperty("items").EnumerateArray(),
                        item => item.GetProperty("action").GetString() == "PLAN_EXECUTION_STARTED");
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, revokeResult.StatusCode);
                    Assert.Equal(409, (start.Failure as IStatusCodeHttpResult)?.StatusCode);
                    Assert.Equal("PLANNED", containerAfterRace!.Status);
                }
                Assert.Equal(stockBeforeProposal, await StockAsync());
                return;
            }
            if (flow == 3)
            {
                var rejectedId = competingProposal.GetProperty("id").GetGuid();
                using var reject = await scenario.Customer.PostAsJsonAsync(competingPath + "/reject",
                    new RejectBlendPlanRequest(Guid.NewGuid(), rejectedId, 0, "Rejected fixture"));
                Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
                Assert.Equal(409, ((await StartAsync(competingContainer.Id, rejectedId, 1))
                    .Failure as IStatusCodeHttpResult)?.StatusCode);

                await scenario.HoldAsync(head);
                var heldStart = await StartAsync(container.Id, body.ProposalId, 1);
                Assert.Null(heldStart.Authorization);
                Assert.Equal(409, (heldStart.Failure as IStatusCodeHttpResult)?.StatusCode);
                var heldContainer = await writer.GetFromJsonAsync<ExportContainerDto>($"/api/export-containers/{container.Id}");
                Assert.Equal("PLANNED", heldContainer!.Status);
                return;
            }
            var revokeBody = new RevokeBlendPlanRequest(Guid.NewGuid(), body.ProposalId, 1, "Buyer cancelled");
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.PostAsJsonAsync(revokePath, revokeBody)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await scenario.Customer.PostAsJsonAsync(revokePath,
                revokeBody with { ExpectedDecisionVersion = 0 })).StatusCode);

            await using var loading = new NpgsqlCommand(
                "UPDATE grainledger.exports_containers SET status='LOADING' WHERE id=$1", owner);
            loading.Parameters.AddWithValue(container.Id);
            await loading.ExecuteNonQueryAsync();
            try
            {
                using var tooLate = await scenario.Customer.PostAsJsonAsync(revokePath, revokeBody);
                Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
                Assert.Equal("PLAN_EXECUTION_STARTED",
                    (await tooLate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            }
            finally
            {
                loading.CommandText = "UPDATE grainledger.exports_containers SET status='PLANNED' WHERE id=$1";
                await loading.ExecuteNonQueryAsync();
            }

            await using var releaseGrant = new NpgsqlCommand("""
                REVOKE EXECUTE ON FUNCTION grainledger.ledger_release_revoked_blend(uuid,uuid) FROM grainledger_test_runtime
                """, owner);
            await releaseGrant.ExecuteNonQueryAsync();
            try
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable,
                    (await scenario.Customer.PostAsJsonAsync(revokePath, revokeBody)).StatusCode);
            }
            finally
            {
                releaseGrant.CommandText = """
                    GRANT EXECUTE ON FUNCTION grainledger.ledger_release_revoked_blend(uuid,uuid) TO grainledger_test_runtime
                    """;
                await releaseGrant.ExecuteNonQueryAsync();
            }
            var stillApproved = await writer.GetFromJsonAsync<JsonElement>($"/api/blend-plans/{draft.Id}/decision");
            Assert.Equal("APPROVED", stillApproved.GetProperty("status").GetString());
            Assert.Equal("10", stillApproved.GetProperty("reservation").GetProperty("remainingKg").GetString());

            using var revokedReply = await scenario.Customer.PostAsJsonAsync(revokePath, revokeBody);
            Assert.True(revokedReply.StatusCode == HttpStatusCode.OK, await revokedReply.Content.ReadAsStringAsync());
            var revoked = await revokedReply.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("REVOKED", revoked.GetProperty("status").GetString());
            Assert.Equal(409, ((await StartAsync(container.Id, body.ProposalId, 2))
                .Failure as IStatusCodeHttpResult)?.StatusCode);
            Assert.Equal(2, revoked.GetProperty("decisionVersion").GetInt32());
            Assert.Equal("Buyer cancelled", revoked.GetProperty("reason").GetString());
            Assert.False(revoked.GetProperty("activeApproval").GetBoolean());
            var released = revoked.GetProperty("reservation");
            Assert.Equal("RELEASED", released.GetProperty("status").GetString());
            Assert.Equal("10", released.GetProperty("releasedKg").GetString());
            Assert.Equal("0", released.GetProperty("remainingKg").GetString());
            using var revokeReplay = await scenario.Customer.PostAsJsonAsync(revokePath, revokeBody);
            Assert.Equal(HttpStatusCode.OK, revokeReplay.StatusCode);
            Assert.True(JsonElement.DeepEquals(revoked, await revokeReplay.Content.ReadFromJsonAsync<JsonElement>()));
            Assert.Equal(HttpStatusCode.Conflict, (await scenario.Customer.PostAsJsonAsync(revokePath,
                revokeBody with { Reason = "Changed reason" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await scenario.Customer.PostAsJsonAsync(revokePath,
                revokeBody with { OperationId = Guid.NewGuid() })).StatusCode);
            Assert.Equal(stockBeforeProposal, await StockAsync());
            var available = await writer.GetFromJsonAsync<JsonElement>(candidatesUrl);
            Assert.Equal("10", available.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("lotId").GetGuid() == head).GetProperty("availableKg").GetString());

            var finalHistory = await writer.GetFromJsonAsync<JsonElement>($"/api/blend-plans/{draft.Id}/history");
            Assert.Single(finalHistory.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("action").GetString() == "PLAN_APPROVED");
            Assert.Single(finalHistory.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("action").GetString() == "PLAN_REVOKED");

            // Seed the future execution boundary on a separate approved proposal.
            var competingId = competingProposal.GetProperty("id").GetGuid();
            using var nowAvailable = await scenario.Customer.PostAsJsonAsync(competingPath + "/approve",
                new ApproveBlendPlanRequest(Guid.NewGuid(), competingId, 0));
            Assert.Equal(HttpStatusCode.OK, nowAvailable.StatusCode);
            await using var consume = new NpgsqlCommand("""
                INSERT INTO grainledger.ledger_blend_reservation_events
                    (id, proposal_id, lot_id, location_id, action, quantity_kg, actor_id, recorded_at)
                SELECT gen_random_uuid(), proposal_id, lot_id, location_id, 'CONSUME', 1, $2, now()
                FROM grainledger.ledger_blend_reservations WHERE proposal_id=$1
                """, owner);
            consume.Parameters.AddWithValue(competingId);
            consume.Parameters.AddWithValue(scenario.Accounts["customer"].User.Id);
            Assert.Equal(1, await consume.ExecuteNonQueryAsync());
            using var consumed = await scenario.Customer.PostAsJsonAsync(competingPath + "/revoke",
                new RevokeBlendPlanRequest(Guid.NewGuid(), competingId, 1, "Too late"));
            Assert.Equal(HttpStatusCode.Conflict, consumed.StatusCode);
            Assert.Equal("PLAN_EXECUTION_STARTED",
                (await consumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            var consumedDecision = await writer.GetFromJsonAsync<JsonElement>(competingPath + "/decision");
            Assert.Equal("APPROVED", consumedDecision.GetProperty("status").GetString());
            Assert.Equal("0", consumedDecision.GetProperty("reservation").GetProperty("releasedKg").GetString());
            return;
        }
        var compatibilityId = Guid.NewGuid();
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post,
            "/api/ledger/compatibilities", new
            {
                id = compatibilityId, facilityId = scenario.FacilityId,
                inputProducts = new[] { $"{scenario.CommodityId}:HEAD_RICE",
                    $"{scenario.CommodityId}:BROKEN_RICE" },
                outputCommodityId = scenario.CommodityId,
                effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
                evidenceId = scenario.EvidenceId
            });
        var yieldResponse = await scenario.Manager.PostAsJsonAsync(
            "/api/ledger/planning-yield-rules", new
            {
                id = Guid.NewGuid(), facilityId = scenario.FacilityId,
                compatibilityPolicyId = compatibilityId, versionNo = 1,
                outputFraction = "1", effectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-2),
                evidenceId = scenario.EvidenceId, reviewedBy = "Synthetic reviewer",
                reviewAuthority = "Synthetic authority",
                reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
            });
        Assert.Equal(HttpStatusCode.OK, yieldResponse.StatusCode);
        var brokenQuality = await scenario.Customer.PostAsJsonAsync(
            "/api/ledger/planning-quality-observations", new
            {
                id = Guid.NewGuid(), ownerId = scenario.OwnerId,
                facilityId = scenario.FacilityId, lotId = LedgerScenario.Output(milling, 1),
                attributeId, versionNo = 1, measuredValue = "13", unit = "%",
                measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                evidenceId = scenario.EvidenceId, reportReference = "SYNTHETIC-QC-BROKEN",
                reviewedBy = "Synthetic reviewer", reviewAuthority = "Synthetic authority",
                reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1), subjectConfirmed = true
            });
        Assert.Equal(HttpStatusCode.OK, brokenQuality.StatusCode);
        var blendOrderResponse = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId,
            commodityId = scenario.CatalogCommodityId, requiredMass = "20", unit = "KG",
            shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "20", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, blendOrderResponse.StatusCode);
        var blendOrder = (await blendOrderResponse.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var blendContainer = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{blendOrder.Id}/containers"))!.Items);
        var blendDraftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{blendContainer.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), blendOrder.Version,
                blendContainer.Version, "BLENDED_RICE"));
        Assert.Equal(HttpStatusCode.Created, blendDraftResponse.StatusCode);
        var blendDraft = (await blendDraftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var blendCandidates = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{blendContainer.Id}/candidates?facilityId={scenario.FacilityId}&planId={blendDraft.Id}");
        var blendPreviewResponse = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{blendDraft.Id}/preview",
            new PreviewBlendPlanRequest(scenario.FacilityId,
                blendCandidates.GetProperty("scopeRevision").GetString()!, 8));
        Assert.Equal(HttpStatusCode.OK, blendPreviewResponse.StatusCode);
        var blendPreview = await blendPreviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        var diagnosticResponse = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{blendDraft.Id}/residue-preview", new
            {
                facilityId = scenario.FacilityId,
                scopeRevision = blendCandidates.GetProperty("scopeRevision").GetString(),
                mode = "BLEND_INPUT_STOCK", compatibilityPolicyId = compatibilityId,
                selected = blendPreview.GetProperty("selection").GetProperty("allocations")
                    .EnumerateArray().Select(x => new
                    {
                        lotId = x.GetProperty("lotId").GetGuid(),
                        locationId = x.GetProperty("locationId").GetGuid(),
                        quantityKg = x.GetProperty("quantityKg").GetString(),
                        candidateRevision = x.GetProperty("candidateRevision").GetString()
                    }).ToArray(),
                substanceIds = new[] { f.SubstanceId }
            });
        Assert.Equal(HttpStatusCode.OK, diagnosticResponse.StatusCode);
        var diagnostic = await diagnosticResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BOUNDS_AVAILABLE", diagnostic.GetProperty("hypotheticalBlendStatus").GetString());
        Assert.True(blendPreview.GetProperty("assessmentStatus").GetString() == "PASS",
            blendPreview.ToString());
        Assert.Equal("BLEND_INPUT_STOCK", blendPreview.GetProperty("selection")
            .GetProperty("mode").GetString());
        Assert.Equal(2, blendPreview.GetProperty("selection")
            .GetProperty("allocations").GetArrayLength());
        var blendStockBefore = await StockAsync();
        var blendReference = blendPreview.GetProperty("previewReference").GetGuid();
        var simultaneousProposals = await Task.WhenAll(
            writer.PostAsJsonAsync($"/api/blend-plans/{blendDraft.Id}/propose",
                new ProposeBlendPlanRequest(Guid.NewGuid(), blendReference)),
            writer.PostAsJsonAsync($"/api/blend-plans/{blendDraft.Id}/propose",
                new ProposeBlendPlanRequest(Guid.NewGuid(), blendReference)));
        Assert.Single(simultaneousProposals, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(simultaneousProposals, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(blendStockBefore, await StockAsync());
        if (flow == 4)
        {
            var blend = await simultaneousProposals.Single(r => r.StatusCode == HttpStatusCode.Created)
                .Content.ReadFromJsonAsync<JsonElement>();
            var blendId = blend.GetProperty("id").GetGuid();
            var path = $"/api/blend-plans/{blendDraft.Id}";
            var approval = new ApproveBlendPlanRequest(Guid.NewGuid(), blendId, 0);
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            await using var failSecond = new NpgsqlCommand("""
                CREATE FUNCTION public.test_fail_second_blend_line() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS(SELECT 1 FROM grainledger.ledger_blend_reservations
                              WHERE proposal_id = NEW.proposal_id) THEN
                        RAISE EXCEPTION 'Fixture failed the second stock position' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER test_fail_second_blend_line BEFORE INSERT ON grainledger.ledger_blend_reservations
                    FOR EACH ROW EXECUTE FUNCTION public.test_fail_second_blend_line();
                """, owner);
            await failSecond.ExecuteNonQueryAsync();
            try
            {
                using var failed = await scenario.Customer.PostAsJsonAsync(path + "/approve", approval);
                Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
                Assert.Equal("PLAN_RESERVATION_CONFLICT",
                    (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
                var pending = await writer.GetFromJsonAsync<JsonElement>(path + "/decision");
                Assert.Equal("PENDING", pending.GetProperty("status").GetString());
                Assert.Empty(pending.GetProperty("reservation").GetProperty("lines").EnumerateArray());
                Assert.Equal(blendStockBefore, await StockAsync());
            }
            finally
            {
                failSecond.CommandText = """
                    DROP TRIGGER test_fail_second_blend_line ON grainledger.ledger_blend_reservations;
                    DROP FUNCTION public.test_fail_second_blend_line();
                    """;
                await failSecond.ExecuteNonQueryAsync();
            }

            var decisions = await Task.WhenAll(
                scenario.Customer.PostAsJsonAsync(path + "/approve", approval),
                scenario.Customer.PostAsJsonAsync(path + "/reject",
                    new RejectBlendPlanRequest(Guid.NewGuid(), blendId, 0, "Alternative shipment")));
            Assert.Single(decisions, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(decisions, r => r.StatusCode == HttpStatusCode.Conflict);
            var decided = await writer.GetFromJsonAsync<JsonElement>(path + "/decision");
            var approved = decisions[0].StatusCode == HttpStatusCode.OK;
            Assert.Equal(approved ? "APPROVED" : "REJECTED", decided.GetProperty("status").GetString());
            Assert.Equal(approved ? 2 : 0, decided.GetProperty("reservation").GetProperty("lines").GetArrayLength());
            var history = await writer.GetFromJsonAsync<JsonElement>(path + "/history");
            Assert.Single(history.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("action").GetString() is "PLAN_APPROVED" or "PLAN_REJECTED");
            Assert.Equal(blendStockBefore, await StockAsync());
            return;
        }
        var revisedQuality = await scenario.Customer.PostAsJsonAsync(
            "/api/ledger/planning-quality-observations", new
            {
                id = Guid.NewGuid(), ownerId = scenario.OwnerId,
                facilityId = scenario.FacilityId, lotId = head, attributeId,
                versionNo = 2, measuredValue = "15", unit = "%",
                measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                evidenceId = scenario.EvidenceId, reportReference = "SYNTHETIC-QC-2",
                reviewedBy = "Synthetic reviewer", reviewAuthority = "Synthetic authority",
                reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1), subjectConfirmed = true
            });
        Assert.Equal(HttpStatusCode.OK, revisedQuality.StatusCode);
        using var staleApproval = await scenario.Customer.PostAsJsonAsync(
            $"/api/blend-plans/{draft.Id}/approve",
            new ApproveBlendPlanRequest(Guid.NewGuid(), proposal.GetProperty("id").GetGuid(), 0));
        Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
        var decisionAfterConflict = await writer.GetFromJsonAsync<JsonElement>($"/api/blend-plans/{draft.Id}/decision");
        Assert.Equal("PENDING", decisionAfterConflict.GetProperty("status").GetString());
        Assert.Equal("0", decisionAfterConflict.GetProperty("reservation").GetProperty("reservedKg").GetString());
        var stale = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/blend-plans/{draft.Id}/proposal");
        Assert.Equal("STALE", stale.GetProperty("freshness").GetString());
        Assert.Contains(stale.GetProperty("freshnessReasons").EnumerateArray(),
            x => x.GetString() is "PREVIEW_DEPENDENCIES_CHANGED" or
                "PLANNING_INPUTS_CHANGED_OR_UNAVAILABLE");
        var changedRetry = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{draft.Id}/propose",
            proposeBody with { PreviewReference = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, changedRetry.StatusCode);
        var failedDraftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{container.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version,
                container.Version, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, failedDraftResponse.StatusCode);
        var failedDraft = (await failedDraftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var failedCandidates = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}&planId={failedDraft.Id}");
        var failedPreviewResponse = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{failedDraft.Id}/preview",
            new PreviewBlendPlanRequest(scenario.FacilityId,
                failedCandidates.GetProperty("scopeRevision").GetString()!, 8));
        Assert.Equal(HttpStatusCode.OK, failedPreviewResponse.StatusCode);
        var failedPreview = await failedPreviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(failedPreview.GetProperty("assessmentStatus").GetString() != "PASS",
            failedPreview.ToString());
        Assert.False(failedPreview.GetProperty("canPropose").GetBoolean());
        Assert.Equal(JsonValueKind.Null, failedPreview.GetProperty("previewReference").ValueKind);
        async Task PublishQualityAsync(int version, string value)
        {
            var response = await scenario.Customer.PostAsJsonAsync(
                "/api/ledger/planning-quality-observations", new
                {
                    id = Guid.NewGuid(), ownerId = scenario.OwnerId,
                    facilityId = scenario.FacilityId, lotId = head, attributeId,
                    versionNo = version, measuredValue = value, unit = "%",
                    measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    evidenceId = scenario.EvidenceId,
                    reportReference = $"SYNTHETIC-QC-{version}",
                    reviewedBy = "Synthetic reviewer",
                    reviewAuthority = "Synthetic authority",
                    reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    subjectConfirmed = true
                });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await PublishQualityAsync(3, "13");
        var secondDraftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{container.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version,
                container.Version, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, secondDraftResponse.StatusCode);
        var secondDraft = (await secondDraftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var secondCandidates = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}&planId={secondDraft.Id}");
        var secondPreviewResponse = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{secondDraft.Id}/preview",
            new PreviewBlendPlanRequest(scenario.FacilityId,
                secondCandidates.GetProperty("scopeRevision").GetString()!, 8));
        Assert.Equal(HttpStatusCode.OK, secondPreviewResponse.StatusCode);
        var secondPreview = await secondPreviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PASS", secondPreview.GetProperty("assessmentStatus").GetString());
        await PublishQualityAsync(4, "15");
        var staleProposal = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{secondDraft.Id}/propose",
            new ProposeBlendPlanRequest(Guid.NewGuid(),
                secondPreview.GetProperty("previewReference").GetGuid()));
        Assert.Equal(HttpStatusCode.Conflict, staleProposal.StatusCode);
        await PublishQualityAsync(5, "13");
        var thirdDraftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{container.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version,
                container.Version, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, thirdDraftResponse.StatusCode);
        var thirdDraft = (await thirdDraftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var thirdCandidates = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}&planId={thirdDraft.Id}");
        var thirdPreviewResponse = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{thirdDraft.Id}/preview",
            new PreviewBlendPlanRequest(scenario.FacilityId,
                thirdCandidates.GetProperty("scopeRevision").GetString()!, 8));
        Assert.Equal(HttpStatusCode.OK, thirdPreviewResponse.StatusCode);
        var thirdPreview = await thirdPreviewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PASS", thirdPreview.GetProperty("assessmentStatus").GetString());
        await scenario.HoldAsync(head);
        var heldProposal = await writer.PostAsJsonAsync(
            $"/api/blend-plans/{thirdDraft.Id}/propose",
            new ProposeBlendPlanRequest(Guid.NewGuid(),
                thirdPreview.GetProperty("previewReference").GetGuid()));
        Assert.True(heldProposal.StatusCode == HttpStatusCode.Conflict,
            $"Expected a stale preview conflict, got {(int)heldProposal.StatusCode}: " +
            await heldProposal.Content.ReadAsStringAsync());
    }
}
