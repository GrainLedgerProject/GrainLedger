using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Exports;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ExportOrderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Draft_residue_preview_uses_selected_stock_without_reserving_it()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipt = await scenario.ReceiptAsync("20");
        var raw = LedgerScenario.Lots(receipt)[0];
        var drying = await scenario.DraftAsync("DRYING", [(raw, "20")], [("DRIED_PADDY", "20")]);
        await scenario.CommitAsync(drying);
        var milling = await scenario.DraftAsync("MILLING", [(LedgerScenario.Output(drying), "20")],
            [("HEAD_RICE", "10"), ("BROKEN_RICE", "10")]);
        await scenario.CommitAsync(milling);
        var head = LedgerScenario.Output(milling, 0);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(staff, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf"))
            .GetProperty("token").GetString());
        var (market, _, attribute) = await CatalogAsync();
        var substanceId = Guid.NewGuid();
        var mrlVersionId = Guid.NewGuid();
        using (var catalogScope = factory.Services.CreateScope())
        {
            var catalog = catalogScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.ActiveSubstances.Add(new ActiveSubstance
            { Id = substanceId, Code = $"F14-{substanceId:N}", Name = "Synthetic F14 analyte" });
            catalog.MrlVersions.Add(new MrlVersion
            {
                Id = mrlVersionId, MarketId = market, CommodityId = scenario.CatalogCommodityId,
                VersionNo = 1, Status = "PUBLISHED", SourceRef = "Synthetic F14 rule",
                PublishedBy = staff.Id, PublishedAt = DateTimeOffset.UtcNow.AddDays(-2),
                EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await catalog.SaveChangesAsync();
            catalog.MrlLimits.Add(new MrlLimit
            { Id = Guid.NewGuid(), MrlVersionId = mrlVersionId, SubstanceId = substanceId,
                LimitValue = 0.01m, Unit = "mg/kg" });
            await catalog.SaveChangesAsync();
        }
        var orderResponse = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = scenario.CatalogCommodityId,
            requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = (await orderResponse.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{order.Id}/containers"))!.Items);
        var draftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{container.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version, container.Version, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var candidatesRoute = $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}&planId={draft.Id}";
        var before = await writer.GetFromJsonAsync<JsonElement>(candidatesRoute);
        var position = Assert.Single(before.GetProperty("items").EnumerateArray().ToArray(),
            x => x.GetProperty("lotId").GetGuid() == head);
        var previewRoute = $"/api/blend-plans/{draft.Id}/residue-preview";
        var previewBody = new
        {
            facilityId = scenario.FacilityId, scopeRevision = before.GetProperty("scopeRevision").GetString(),
            mode = "DIRECT_STOCK", compatibilityPolicyId = (Guid?)null,
            selected = new[] { new { lotId = head, locationId = position.GetProperty("locationId").GetGuid(),
                quantityKg = "10", candidateRevision = position.GetProperty("candidateRevision").GetString() } },
            substanceIds = new[] { substanceId }
        };
        var previewResponse = await writer.PostAsJsonAsync(previewRoute, previewBody);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = (await previewResponse.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("VALID", preview.GetProperty("status").GetString());
        Assert.Equal("INCOMPLETE", preview.GetProperty("assessmentStatus").GetString());
        Assert.Contains(preview.GetProperty("assessmentFindings").EnumerateArray(),
            x => x.GetProperty("code").GetString() == "ASSESSMENT_POLICY_MISSING");
        var applicable = preview.GetProperty("applicableMrl");
        Assert.Equal("FOUND", applicable.GetProperty("status").GetString());
        Assert.Equal(mrlVersionId, applicable.GetProperty("versionId").GetGuid());
        Assert.Equal("Synthetic F14 rule", applicable.GetProperty("sourceRef").GetString());
        Assert.Equal("0.01", Assert.Single(applicable.GetProperty("requestedLimits").EnumerateArray().ToArray())
            .GetProperty("limitValue").GetString());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("hypotheticalBlend").ValueKind);
        Assert.Equal(head, Assert.Single(preview.GetProperty("residue").EnumerateArray().ToArray())
            .GetProperty("lotId").GetGuid());
        var after = await writer.GetFromJsonAsync<JsonElement>(candidatesRoute);
        Assert.Equal(before.GetProperty("scopeRevision").GetString(), after.GetProperty("scopeRevision").GetString());
        var stale = await writer.PostAsJsonAsync(previewRoute, new
        {
            previewBody.facilityId, scopeRevision = "outdated", previewBody.mode,
            previewBody.compatibilityPolicyId, previewBody.selected, previewBody.substanceIds
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Blend_preview_uses_policy_output_commodity_and_stays_incomplete_without_yield_and_evidence()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipt = await scenario.ReceiptAsync("20");
        var drying = await scenario.DraftAsync("DRYING", [(LedgerScenario.Lots(receipt)[0], "20")],
            [("DRIED_PADDY", "20")]);
        await scenario.CommitAsync(drying);
        var milling = await scenario.DraftAsync("MILLING", [(LedgerScenario.Output(drying), "20")],
            [("HEAD_RICE", "10"), ("BROKEN_RICE", "10")]);
        await scenario.CommitAsync(milling);
        var policyId = Guid.NewGuid();
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = policyId, facilityId = scenario.FacilityId,
            inputProducts = new[] { $"{scenario.CommodityId}:HEAD_RICE", $"{scenario.CommodityId}:BROKEN_RICE" },
            outputCommodityId = scenario.CommodityId,
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = scenario.EvidenceId
        });
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(staff, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf"))
            .GetProperty("token").GetString());
        var (market, _, attribute) = await CatalogAsync();
        var orderResponse = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = scenario.CatalogCommodityId,
            requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = (await orderResponse.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{order.Id}/containers"))!.Items);
        var draftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{container.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version, container.Version, "BLENDED_RICE"));
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = (await draftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var candidates = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}&planId={draft.Id}");
        var positions = candidates.GetProperty("items").EnumerateArray().ToArray();
        var selected = new[] { LedgerScenario.Output(milling, 0), LedgerScenario.Output(milling, 1) }
            .Select(lotId =>
            {
                var item = Assert.Single(positions, x => x.GetProperty("lotId").GetGuid() == lotId);
                return new { lotId, locationId = item.GetProperty("locationId").GetGuid(),
                    quantityKg = "10", candidateRevision = item.GetProperty("candidateRevision").GetString() };
            }).ToArray();
        var previewRequest = new
        {
            facilityId = scenario.FacilityId, scopeRevision = candidates.GetProperty("scopeRevision").GetString(),
            mode = "BLEND_INPUT_STOCK", compatibilityPolicyId = policyId, selected,
            substanceIds = new[] { Guid.NewGuid() }
        };
        var response = await writer.PostAsJsonAsync($"/api/blend-plans/{draft.Id}/residue-preview", previewRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("INCOMPLETE", preview.GetProperty("status").GetString());
        Assert.Contains(preview.GetProperty("findings").EnumerateArray(),
            x => x.GetString() == "OUTPUT_YIELD_POLICY_MISSING");
        Assert.NotEqual(JsonValueKind.Null, preview.GetProperty("hypotheticalBlend").ValueKind);
        Assert.Equal("INCOMPLETE", preview.GetProperty("hypotheticalBlendStatus").GetString());
        Assert.Equal("INCOMPLETE", preview.GetProperty("assessmentStatus").GetString());
        Assert.Contains(preview.GetProperty("assessmentFindings").EnumerateArray(),
            x => x.GetProperty("code").GetString() == "OUTPUT_YIELD_POLICY_MISSING");
        Assert.Equal(preview.GetProperty("residueSnapshotAt").GetString(),
            preview.GetProperty("hypotheticalBlend").GetProperty("snapshotAt").GetString());
        var yieldId = Guid.NewGuid();
        await LedgerScenario.SendAsync(scenario.Manager, HttpMethod.Post,
            "/api/ledger/planning-yield-rules", new
            {
                id = yieldId, facilityId = scenario.FacilityId,
                compatibilityPolicyId = policyId, versionNo = 1,
                outputFraction = "0.5", effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1),
                evidenceId = scenario.EvidenceId, reviewedBy = "Synthetic reviewer",
                reviewAuthority = "Synthetic authority", reviewedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        var afterYield = await writer.PostAsJsonAsync($"/api/blend-plans/{draft.Id}/residue-preview", previewRequest);
        Assert.Equal(HttpStatusCode.OK, afterYield.StatusCode);
        var withYield = await afterYield.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALID", withYield.GetProperty("status").GetString());
        Assert.Equal(yieldId, withYield.GetProperty("outputYieldPolicy").GetProperty("id").GetGuid());
        Assert.Equal("INCOMPLETE", withYield.GetProperty("assessmentStatus").GetString());
        Assert.DoesNotContain(withYield.GetProperty("findings").EnumerateArray(),
            x => x.GetString() == "OUTPUT_YIELD_POLICY_MISSING");
        var assessedResponse = await writer.PostAsJsonAsync($"/api/blend-plans/{draft.Id}/preview",
            new PreviewBlendPlanRequest(scenario.FacilityId,
                candidates.GetProperty("scopeRevision").GetString()!, 4));
        Assert.Equal(HttpStatusCode.OK, assessedResponse.StatusCode);
        var assessed = await assessedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INCOMPLETE", assessed.GetProperty("assessmentStatus").GetString());
        Assert.False(assessed.GetProperty("canPropose").GetBoolean());
        Assert.Equal(JsonValueKind.Null, assessed.GetProperty("previewReference").ValueKind);
        var selectedBySearch = assessed.GetProperty("selection").GetProperty("allocations")
            .EnumerateArray().ToArray();
        Assert.Equal(20m, selectedBySearch.Sum(x => decimal.Parse(
            x.GetProperty("quantityKg").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Candidate_product_status_uses_draft_target_and_effective_Ledger_blend_sets()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipt = await scenario.ReceiptAsync("30");
        var raw = LedgerScenario.Lots(receipt)[0];
        var drying = await scenario.DraftAsync("DRYING", [(raw, "30")], [("DRIED_PADDY", "30")]);
        await scenario.CommitAsync(drying);
        var milling = await scenario.DraftAsync("MILLING", [(LedgerScenario.Output(drying), "30")],
            [("HEAD_RICE", "12"), ("BROKEN_RICE", "18")]);
        await scenario.CommitAsync(milling);
        var head = LedgerScenario.Output(milling, 0);
        var broken = LedgerScenario.Output(milling, 1);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(staff, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf"))
            .GetProperty("token").GetString());
        var (market, otherCommodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = scenario.CatalogCommodityId,
            requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{order.Id}/containers"))!.Items);
        var draftRoute = $"/api/export-containers/{container.Id}/blend-plans/drafts";
        async Task<BlendPlanDraftDto> Draft(string target)
        {
            var response = await writer.PostAsJsonAsync(draftRoute,
                new CreateBlendPlanDraftRequest(Guid.NewGuid(), 1, 1, target));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        }
        var headDraft = await Draft("HEAD_RICE");
        var route = $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}";
        var baseList = await writer.GetFromJsonAsync<JsonElement>(route);
        Assert.False(baseList.TryGetProperty("physicalSelection", out _));
        Assert.All(baseList.GetProperty("items").EnumerateArray().ToArray(),
            item => Assert.Equal("NOT_EVALUATED", item.GetProperty("productCompatibility").GetString()));
        var otherOrderResponse = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = otherCommodity,
            requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, otherOrderResponse.StatusCode);
        var otherOrder = (await otherOrderResponse.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var otherContainer = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{otherOrder.Id}/containers"))!.Items);
        var otherList = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{otherContainer.Id}/candidates?facilityId={scenario.FacilityId}");
        Assert.Contains(otherList.GetProperty("items").EnumerateArray().ToArray(), x =>
            x.GetProperty("lotId").GetGuid() == head);
        var otherDraftResponse = await writer.PostAsJsonAsync(
            $"/api/export-containers/{otherContainer.Id}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), 1, 1, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, otherDraftResponse.StatusCode);
        var otherDraft = (await otherDraftResponse.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        var otherHints = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{otherContainer.Id}/candidates?facilityId={scenario.FacilityId}&planId={otherDraft.Id}");
        var riceHead = Assert.Single(otherHints.GetProperty("items").EnumerateArray().ToArray(),
            x => x.GetProperty("lotId").GetGuid() == head);
        Assert.Equal("DIRECT_MATCH", riceHead.GetProperty("productCompatibility").GetString());
        Assert.Equal("PRODUCT_MISMATCH", Assert.Single(otherHints.GetProperty("items").EnumerateArray().ToArray(),
            x => x.GetProperty("lotId").GetGuid() == broken).GetProperty("productCompatibility").GetString());
        Assert.True(decimal.Parse(otherHints.GetProperty("summary").GetProperty("directProductAvailableMassKg")
            .GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= 12m);
        var matchingOnly = await writer.GetFromJsonAsync<JsonElement>(
            $"/api/export-containers/{otherContainer.Id}/candidates?facilityId={scenario.FacilityId}&planId={otherDraft.Id}&productMatchOnly=true&pageSize=1");
        Assert.Equal(1, matchingOnly.GetProperty("totalCount").GetInt32());
        Assert.Equal(head, Assert.Single(matchingOnly.GetProperty("items").EnumerateArray().ToArray())
            .GetProperty("lotId").GetGuid());
        Assert.False(matchingOnly.GetProperty("hasMore").GetBoolean());
        var ricePreview = await writer.PostAsJsonAsync($"/api/blend-plans/{otherDraft.Id}/residue-preview", new
        {
            facilityId = scenario.FacilityId,
            scopeRevision = otherHints.GetProperty("scopeRevision").GetString(),
            mode = "DIRECT_STOCK", compatibilityPolicyId = (Guid?)null,
            selected = new[] { new { lotId = head,
                locationId = riceHead.GetProperty("locationId").GetGuid(),
                quantityKg = "10", candidateRevision = riceHead.GetProperty("candidateRevision").GetString() } },
            substanceIds = new[] { Guid.NewGuid() }
        });
        Assert.Equal(HttpStatusCode.OK, ricePreview.StatusCode);
        Assert.Equal("VALID", (await ricePreview.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString());
        var direct = await writer.GetFromJsonAsync<JsonElement>(route + $"&planId={headDraft.Id}");
        var directItems = direct.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal("HEAD_RICE", direct.GetProperty("targetProductIdentity").GetString());
        Assert.Equal("DIRECT_MATCH", Assert.Single(directItems, x => x.GetProperty("lotId").GetGuid() == head)
            .GetProperty("productCompatibility").GetString());
        Assert.Equal("PRODUCT_MISMATCH", Assert.Single(directItems, x => x.GetProperty("lotId").GetGuid() == broken)
            .GetProperty("productCompatibility").GetString());
        Assert.False(direct.TryGetProperty("physicalSelection", out _));
        Assert.True(decimal.Parse(direct.GetProperty("summary").GetProperty("directProductAvailableMassKg")
            .GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= 12m);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.ForeignCustomer.GetAsync(
            route + $"&planId={headDraft.Id}")).StatusCode);

        var unavailableDraft = await Draft("HUSK");
        var unavailable = await writer.GetFromJsonAsync<JsonElement>(route + $"&planId={unavailableDraft.Id}");
        Assert.Equal("0", unavailable.GetProperty("summary").GetProperty("directProductAvailableMassKg").GetString());

        var blendedDraft = await Draft("BLENDED_RICE");
        var blendedRoute = route + $"&planId={blendedDraft.Id}";
        var before = await writer.GetFromJsonAsync<JsonElement>(blendedRoute);
        Assert.False(before.TryGetProperty("physicalSelection", out _));
        Assert.All(before.GetProperty("items").EnumerateArray().ToArray(),
            item => Assert.Equal("BLEND_POLICY_MISSING", item.GetProperty("productCompatibility").GetString()));
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(), facilityId = scenario.FacilityId,
            inputProducts = new[] { $"{scenario.CommodityId}:HEAD_RICE", $"{scenario.CommodityId}:BROKEN_RICE",
                $"{scenario.CommodityId}:BLENDED_RICE" },
            outputCommodityId = scenario.CommodityId,
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = scenario.EvidenceId
        });
        var missingThirdInput = await writer.GetFromJsonAsync<JsonElement>(blendedRoute);
        Assert.All(missingThirdInput.GetProperty("items").EnumerateArray().ToArray(), item =>
            Assert.Equal("POSSIBLE_BLEND_INPUT", item.GetProperty("productCompatibility").GetString()));
        Assert.False(missingThirdInput.TryGetProperty("physicalSelection", out _));
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(), facilityId = scenario.FacilityId,
            inputProducts = new[] { $"{scenario.CommodityId}:HEAD_RICE", $"{scenario.CommodityId}:BROKEN_RICE" },
            outputCommodityId = scenario.CommodityId,
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = scenario.EvidenceId
        });
        Assert.Equal(HttpStatusCode.Conflict, (await writer.GetAsync(blendedRoute + "&scopeRevision=" +
            before.GetProperty("scopeRevision").GetString())).StatusCode);
        var after = await writer.GetFromJsonAsync<JsonElement>(blendedRoute);
        Assert.All(after.GetProperty("items").EnumerateArray().ToArray(), item =>
            Assert.Equal("POSSIBLE_BLEND_INPUT", item.GetProperty("productCompatibility").GetString()));
        Assert.False(after.TryGetProperty("physicalSelection", out _));
        Assert.False(after.GetProperty("canPropose").GetBoolean());
    }

    [Fact]
    public async Task Candidate_lineage_includes_processing_operation_and_source()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipt = await scenario.ReceiptAsync("30");
        var sourceLot = LedgerScenario.Lots(receipt)[0];
        var draft = await scenario.DraftAsync("DRYING", [(sourceLot, "30")], [("DRIED_PADDY", "28")], "2");
        await scenario.CommitAsync(draft);
        var outputLot = LedgerScenario.Output(draft);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(staff, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString());
        var (market, _, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = scenario.CatalogCommodityId,
            requiredMass = "20", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "20", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var route = $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}";
        var list = await writer.GetFromJsonAsync<JsonElement>(route);
        var candidate = Assert.Single(list.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(outputLot, candidate.GetProperty("lotId").GetGuid());
        Assert.Equal("DRIED_PADDY", candidate.GetProperty("stage").GetString());
        Assert.True(candidate.GetProperty("lineageComplete").GetBoolean(), list.ToString());
        var lineage = await writer.GetFromJsonAsync<JsonElement>($"/api/export-containers/{container.Id}/candidates/{outputLot}/lineage?facilityId={scenario.FacilityId}&locationId={scenario.LocationId}");
        Assert.Contains(lineage.GetProperty("lineage").GetProperty("nodes").EnumerateArray(),
            x => x.GetProperty("lotId").GetGuid() == sourceLot);
        Assert.Contains(lineage.GetProperty("lineage").GetProperty("edges").EnumerateArray(),
            x => x.GetProperty("inputLotId").GetGuid() == sourceLot &&
                x.GetProperty("outputLotId").GetGuid() == outputLot);
        var operation = Assert.Single(lineage.GetProperty("lineage").GetProperty("operations").EnumerateArray().ToArray());
        Assert.Equal("DRYING", operation.GetProperty("kind").GetString());
        Assert.Equal("COMMITTED", operation.GetProperty("status").GetString());
        Assert.Equal("2", operation.GetProperty("content").GetProperty("declaredLossKg").GetString());
    }

    [Fact]
    public async Task Mixed_bulk_candidates_keep_withdrawn_lineage_when_pool_is_topped_up()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var mixedLocation = Guid.NewGuid();
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post, "/api/ledger/locations", new
        { id = mixedLocation, facilityId = scenario.FacilityId, name = "Synthetic planning pool", kind = "STORE", mixedBulk = true });
        await LedgerScenario.SendAsync(scenario.Admin, HttpMethod.Post, "/api/ledger/mixed-bulk-policies", new
        {
            id = Guid.NewGuid(), locationId = mixedLocation, ownerId = scenario.OwnerId,
            commodityId = scenario.CommodityId, stage = "RAW_PADDY", productIdentity = "RAW_PADDY",
            effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = scenario.EvidenceId,
            identityScopeConfirmed = true
        });
        async Task<JsonElement> Arrive(string quantity)
        {
            var receipt = await LedgerScenario.SendAsync(scenario.Purchaser, HttpMethod.Post, "/api/procurements",
                scenario.ReceiptBody(Guid.NewGuid(), [quantity], mixedLocation));
            await scenario.ConfirmArrivalAsync(receipt);
            return receipt;
        }
        var first = await Arrive("30");
        var second = await Arrive("20");
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(staff, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString());
        var (market, _, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = scenario.CatalogCommodityId,
            requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var route = $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}";
        var before = await writer.GetFromJsonAsync<JsonElement>(route + "&includeExcluded=true");
        var pool = Assert.Single(before.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(2, pool.GetProperty("sourceCount").GetInt32());
        Assert.True(pool.GetProperty("lineageComplete").GetBoolean(), before.ToString());
        Assert.True(pool.GetProperty("inventoryEligible").GetBoolean(), before.ToString());
        Assert.Equal("CURRENT_POOL", pool.GetProperty("stockKind").GetString());
        var poolLotId = pool.GetProperty("lotId").GetGuid();
        var poolRevision = long.Parse(pool.GetProperty("poolRevision").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var transferred = await LedgerScenario.SendAsync(scenario.Operator, HttpMethod.Post, "/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = poolLotId, fromLocationId = mixedLocation,
            toLocationId = scenario.SecondLocationId, quantityKg = "10", poolRevision,
            occurredAt = DateTimeOffset.UtcNow
        });
        var aliquotId = transferred.GetProperty("actualLotId").GetGuid();
        var third = await Arrive("15");
        var after = await writer.GetFromJsonAsync<JsonElement>(route + "&includeExcluded=true");
        Assert.NotEqual(before.GetProperty("scopeRevision").GetString(), after.GetProperty("scopeRevision").GetString());
        var items = after.GetProperty("items").EnumerateArray().ToArray();
        var aliquot = Assert.Single(items, x => x.GetProperty("lotId").GetGuid() == aliquotId);
        var current = Assert.Single(items, x => x.GetProperty("stockKind").GetString() == "CURRENT_POOL");
        Assert.True(aliquot.GetProperty("inventoryEligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, aliquot.GetProperty("poolRevision").ValueKind);
        Assert.Equal(2, aliquot.GetProperty("sourceCount").GetInt32());
        Assert.Equal(3, current.GetProperty("sourceCount").GetInt32());
        var lineage = await writer.GetFromJsonAsync<JsonElement>($"/api/export-containers/{container.Id}/candidates/{aliquotId}/lineage?facilityId={scenario.FacilityId}&locationId={scenario.SecondLocationId}");
        var sources = lineage.GetProperty("composition").EnumerateArray()
            .Select(x => x.GetProperty("ledgerSourceLotId").GetGuid()).ToArray();
        var captureSources = lineage.GetProperty("composition").EnumerateArray()
            .Select(x => x.GetProperty("captureSourceLotId").GetGuid()).ToArray();
        Assert.Contains(LedgerScenario.Lots(first)[0], sources);
        Assert.Contains(LedgerScenario.Lots(second)[0], sources);
        Assert.DoesNotContain(LedgerScenario.Lots(third)[0], sources);
        Assert.Contains(first.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid(), captureSources);
        Assert.Contains(second.GetProperty("items")[0].GetProperty("sourceLotId").GetGuid(), captureSources);
        Assert.True(lineage.GetProperty("lineage").GetProperty("complete").GetBoolean());
        await scenario.HoldAsync(LedgerScenario.Lots(third)[0]);
        var afterHold = await writer.GetFromJsonAsync<JsonElement>(route + "&includeExcluded=true");
        var heldItems = afterHold.GetProperty("items").EnumerateArray().ToArray();
        Assert.True(Assert.Single(heldItems, x => x.GetProperty("lotId").GetGuid() == aliquotId)
            .GetProperty("inventoryEligible").GetBoolean());
        var blockedPool = Assert.Single(heldItems, x => x.GetProperty("stockKind").GetString() == "CURRENT_POOL");
        Assert.False(blockedPool.GetProperty("inventoryEligible").GetBoolean());
        Assert.Contains("HOLD_ACTIVE", blockedPool.GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Candidate_read_is_scoped_revisioned_and_read_only()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipt = await scenario.ReceiptAsync("30");
        var lotId = LedgerScenario.Lots(receipt)[0];
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(user, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString());
        var (market, _, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = scenario.CatalogCommodityId,
            requiredMass = "20", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "20", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var route = $"/api/export-containers/{container.Id}/candidates?facilityId={scenario.FacilityId}";
        var before = await StockSnapshotAsync();
        var listResponse = await writer.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var candidate = Assert.Single(list.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(lotId, candidate.GetProperty("lotId").GetGuid());
        Assert.True(candidate.GetProperty("inventoryEligible").GetBoolean());
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("poolRevision").ValueKind);
        Assert.False(list.GetProperty("canPropose").GetBoolean());
        var lineage = await writer.GetFromJsonAsync<JsonElement>($"/api/export-containers/{container.Id}/candidates/{lotId}/lineage?facilityId={scenario.FacilityId}&locationId={scenario.LocationId}");
        Assert.True(lineage.GetProperty("lineage").GetProperty("complete").GetBoolean());
        Assert.Equal(lotId, Assert.Single(lineage.GetProperty("composition").EnumerateArray().ToArray()).GetProperty("ledgerSourceLotId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound,
            (await writer.PostAsJsonAsync($"/api/export-containers/{container.Id}/candidate-selection/validate",
                new { facilityId = scenario.FacilityId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Customer.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.ForeignCustomer.GetAsync(route)).StatusCode);
        Assert.Equal(before, await StockSnapshotAsync());
        await scenario.HoldAsync(lotId);
        Assert.Equal(HttpStatusCode.Conflict,
            (await writer.GetAsync(route + "&scopeRevision=" + list.GetProperty("scopeRevision").GetString())).StatusCode);
        var held = await writer.GetFromJsonAsync<JsonElement>(route + "&includeExcluded=true");
        Assert.Contains("HOLD_ACTIVE", Assert.Single(held.GetProperty("items").EnumerateArray().ToArray())
            .GetProperty("reasons").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Stock_preview_reads_scoped_ledger_positions_and_shows_HOLD_without_claiming_feasibility()
    {
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var receipts = new[] { await scenario.ReceiptAsync("10"), await scenario.ReceiptAsync("20") };
        await scenario.ForeignReceiptAsync("5");
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        using var writer = factory.CreateRealCookieClient(user, session);
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "24", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "KG" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var route = $"/api/export-containers/{container.Id}/stock-preview?facilityId={scenario.FacilityId}";
        var first = (await writer.GetFromJsonAsync<ExportStockPreviewDto>(route + "&pageSize=1"))!;
        Assert.Equal(2, first.TotalCount); Assert.True(first.HasMore); Assert.Single(first.Items);
        Assert.Equal(scenario.OwnerId, (await scenario.LotAsync(first.Items[0].LotId, scenario.Customer)).GetProperty("ownerId").GetGuid());
        var full = (await writer.GetFromJsonAsync<ExportStockPreviewDto>(route))!;
        Assert.Equal(2, full.Items.Count);
        Assert.All(full.Items, x => Assert.Equal(scenario.FacilityId, Assert.Single(x.Positions).FacilityId));
        Assert.Equal(new[] { 10m, 20m }, full.Items.Select(x => decimal.Parse(Assert.Single(x.Positions).AvailableKg,
            System.Globalization.CultureInfo.InvariantCulture)).OrderBy(x => x).ToArray());
        await scenario.HoldAsync(LedgerScenario.Lots(receipts[0])[0]);
        var held = (await writer.GetFromJsonAsync<ExportStockPreviewDto>(route))!;
        var blocked = Assert.Single(held.Items, x => x.LotId == LedgerScenario.Lots(receipts[0])[0]);
        Assert.Contains("HOLD", blocked.Restrictions);
        Assert.Equal("0.000000", Assert.Single(blocked.Positions).AvailableKg);
        var inventoryRoute = $"/api/export-containers/{container.Id}/inventory-scope?facilityId={scenario.FacilityId}";
        var inventory = (await writer.GetFromJsonAsync<ExportInventoryScopeDto>(inventoryRoute))!;
        Assert.Equal(2, inventory.TotalLots);
        Assert.Equal("20", inventory.AvailableMassKg);
        Assert.Equal(1, inventory.RestrictedLots);
        Assert.False(inventory.Revisioned);
        Assert.False(inventory.ComplianceQualified);
        Assert.Equal(HttpStatusCode.BadRequest, (await writer.GetAsync($"/api/export-containers/{container.Id}/stock-preview")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await writer.GetAsync($"/api/export-containers/{container.Id}/inventory-scope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await writer.GetAsync($"/api/export-containers/{Guid.NewGuid()}/stock-preview?facilityId={scenario.FacilityId}")).StatusCode);
        var (outsider, foreignSession) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.ForeignOwnerId);
        using var foreign = factory.CreateRealCookieClient(outsider, foreignSession);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(inventoryRoute)).StatusCode);
    }

    [Fact]
    public async Task Planning_draft_replay_cancellation_and_editable_demand_preserve_stock()
    {
        var (client, order, market, commodity) = await EditableOrderAsync();
        using var writer = client;
        var before = await StockSnapshotAsync();
        var target = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var context = (await writer.GetFromJsonAsync<BlendPlanningContextDto>($"/api/export-containers/{target.Id}/planning-context"))!;
        Assert.True(context.StorageReady); Assert.True(context.CanCreateDraft);
        Assert.True(context.CandidateReadReady); Assert.False(context.CanPropose);
        Assert.Empty(context.MissingDependencies);
        var body = new CreateBlendPlanDraftRequest(Guid.NewGuid(), 1, 1, "HEAD_RICE");
        var route = $"/api/export-containers/{target.Id}/blend-plans";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await writer.PostAsJsonAsync(route + "/drafts", body with { TargetProductIdentity = null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await writer.PostAsJsonAsync(route + "/drafts", body with { TargetProductIdentity = "BAGGED_RICE" })).StatusCode);
        var created = await writer.PostAsJsonAsync(route + "/drafts", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        Assert.Equal("DRAFT", draft.Status); Assert.Equal(1, draft.VersionNo); Assert.Equal("24000", draft.TargetMassKg);
        Assert.Equal("HEAD_RICE", draft.TargetProductIdentity);
        Assert.True(draft.DemandCurrent);
        Assert.Equal("14", JsonDocument.Parse(draft.DemandSnapshot).RootElement.GetProperty("limits")[0].GetProperty("maxValue").GetString());
        Assert.Equal("HEAD_RICE", JsonDocument.Parse(draft.DemandSnapshot).RootElement.GetProperty("targetProductIdentity").GetString());
        var replay = await writer.PostAsJsonAsync(route + "/drafts", body);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
        Assert.Equal(draft.Id, (await replay.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await writer.PostAsJsonAsync(route + "/drafts", body with { OrderVersion = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await writer.PostAsJsonAsync(route + "/drafts", body with { TargetProductIdentity = "BROKEN_RICE" })).StatusCode);
        Assert.True((await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}"))!.CanEdit);
        var update = await PatchAsync(writer, $"/api/export-orders/{order.Id}", 1, new UpdateExportOrderRequest(
            Guid.NewGuid(), market, commodity, "49", "T", order.ShippingFrom, order.ShippingTo));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(writer, $"/api/export-containers/{target.Id}", 1,
            new UpdateExportContainerRequest(Guid.NewGuid(), "25", "T"))).StatusCode);
        using var spec = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/specifications")
        { Content = JsonContent.Create(new AddExportSpecificationRequest(Guid.NewGuid(), order.Limits.Select(x => new ExportLimitInput(x.AttributeId, null, "15")).ToArray())) };
        spec.Headers.TryAddWithoutValidation("If-Match", "\"3\"");
        Assert.Equal(HttpStatusCode.OK, (await writer.SendAsync(spec)).StatusCode);
        var oldDraft = (await writer.GetFromJsonAsync<BlendPlanDraftDto>($"/api/blend-plans/{draft.Id}"))!;
        Assert.Equal("24000", oldDraft.TargetMassKg);
        Assert.Equal("HEAD_RICE", oldDraft.TargetProductIdentity);
        Assert.False(oldDraft.DemandCurrent);
        var cancelBody = new CancelBlendPlanDraftRequest(Guid.NewGuid());
        async Task<HttpResponseMessage> Cancel(int version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/blend-plans/{draft.Id}/cancel") { Content = JsonContent.Create(cancelBody) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            return await writer.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await Cancel(99)).StatusCode);
        var cancelled = await Cancel(1);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("CANCELLED", (await cancelled.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await Cancel(1)).StatusCode);
        Assert.True((await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}"))!.CanEdit);
        Assert.Equal(2, (await writer.GetFromJsonAsync<ExportPage<ExportEventDto>>($"/api/blend-plans/{draft.Id}/history"))!.TotalCount);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await writer.PostAsJsonAsync(route + "/drafts", body with { OperationId = Guid.NewGuid() })).StatusCode);
        var refreshed = await writer.PostAsJsonAsync(route + "/drafts", body with { OperationId = Guid.NewGuid(), OrderVersion = 4, ContainerVersion = 2, TargetProductIdentity = "BROKEN_RICE" });
        Assert.Equal(HttpStatusCode.Created, refreshed.StatusCode);
        var refreshedDraft = (await refreshed.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        Assert.True(refreshedDraft.DemandCurrent);
        Assert.Equal("BROKEN_RICE", refreshedDraft.TargetProductIdentity);
        var page = (await writer.GetFromJsonAsync<ExportPage<BlendPlanDraftDto>>(route + "?pageSize=1"))!;
        Assert.Equal(2, page.TotalCount); Assert.True(page.HasMore); Assert.Equal(2, Assert.Single(page.Items).VersionNo);
        Assert.Equal(before, await StockSnapshotAsync());
    }

    [Fact]
    public async Task Planning_is_company_scoped_and_manager_is_read_only()
    {
        var (client, order, _, _) = await EditableOrderAsync();
        using var writer = client;
        Guid companyId;
        using (var scope = factory.Services.CreateScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<ExportDbContext>().Orders.SingleAsync(x => x.Id == order.Id)).CompanyId;
        var other = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (manager, session) = await factory.SeedUserAsync(Roles.CustomerManager, companyId);
        var (outsider, outsideSession) = await factory.SeedUserAsync(Roles.ExportStaff, other.Id);
        using var reader = factory.CreateRealCookieClient(manager, session);
        using var stranger = factory.CreateRealCookieClient(outsider, outsideSession);
        foreach (var c in new[] { reader, stranger })
            c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", (await c.GetFromJsonAsync<JsonElement>("/api/auth/csrf")).GetProperty("token").GetString());
        var target = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var route = $"/api/export-containers/{target.Id}/blend-plans";
        var body = new CreateBlendPlanDraftRequest(Guid.NewGuid(), 1, 1, "HEAD_RICE");
        var draft = (await (await writer.PostAsJsonAsync(route + "/drafts", body)).Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/blend-plans/{draft.Id}")).StatusCode);
        Assert.False((await reader.GetFromJsonAsync<BlendPlanDraftDto>($"/api/blend-plans/{draft.Id}"))!.CanCancel);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(route + "/drafts", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"/api/blend-plans/{draft.Id}/cancel", new CancelBlendPlanDraftRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/blend-plans/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/blend-plans/{draft.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(route + "/drafts", body)).StatusCode);
    }

    [Fact]
    public async Task Planning_creation_and_target_edit_serialize_and_history_cannot_be_rewritten()
    {
        var (client, order, _, _) = await EditableOrderAsync();
        using var writer = client;
        var target = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!.Items);
        var route = $"/api/export-containers/{target.Id}/blend-plans/drafts";
        var create = writer.PostAsJsonAsync(route, new CreateBlendPlanDraftRequest(Guid.NewGuid(), 1, 1, "HEAD_RICE"));
        var edit = PatchAsync(writer, $"/api/export-containers/{target.Id}", 1, new UpdateExportContainerRequest(Guid.NewGuid(), "25", "T"));
        var outcomes = await Task.WhenAll(create, edit);
        Assert.True(outcomes[0].StatusCode == HttpStatusCode.Created && outcomes[1].StatusCode == HttpStatusCode.OK ||
            outcomes[0].StatusCode == HttpStatusCode.PreconditionFailed && outcomes[1].StatusCode == HttpStatusCode.OK);
        var response = outcomes[0].StatusCode == HttpStatusCode.Created ? outcomes[0] :
            await writer.PostAsJsonAsync(route, new CreateBlendPlanDraftRequest(Guid.NewGuid(), 2, 2, "HEAD_RICE"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<BlendPlanDraftDto>())!;
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        foreach (var sql in new[] {
            "UPDATE exports.blend_plan_drafts SET target_mass_kg=1 WHERE id=$1",
            "DELETE FROM exports.blend_plan_drafts WHERE id=$1",
            "UPDATE exports.blend_plan_events SET action='FAKE' WHERE plan_id=$1" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue(draft.Id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
        await using var direct = new NpgsqlCommand("UPDATE exports.export_orders SET required_mass_kg=49000 WHERE id=$1", connection);
        direct.Parameters.AddWithValue(order.Id);
        Assert.Equal(1, await direct.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Header_edits_preserve_allocation_and_replay_without_overwriting_newer_changes()
    {
        var (client, order, market, commodity) = await EditableOrderAsync();
        using var writer = client;
        var stockBefore = await StockSnapshotAsync();
        object Body(Guid operation, string mass) => new
        {
            operationId = operation, marketId = market, commodityId = commodity,
            requiredMass = mass, unit = "T", shippingFrom = "2026-11-11", shippingTo = "2026-11-21"
        };
        var route = $"/api/export-orders/{order.Id}";
        var tooSmall = await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "23"));
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
        Assert.True((await tooSmall.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("requiredMass", out _));
        var operation = Guid.NewGuid();
        var body = Body(operation, "50");
        var updated = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = (await updated.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        Assert.Equal("50000", saved.RequiredMassKg);
        Assert.Equal("50", saved.OriginalMass);
        Assert.Equal("T", saved.OriginalUnit);
        Assert.Equal("24000", saved.AllocatedMassKg);
        Assert.Equal("26000", saved.RemainingMassKg);
        Assert.Equal(new DateOnly(2026, 11, 11), saved.ShippingFrom);
        Assert.Equal("\"2\"", updated.Headers.ETag!.ToString());
        var newer = await PatchAsync(writer, route, 2, Body(Guid.NewGuid(), "51"));
        Assert.Equal(HttpStatusCode.OK, newer.StatusCode);
        var replay = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("50000", (await replay.Content.ReadFromJsonAsync<ExportOrderDto>())!.RequiredMassKg);
        Assert.Equal(updated.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync(writer, route, 1, Body(operation, "52"))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "53"))).StatusCode);
        var detail = (await writer.GetFromJsonAsync<ExportOrderDto>(route))!;
        Assert.Equal("51000", detail.RequiredMassKg);
        Assert.Equal(3, detail.Version);
        Assert.Equal(1, detail.SpecificationVersion);
        var history = (await writer.GetFromJsonAsync<ExportPage<ExportEventDto>>(route + "/history"))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(2, history.Items.Count(x => x.Action == "ORDER_UPDATED"));
        Assert.Equal(stockBefore, await StockSnapshotAsync());
    }

    [Fact]
    public async Task Target_edits_reject_overallocation_and_stale_versions_and_replay_exactly_once()
    {
        var (client, order, _, _) = await EditableOrderAsync();
        using var writer = client;
        var stockBefore = await StockSnapshotAsync();
        var containers = (await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!;
        var container = Assert.Single(containers.Items);
        var route = $"/api/export-containers/{container.Id}";
        object Body(Guid operation, string mass) => new { operationId = operation, targetMass = mass, unit = "T" };
        var over = await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "49"));
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.True((await over.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("targetMass", out _));
        var operation = Guid.NewGuid();
        var body = Body(operation, "30");
        var updated = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = (await updated.Content.ReadFromJsonAsync<ExportContainerDto>())!;
        Assert.Equal("30000", saved.TargetMassKg);
        Assert.Equal(container.InternalCode, saved.InternalCode);
        Assert.Equal("30", saved.OriginalMass);
        Assert.Equal("T", saved.OriginalUnit);
        Assert.Equal("\"2\"", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(writer, route, 2, Body(Guid.NewGuid(), "31"))).StatusCode);
        var replay = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("30000", (await replay.Content.ReadFromJsonAsync<ExportContainerDto>())!.TargetMassKg);
        Assert.Equal(updated.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync(writer, route, 1, Body(operation, "32"))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "33"))).StatusCode);
        var detail = (await writer.GetFromJsonAsync<ExportContainerDto>(route))!;
        Assert.Equal("31000", detail.TargetMassKg);
        Assert.Equal(3, detail.Version);
        var currentOrder = (await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}"))!;
        Assert.Equal("31000", currentOrder.AllocatedMassKg);
        Assert.Equal("17000", currentOrder.RemainingMassKg);
        Assert.Equal(3, currentOrder.Version);
        var history = (await writer.GetFromJsonAsync<ExportPage<ExportEventDto>>($"/api/export-orders/{order.Id}/history"))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(2, history.Items.Count(x => x.Action == "CONTAINER_UPDATED"));
        Assert.Equal(stockBefore, await StockSnapshotAsync());
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string route, int version, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, route) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request);
    }

    private async Task<(HttpClient Client, ExportOrderDto Order, Guid Market, Guid Commodity)> EditableOrderAsync()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        var writer = factory.CreateRealCookieClient(staff, session);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "48", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (writer, (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!, market, commodity);
    }

    [Fact]
    public async Task Rice_order_only_offers_finished_rice_products_and_rejects_paddy_draft()
    {
        var (writer, order, _, _) = await EditableOrderAsync();
        using var client = writer;
        var container = Assert.Single((await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>(
            $"/api/export-orders/{order.Id}/containers"))!.Items);
        var context = await writer.GetFromJsonAsync<BlendPlanningContextDto>(
            $"/api/export-containers/{container.Id}/planning-context");
        Assert.NotNull(context);
        Assert.Equal(new[] { "HEAD_RICE", "BROKEN_RICE", "BLENDED_RICE" }, context.AllowedTargetProducts);

        var route = $"/api/export-containers/{container.Id}/blend-plans/drafts";
        var invalid = await writer.PostAsJsonAsync(route,
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.Version, container.Version, "RAW_PADDY"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var plans = await writer.GetFromJsonAsync<ExportPage<BlendPlanDraftDto>>(
            $"/api/export-containers/{container.Id}/blend-plans");
        Assert.Empty(plans!.Items);
    }

    private async Task<(long Positions, decimal Mass, long Postings)> StockSnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM ledger.positions),
                   (SELECT coalesce(sum(quantity_kg), 0) FROM ledger.positions),
                   (SELECT count(*) FROM ledger.postings)
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetDecimal(1), reader.GetInt64(2));
    }

    [Fact]
    public async Task Specification_bounds_keep_twelve_decimal_places_in_reads()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        using var writer = factory.CreateRealCookieClient(staff, session);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "0.001", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = "0.000000000001", maxValue = "0.0000001" } },
            containers = new[] { new { targetMass = "0.001", unit = "T" } }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        Assert.Equal("0.000000000001", order.Limits.Single().MinValue);
        Assert.Equal("0.0000001", order.Limits.Single().MaxValue);
        var detail = await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}");
        Assert.Equal(order.Limits, detail!.Limits);
    }

    [Fact]
    public async Task Concurrent_target_additions_cannot_overallocate_an_order()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        using var writer = factory.CreateRealCookieClient(staff, session);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "48", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        HttpRequestMessage Addition()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/containers")
                { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), containers = new[] { new { targetMass = "24", unit = "T" } } }) };
            request.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
            return request;
        }
        using var first = Addition(); using var second = Addition();
        var results = await Task.WhenAll(writer.SendAsync(first), writer.SendAsync(second));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.PreconditionFailed));
        var detail = await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}");
        Assert.Equal("48000", detail!.AllocatedMassKg);
        Assert.Equal("0", detail.RemainingMassKg);
        var targets = await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers?page=1&pageSize=1");
        Assert.Equal(2, targets!.TotalCount);
        Assert.Single(targets.Items);
        Assert.True(targets.HasMore);
    }

    [Fact]
    public async Task Create_replay_scope_and_allocation_guard_use_real_postgres()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var otherCompany = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        var (manager, managerSession) = await factory.SeedUserAsync(Roles.CustomerManager, company.Id);
        var (other, otherSession) = await factory.SeedUserAsync(Roles.ExportStaff, otherCompany.Id);
        using var writer = factory.CreateRealCookieClient(staff, session);
        using var reader = factory.CreateRealCookieClient(manager, managerSession);
        using var outsider = factory.CreateRealCookieClient(other, otherSession);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.True((await writer.GetFromJsonAsync<JsonElement>("/api/export-availability")).GetProperty("ready").GetBoolean());
        var (market, commodity, attribute) = await CatalogAsync();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        async Task<(long Positions, decimal Mass, long Postings)> StockAsync()
        {
            await using var check = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM ledger.positions),
                       (SELECT coalesce(sum(quantity_kg), 0) FROM ledger.positions),
                       (SELECT count(*) FROM ledger.postings)
                """, owner);
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetInt64(0), reader.GetDecimal(1), reader.GetInt64(2));
        }
        var stockBefore = await StockAsync();
        var operation = Guid.NewGuid();
        var payload = new
        {
            operationId = operation, marketId = market, commodityId = commodity,
            requiredMass = "48", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" }, new { targetMass = "24", unit = "T" } }
        };
        var created = await writer.PostAsJsonAsync("/api/export-orders", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        Assert.Matches(@"^EXP-\d{6}-\d{6,}$", order.Code);
        Assert.Equal("48000", order.RequiredMassKg);
        Assert.Equal("0", order.RemainingMassKg);
        var targets = await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers");
        Assert.Equal(2, targets!.TotalCount);
        Assert.All(targets.Items, x => Assert.Matches(@"^CTN-\d{8}-\d{6,}$", x.InternalCode));
        var replay = await writer.PostAsJsonAsync("/api/export-orders", payload);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(order.Id, (await replay.Content.ReadFromJsonAsync<ExportOrderDto>())!.Id);
        var changed = new
        {
            operationId = operation, marketId = market, commodityId = commodity,
            requiredMass = "49", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" } }
        };
        Assert.Equal(HttpStatusCode.Conflict, (await writer.PostAsJsonAsync("/api/export-orders", changed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/export-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/export-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/export-orders", payload)).StatusCode);
        var add = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/containers")
            { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), containers = new[] { new { targetMass = "1", unit = "T" } } }) };
        add.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await writer.SendAsync(add)).StatusCode);
        var specOperation = Guid.NewGuid();
        var spec = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/specifications")
            { Content = JsonContent.Create(new { operationId = specOperation, limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "13" } } }) };
        spec.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        var updated = await writer.SendAsync(spec);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<ExportOrderDto>())!.SpecificationVersion);
        var specPage = await writer.GetFromJsonAsync<ExportPage<ExportSpecificationDto>>(
            $"/api/export-orders/{order.Id}/specifications?page=1&pageSize=1");
        Assert.Equal(2, specPage!.TotalCount);
        Assert.True(specPage.HasMore);
        Assert.Equal(2, Assert.Single(specPage.Items).VersionNo);
        var historyPage = await reader.GetFromJsonAsync<ExportPage<ExportEventDto>>(
            $"/api/export-orders/{order.Id}/history?page=1&pageSize=1");
        Assert.NotNull(historyPage);
        Assert.Equal(staff.DisplayName, Assert.Single(historyPage.Items).ActorName);
        var oldReplay = await writer.PostAsJsonAsync("/api/export-orders", payload);
        Assert.Equal(HttpStatusCode.Created, oldReplay.StatusCode);
        Assert.Equal(1, (await oldReplay.Content.ReadFromJsonAsync<ExportOrderDto>())!.SpecificationVersion);
        var stale = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/specifications")
            { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "12" } } }) };
        stale.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await writer.SendAsync(stale)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
        Assert.Equal(1, await exports.Orders.CountAsync(x => x.CompanyId == company.Id));
        Assert.Equal(2, await exports.Containers.CountAsync(x => x.OrderId == order.Id));
        Assert.Equal(2, await exports.Specifications.CountAsync(x => x.OrderId == order.Id));
        Assert.Equal(stockBefore, await StockAsync());
        await using var rewrite = new NpgsqlCommand("""
            UPDATE exports.specification_limits SET max_value=99
            WHERE specification_id IN (SELECT id FROM exports.order_specifications WHERE order_id=$1)
            """, owner);
        rewrite.Parameters.AddWithValue(order.Id);
        var violation = await Assert.ThrowsAsync<PostgresException>(() => rewrite.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
    }

    private async Task<(Guid Market, Guid Commodity, Guid Attribute)> CatalogAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var market = new Market { Id = Guid.NewGuid(), Code = "TEST-MKT-" + suffix, Name = "Test market" };
        var commodity = await db.Commodities.SingleOrDefaultAsync(x => x.Code == "RICE")
            ?? new Commodity { Id = Guid.NewGuid(), Code = "RICE", Name = "Rice" };
        var attribute = new QualityAttribute { Id = Guid.NewGuid(), Code = "TEST-MOIST-" + suffix, Name = "Moisture", Unit = "%" };
        db.AddRange(market, attribute);
        if (db.Entry(commodity).State == EntityState.Detached) db.Commodities.Add(commodity);
        if (!await db.MeasurementUnits.AnyAsync(x => x.Code == "T"))
            db.MeasurementUnits.Add(new MeasurementUnit { Id = Guid.NewGuid(), Code = "T", Name = "Tonne", QuantityKind = "MASS" });
        await db.SaveChangesAsync();
        return (market.Id, commodity.Id, attribute.Id);
    }
}
