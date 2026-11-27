using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Features.Identity;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class RiceVarietyEligibilityTests
{
    [Theory]
    [InlineData("SINGLE", true, null)]
    [InlineData("SINGLE", false, "VARIETY_MISMATCH")]
    [InlineData("UNKNOWN", true, "VARIETY_UNKNOWN")]
    [InlineData("MIXED", true, "VARIETY_MIXED")]
    public void Registered_identity_and_status_determine_eligibility(string status, bool same, string? expected)
    {
        var required = Guid.NewGuid();
        Assert.Equal(expected, RiceVarietyEligibility.Reason(required, same ? required : Guid.NewGuid(), status, "HEAD_RICE"));
    }

    [Fact]
    public void Missing_requirement_and_missing_identity_fail_closed_but_byproducts_keep_existing_policy()
    {
        var variety = Guid.NewGuid();
        Assert.Equal("VARIETY_REQUIREMENT_MISSING", RiceVarietyEligibility.Reason(null, variety, "SINGLE", "BLENDED_RICE"));
        Assert.Equal("VARIETY_UNKNOWN", RiceVarietyEligibility.Reason(variety, null, "SINGLE", "BROKEN_RICE"));
        Assert.Null(RiceVarietyEligibility.Reason(null, null, "MIXED", "BRAN"));
        Assert.Null(RiceVarietyEligibility.FromDemand("{}"));
        Assert.Equal(variety, RiceVarietyEligibility.FromDemand(JsonSerializer.Serialize(new { requiredRiceVarietyId = variety })));
    }
}

public sealed class F14RiceVarietyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Rice_order_requires_registered_variety_and_specification_versions_preserve_history()
    {
        using var s = await LedgerScenario.CreateAsync(factory);
        using var writer = await Writer(s);
        var rice = await SendAsync(s.Admin, HttpMethod.Post, "/api/commodities", new { code = "RICE", name = "Rice" });
        var (market, attribute) = await Catalog(s);
        JsonObject Body(Guid? variety) => JsonSerializer.SerializeToNode(new {
            operationId = Guid.NewGuid(), marketId = market, commodityId = rice.GetProperty("id").GetGuid(),
            requiredRiceVarietyId = variety, requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, maxValue = "14" } }, containers = new[] { new { targetMass = "10", unit = "KG" } }
        })!.AsObject();
        using (var missing = await writer.PostAsJsonAsync("/api/export-orders", Body(null))) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var invalid = await writer.PostAsJsonAsync("/api/export-orders", Body(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var order = await SendAsync(writer, HttpMethod.Post, "/api/export-orders", Body(s.RiceVarietyId));
        var orderId = order.GetProperty("id").GetGuid();
        var route = $"/api/export-orders/{orderId}/specifications";
        async Task<JsonElement> Revise(Guid? variety, int version) {
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), requiredRiceVarietyId = variety, limits = new[] { new { attributeId = attribute, maxValue = "13.9" } } }) };
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var response = await writer.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        var preserved = await Revise(null, order.GetProperty("version").GetInt32());
        Assert.Equal(s.RiceVarietyId, preserved.GetProperty("requiredRiceVarietyId").GetGuid());
        var other = await SendAsync(s.Admin, HttpMethod.Post, "/api/rice-varieties", new { code = "OM" + Guid.NewGuid().ToString("N"), name = "OM5451" });
        var changed = await Revise(other.GetProperty("id").GetGuid(), preserved.GetProperty("version").GetInt32());
        Assert.Equal(other.GetProperty("id").GetGuid(), changed.GetProperty("requiredRiceVarietyId").GetGuid());
        var history = await GetAsync(writer, route);
        var first = history.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("versionNo").GetInt32() == 1);
        Assert.Equal(s.RiceVarietyId, first.GetProperty("requiredRiceVarietyId").GetGuid());
    }

    [Fact]
    public async Task Candidate_read_excludes_other_variety_and_forged_selection_is_rejected_before_residue_assessment()
    {
        using var s = await LedgerScenario.CreateAsync(factory);
        using var writer = await Writer(s);
        var receipt = await s.ReceiptAsync("10");
        var head = await Mill(s, Lots(receipt)[0]);
        var otherVariety = await SendAsync(s.Admin, HttpMethod.Post, "/api/rice-varieties", new { code = "OM" + Guid.NewGuid().ToString("N"), name = "OM5451" });
        var farmer = Guid.NewGuid(); var plot = Guid.NewGuid(); var plotSeason = Guid.NewGuid();
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/farmers", CaptureCommand(new { entityId = farmer, name = "Other farmer", contact = "+84000000000" }, 2));
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/plots", CaptureCommand(new { entityId = plot, farmerId = farmer, areaHa = 1, geometry = new { type = "Point", coordinates = new[] { 106.1m, 10.1m } } }, 2));
        await SendAsync(s.Purchaser, HttpMethod.Post, "/api/plot-seasons", CaptureCommand(new { entityId = plotSeason, plotId = plot, seasonId = s.SeasonId, riceVarietyId = otherVariety.GetProperty("id").GetGuid() }));
        var otherReceipt = await SendAsync(s.Purchaser, HttpMethod.Post, "/api/procurements", s.ReceiptBody(Guid.NewGuid(), ["10"], farmerId: farmer, plotSeasonId: plotSeason));
        await s.ConfirmArrivalAsync(otherReceipt);
        var otherHead = await Mill(s, Lots(otherReceipt)[0]);
        var (market, attribute) = await Catalog(s);
        var order = await SendAsync(writer, HttpMethod.Post, "/api/export-orders", new {
            operationId = Guid.NewGuid(), marketId = market, commodityId = s.CatalogCommodityId, requiredRiceVarietyId = (Guid?)null,
            requiredMass = "10", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, maxValue = "14" } }, containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        var containers = await GetAsync(writer, $"/api/export-orders/{order.GetProperty("id").GetGuid()}/containers");
        var container = containers.GetProperty("items")[0];
        using (var legacy = await writer.PostAsJsonAsync($"/api/export-containers/{container.GetProperty("id").GetGuid()}/blend-plans/drafts",
            new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.GetProperty("version").GetInt32(), container.GetProperty("version").GetInt32(), "HEAD_RICE")))
        {
            Assert.Equal(HttpStatusCode.Conflict, legacy.StatusCode);
            Assert.Equal("VARIETY_REQUIREMENT_MISSING", (await legacy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using var revise = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.GetProperty("id").GetGuid()}/specifications") {
            Content = JsonContent.Create(new { operationId = Guid.NewGuid(), requiredRiceVarietyId = s.RiceVarietyId, limits = new[] { new { attributeId = attribute, maxValue = "14" } } }) };
        revise.Headers.TryAddWithoutValidation("If-Match", $"\"{order.GetProperty("version").GetInt32()}\"");
        using var revised = await writer.SendAsync(revise);
        Assert.True(revised.IsSuccessStatusCode, await revised.Content.ReadAsStringAsync());
        order = await revised.Content.ReadFromJsonAsync<JsonElement>();
        var draft = await SendAsync(writer, HttpMethod.Post, $"/api/export-containers/{container.GetProperty("id").GetGuid()}/blend-plans/drafts", new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.GetProperty("version").GetInt32(), container.GetProperty("version").GetInt32(), "HEAD_RICE"));
        var route = $"/api/export-containers/{container.GetProperty("id").GetGuid()}/candidates?facilityId={s.FacilityId}&planId={draft.GetProperty("id").GetGuid()}";
        var eligible = await GetAsync(writer, route);
        Assert.Contains(eligible.GetProperty("items").EnumerateArray(), x => x.GetProperty("lotId").GetGuid() == head);
        Assert.DoesNotContain(eligible.GetProperty("items").EnumerateArray(), x => x.GetProperty("lotId").GetGuid() == otherHead);
        var all = await GetAsync(writer, route + "&includeExcluded=true");
        var wrong = all.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("lotId").GetGuid() == otherHead);
        Assert.Equal("VARIETY_MISMATCH", wrong.GetProperty("varietyExclusion").GetString());
        var forged = await SendAsync(writer, HttpMethod.Post, $"/api/blend-plans/{draft.GetProperty("id").GetGuid()}/residue-preview", new {
            facilityId = s.FacilityId, scopeRevision = all.GetProperty("scopeRevision").GetString(), mode = "DIRECT_STOCK",
            selected = new[] { new { lotId = otherHead, locationId = wrong.GetProperty("locationId").GetGuid(), quantityKg = "10", candidateRevision = wrong.GetProperty("candidateRevision").GetString() } },
            substanceIds = new[] { Guid.NewGuid() }
        });
        Assert.Equal("INVALID", forged.GetProperty("status").GetString());
        Assert.Contains(forged.GetProperty("findings").EnumerateArray(), x => x.GetString() == "VARIETY_MISMATCH");
        var fractional = await SendAsync(writer, HttpMethod.Post, $"/api/blend-plans/{draft.GetProperty("id").GetGuid()}/residue-preview", new {
            facilityId = s.FacilityId, scopeRevision = all.GetProperty("scopeRevision").GetString(), mode = "DIRECT_STOCK",
            selected = new[] { new { lotId = head, locationId = s.LocationId, quantityKg = "9.5", candidateRevision = eligible.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("lotId").GetGuid() == head).GetProperty("candidateRevision").GetString() } },
            substanceIds = new[] { Guid.NewGuid() }
        });
        Assert.Equal("INVALID", fractional.GetProperty("status").GetString());
        Assert.Contains(fractional.GetProperty("findings").EnumerateArray(), x => x.GetString() == "QUANTITY_WHOLE_KG_REQUIRED");
        Assert.Equal("NOT_ASSESSED", fractional.GetProperty("assessmentStatus").GetString());
        using var change = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.GetProperty("id").GetGuid()}/specifications") {
            Content = JsonContent.Create(new { operationId = Guid.NewGuid(), requiredRiceVarietyId = otherVariety.GetProperty("id").GetGuid(), limits = new[] { new { attributeId = attribute, maxValue = "14" } } }) };
        change.Headers.TryAddWithoutValidation("If-Match", $"\"{order.GetProperty("version").GetInt32()}\"");
        using var changed = await writer.SendAsync(change); Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        using var stale = await writer.PostAsJsonAsync($"/api/blend-plans/{draft.GetProperty("id").GetGuid()}/preview", new PreviewBlendPlanRequest(s.FacilityId, all.GetProperty("scopeRevision").GetString()!));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(10m, Quantity(await s.LotAsync(head)));
        Assert.Equal(10m, Quantity(await s.LotAsync(otherHead)));
    }

    private async Task<HttpClient> Writer(LedgerScenario s)
    {
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, s.OwnerId);
        var writer = factory.CreateRealCookieClient(user, session); await CsrfAsync(writer); return writer;
    }

    private static async Task<(Guid Market, Guid Attribute)> Catalog(LedgerScenario s)
    {
        var market = await SendAsync(s.Admin, HttpMethod.Post, "/api/markets", new { code = "V" + Guid.NewGuid().ToString("N"), name = "Synthetic market" });
        var attribute = await SendAsync(s.Admin, HttpMethod.Post, "/api/quality-attributes", new { code = "M" + Guid.NewGuid().ToString("N"), name = "Moisture", unit = "%" });
        return (market.GetProperty("id").GetGuid(), attribute.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> Mill(LedgerScenario s, Guid raw)
    {
        var dry = await s.DraftAsync("DRYING", [(raw, "10")], [("DRIED_PADDY", "10")]); await s.CommitAsync(dry);
        var head = await s.DraftAsync("MILLING", [(Output(dry), "10")], [("HEAD_RICE", "10")]); await s.CommitAsync(head); return Output(head);
    }
}
