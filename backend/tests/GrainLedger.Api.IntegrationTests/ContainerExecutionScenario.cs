using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Features.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LabReportTests;

namespace GrainLedger.Api.IntegrationTests;

internal sealed class ContainerExecutionScenario : IDisposable
{
    internal required LedgerScenario Stock { get; init; }
    internal required HttpClient Export { get; init; }
    internal required Guid ActorId { get; init; }
    internal required Guid ContainerId { get; init; }
    internal required Guid PlanId { get; init; }
    internal required Guid ProposalId { get; init; }
    internal required Guid LotId { get; init; }
    internal required Guid SourceId { get; init; }
    internal required Guid QualityAttributeId { get; init; }
    internal string Route => $"/api/export-containers/{ContainerId}/execution";
    internal static async Task<ContainerExecutionScenario> CreateAsync(ApiFactory factory, bool preBlended = false)
    {
        var stock = await LedgerScenario.CreateAsync(factory);
        var residue = await ResidueScenario.CreateAsync(stock);
        var receipt = await stock.ReceiptAsync("20");
        var sample = await SampleAsync(stock, receipt);
        var (_, report) = await residue.ReportAsync(sample);
        await residue.PublishRuleAsync(); await residue.SelectAsync(sample, report);
        await residue.ProcessRuleAsync("DRYING", "RAW_PADDY", "DRIED_PADDY");
        await residue.ProcessRuleAsync("MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5", "0.5");
        var dried = LedgerScenario.Output(await stock.CommitAsync(await stock.DraftAsync("DRYING", [(sample.LotId, "20")], [("DRIED_PADDY", "20")])));
        var lot = LedgerScenario.Output(await stock.CommitAsync(await stock.DraftAsync("MILLING", [(dried, "20")], [("HEAD_RICE", "10"), ("BROKEN_RICE", "10")])));
        if (preBlended)
        {
            var secondSample = await SampleAsync(stock, await stock.ReceiptAsync("20"));
            var (_, secondReport) = await residue.ReportAsync(secondSample);
            await residue.SelectAsync(secondSample, secondReport);
            var secondDried = LedgerScenario.Output(await stock.CommitAsync(await stock.DraftAsync("DRYING", [(secondSample.LotId, "20")], [("DRIED_PADDY", "20")])));
            var secondHead = LedgerScenario.Output(await stock.CommitAsync(await stock.DraftAsync("MILLING", [(secondDried, "20")], [("HEAD_RICE", "10"), ("BROKEN_RICE", "10")])));
            await residue.ProcessRuleAsync("BLENDING", "HEAD_RICE", "BLENDED_RICE");
            await residue.BlendCompatibilityAsync("HEAD_RICE");
            lot = LedgerScenario.Output(await stock.CommitAsync(await stock.DraftAsync("BLENDING", [(lot, "10"), (secondHead, "10")], [("BLENDED_RICE", "20")])));
        }
        var market = Guid.NewGuid(); var attribute = Guid.NewGuid(); var mrl = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Markets.Add(new Market { Id = market, Code = "M" + market.ToString("N"), Name = "Synthetic container market" });
            db.QualityAttributes.Add(new QualityAttribute { Id = attribute, Code = "Q" + attribute.ToString("N"), Name = "Synthetic quality", Unit = "%" });
            db.MrlVersions.Add(new MrlVersion
            {
                Id = mrl,
                MarketId = market,
                CommodityId = stock.CatalogCommodityId,
                VersionNo = 1,
                Status = "PUBLISHED",
                SourceRef = "Synthetic legal reference",
                PublishedBy = stock.Accounts["customer"].User.Id,
                PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                EffectiveFrom = DateTimeOffset.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync();
            db.MrlLimits.Add(new MrlLimit { Id = Guid.NewGuid(), MrlVersionId = mrl, SubstanceId = residue.SubstanceId, LimitValue = 1m, Unit = "mg/kg" });
            await db.SaveChangesAsync();
        }
        await LedgerScenario.SendAsync(stock.Customer, HttpMethod.Post, "/api/ledger/planning-quality-observations", new
        {
            id = Guid.NewGuid(),
            ownerId = stock.OwnerId,
            facilityId = stock.FacilityId,
            lotId = lot,
            attributeId = attribute,
            versionNo = 1,
            measuredValue = "13",
            unit = "%",
            measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            evidenceId = stock.EvidenceId,
            reportReference = "SYNTHETIC-QC",
            reviewedBy = "Synthetic reviewer",
            reviewAuthority = "Synthetic authority",
            reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            subjectConfirmed = true
        });
        await LedgerScenario.SendAsync(stock.Customer, HttpMethod.Post, "/api/blend-assessment-policies", new PublishBlendAssessmentPolicyRequest(
            Guid.NewGuid(), market, stock.CatalogCommodityId, 1, DateTimeOffset.UtcNow.AddMinutes(-1), null, [residue.SubstanceId], "0.1", ResidueScenario.Basis,
            [new PublishedQualityPropagationRule(attribute, "%", "MASS_WEIGHTED_MEAN")], "Synthetic reviewed policy", "Synthetic reviewer", "Synthetic authority", DateTimeOffset.UtcNow.AddMinutes(-2)));
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, stock.OwnerId);
        var export = factory.CreateRealCookieClient(user, session); await LedgerScenario.CsrfAsync(export);
        var order = await LedgerScenario.SendAsync(export, HttpMethod.Post, "/api/export-orders", new
        {
            operationId = Guid.NewGuid(),
            marketId = market,
            commodityId = stock.CatalogCommodityId,
            requiredMass = "10",
            unit = "KG",
            shippingFrom = "2026-11-10",
            shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "10", unit = "KG" } }
        });
        var containers = await LedgerScenario.GetAsync(export, $"/api/export-orders/{order.GetProperty("id").GetGuid()}/containers");
        var container = Assert.Single(containers.GetProperty("items").EnumerateArray()); var cid = container.GetProperty("id").GetGuid();
        var draft = await LedgerScenario.SendAsync(export, HttpMethod.Post, $"/api/export-containers/{cid}/blend-plans/drafts", new CreateBlendPlanDraftRequest(
            Guid.NewGuid(), order.GetProperty("version").GetInt32(), container.GetProperty("version").GetInt32(), preBlended ? "BLENDED_RICE" : "HEAD_RICE"));
        var pid = draft.GetProperty("id").GetGuid();
        var candidates = await LedgerScenario.GetAsync(export, $"/api/export-containers/{cid}/candidates?facilityId={stock.FacilityId}&planId={pid}");
        var preview = await LedgerScenario.SendAsync(export, HttpMethod.Post, $"/api/blend-plans/{pid}/preview", new PreviewBlendPlanRequest(stock.FacilityId, candidates.GetProperty("scopeRevision").GetString()!));
        Assert.Equal("PASS", preview.GetProperty("assessmentStatus").GetString());
        var proposal = await LedgerScenario.SendAsync(export, HttpMethod.Post, $"/api/blend-plans/{pid}/propose", new ProposeBlendPlanRequest(Guid.NewGuid(), preview.GetProperty("previewReference").GetGuid()));
        var prop = proposal.GetProperty("id").GetGuid();
        await LedgerScenario.SendAsync(stock.Customer, HttpMethod.Post, $"/api/blend-plans/{pid}/approve", new ApproveBlendPlanRequest(Guid.NewGuid(), prop, 0));
        return new() { Stock = stock, Export = export, ActorId = user.Id, ContainerId = cid, PlanId = pid, ProposalId = prop, LotId = lot, SourceId = sample.LotId, QualityAttributeId = attribute };
    }
    internal ActualLoadRequest Load(int version, string kg) => new(Guid.NewGuid(), version, ProposalId, 1, DateTimeOffset.UtcNow, [new(LotId, Stock.LocationId, kg)]);
    internal Task<JsonElement> Detail() => LedgerScenario.GetAsync(Export, Route);
    internal Task<JsonElement> Post(string action, object request) => LedgerScenario.SendAsync(Export, HttpMethod.Post, Route + "/" + action, request);
    public void Dispose() { Export.Dispose(); Stock.Dispose(); }
}
