using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Features.Exports;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ContainerSeasonBenchmarkTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Synthetic_season_fanout_returns_all_1000_actual_containers_and_exact_50_source_shares_within_30_seconds()
    {
        const int sourceCount = 50, containerCount = 1000;
        using var f = await ContainerExecutionScenario.CreateAsync(factory);
        await f.Residue.BlendCompatibilityAsync("HEAD_RICE");
        var roots = new List<Guid>(); var heads = new List<(Guid, string)>();
        for (var i = 0; i < sourceCount; i++)
        {
            var sample = await LabReportTests.SampleAsync(f.Stock, await f.Stock.ReceiptAsync("1000000"));
            var (_, report) = await f.Residue.ReportAsync(sample); await f.Residue.SelectAsync(sample, report); roots.Add(sample.LotId);
            var dry = Output(await f.Stock.CommitAsync(await f.Stock.DraftAsync("DRYING", [(sample.LotId, "1000000")], [("DRIED_PADDY", "1000000")])));
            var head = Output(await f.Stock.CommitAsync(await f.Stock.DraftAsync("MILLING", [(dry, "1000000")], [("HEAD_RICE", "500000"), ("BROKEN_RICE", "500000")])));
            heads.Add((head, "500000"));
        }
        var blended = Output(await f.Stock.CommitAsync(await f.Stock.DraftAsync("BLENDING", heads.ToArray(), [("BLENDED_RICE", "25000000")])));
        await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/planning-quality-observations", new
        {
            id = Guid.NewGuid(), ownerId = f.Stock.OwnerId, facilityId = f.Stock.FacilityId, lotId = blended, attributeId = f.QualityAttributeId,
            versionNo = 1, measuredValue = "13", unit = "%", measuredAt = DateTimeOffset.UtcNow.AddMinutes(-2), evidenceId = f.Stock.EvidenceId,
            reportReference = "Synthetic season quality", reviewedBy = "Synthetic reviewer", reviewAuthority = "Isolated benchmark only", reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1), subjectConfirmed = true
        });
        var old = (await f.Detail()).GetProperty("container"); var originalOrder = await GetAsync(f.Export, $"/api/export-orders/{old.GetProperty("orderId").GetGuid()}");
        var order = await SendAsync(f.Export, HttpMethod.Post, "/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = originalOrder.GetProperty("marketId").GetGuid(), commodityId = f.Stock.CatalogCommodityId,
            requiredRiceVarietyId = f.Stock.RiceVarietyId, requiredMass = "25000000", unit = "KG", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = f.QualityAttributeId, maxValue = "14", minValue = (string?)null } }, containers = new[] { new { targetMass = "25000", unit = "KG" } }
        });
        var container = (await GetAsync(f.Export, $"/api/export-orders/{order.GetProperty("id").GetGuid()}/containers")).GetProperty("items")[0];
        var id = container.GetProperty("id").GetGuid();
        var draft = await SendAsync(f.Export, HttpMethod.Post, $"/api/export-containers/{id}/blend-plans/drafts", new CreateBlendPlanDraftRequest(Guid.NewGuid(), order.GetProperty("version").GetInt32(), 1, "BLENDED_RICE"));
        var plan = draft.GetProperty("id").GetGuid();
        var candidates = await GetAsync(f.Export, $"/api/export-containers/{id}/candidates?facilityId={f.Stock.FacilityId}&planId={plan}");
        var preview = await SendAsync(f.Export, HttpMethod.Post, $"/api/blend-plans/{plan}/preview", new PreviewBlendPlanRequest(f.Stock.FacilityId, candidates.GetProperty("scopeRevision").GetString()!));
        Assert.Equal("PASS", preview.GetProperty("assessmentStatus").GetString());
        var proposal = await SendAsync(f.Export, HttpMethod.Post, $"/api/blend-plans/{plan}/propose", new ProposeBlendPlanRequest(Guid.NewGuid(), preview.GetProperty("previewReference").GetGuid()));
        var proposed = proposal.GetProperty("id").GetGuid();
        await SendAsync(f.Stock.Customer, HttpMethod.Post, $"/api/blend-plans/{plan}/approve", new ApproveBlendPlanRequest(Guid.NewGuid(), proposed, 0));
        var evaluated = await GetAsync(f.Export, $"/api/export-containers/{id}/execution");
        var basis = evaluated.GetProperty("readiness"); Assert.Equal("PASS", basis.GetProperty("assessmentStatus").GetString());
        // Bulk setup is explicitly separate from the functional API/browser proof. Same measured source blend,
        // requirement and genuine assessment; fifty official source records retain original Capture provenance.
        var template = new ContainerExecutionScenario { Stock = f.Stock, Residue = f.Residue, Export = f.Export, ActorId = f.ActorId,
            ContainerId = id, PlanId = plan, ProposalId = proposed, LotId = blended, SourceId = roots[0], QualityAttributeId = f.QualityAttributeId, LoadingLocationId = f.Stock.LocationId };
        var containers = await ContainerRetainedTraceFixture.SeedAsync(factory, template, blended, 25000m, containerCount, basis);
        var forwardTimes = new List<double>(); var backwardTimes = new List<double>();
        for (var run = 0; run < 3; run++)
        {
            var clock = Stopwatch.StartNew();
            var forward = await SendAsync(f.Stock.Customer, HttpMethod.Post, "/api/ledger/trace", new { lotId = roots[0], direction = "forward" });
            clock.Stop(); forwardTimes.Add(clock.Elapsed.TotalMilliseconds);
            var targets = forward.GetProperty("actualContainers").EnumerateArray().ToArray();
            Assert.Equal(containerCount, targets.Length);
            Assert.True(containers.ToHashSet().SetEquals(targets.Select(x => x.GetProperty("containerId").GetGuid())));
            Assert.All(targets, x => Assert.Equal("25000.000000", x.GetProperty("linkedLoadedKg").GetString()));
            clock.Restart();
            var backward = await SendAsync(f.Stock.Customer, HttpMethod.Post, $"/api/export-containers/{containers[^1]}/execution/trace", new { });
            clock.Stop(); backwardTimes.Add(clock.Elapsed.TotalMilliseconds);
            Assert.True(backward.GetProperty("complete").GetBoolean());
            var sources = backward.GetProperty("sourceContributions").EnumerateArray().ToArray(); Assert.Equal(sourceCount, sources.Length);
            Assert.True(roots.ToHashSet().SetEquals(sources.Select(x => x.GetProperty("sourceLotId").GetGuid())));
            Assert.All(sources, x => Assert.Equal("500.000000", x.GetProperty("quantityKg").GetString()));
            Assert.Equal(25000m, sources.Sum(x => decimal.Parse(x.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture)));
        }
        Assert.All(forwardTimes.Concat(backwardTimes), ms => Assert.True(ms < 30000, $"Trace exceeded NFR target: {ms:F1}ms"));
        var metrics = new { profile = "SYNTHETIC_SEASON_50_SOURCES_1000_CONTAINERS", sourceCount, containerCount, processingOperations = 101,
            loadedKg = "25000000.000000", containerKg = "25000.000000", sourceKgPerContainer = "500.000000", sourceRows = 50000,
            forwardHttpAndParseMs = forwardTimes, backwardHttpAndParseMs = backwardTimes, Environment.ProcessorCount,
            conditions = "Disposable PostgreSQL; in-process authenticated real HTTP handlers and JSON parse; warm application/database; no network/TLS or page timing. Bulk history fixture uses a real evaluated basis; not 1000 manual UI runs or production acceptance." };
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "container-s2-season-benchmark.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
    }
}
