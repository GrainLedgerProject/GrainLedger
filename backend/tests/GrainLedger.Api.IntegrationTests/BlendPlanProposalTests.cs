using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Exports;
using GrainLedger.Modules.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BlendPlanProposalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Confirmed_evidence_can_propose_once_without_changing_stock()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(scenario);
        var receipt = await scenario.ReceiptAsync("20");
        var sample = await SampleAsync(scenario, receipt);
        var (_, report) = await f.ReportAsync(sample);
        await f.PublishRuleAsync();
        await f.SelectAsync(sample, report);
        await f.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        await f.ProcessRuleAsync("MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5", "0.5");
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
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
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
                null, [f.SubstanceId], "0.1", ResidueScenario.Basis,
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
                SELECT (SELECT count(*) FROM ledger.positions),
                       (SELECT coalesce(sum(quantity_kg), 0) FROM ledger.positions),
                       (SELECT count(*) FROM ledger.postings),
                       (SELECT count(*) FROM ledger.holds)
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
        await f.ProcessRuleAsync("MILLING", "DRIED_PADDY", "BROKEN_RICE", "0.5", "0.5");
        await f.ProcessRuleAsync("BLENDING", "HEAD_RICE", "BLENDED_RICE");
        await f.ProcessRuleAsync("BLENDING", "BROKEN_RICE", "BLENDED_RICE");
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
