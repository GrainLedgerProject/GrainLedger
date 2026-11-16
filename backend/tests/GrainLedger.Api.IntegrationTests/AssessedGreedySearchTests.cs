using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Features.Ledger;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class AssessedGreedySearchTests
{
    [Fact]
    public async Task UT13_replaces_physical_first_choice_when_assessment_fails()
    {
        var commodity = Guid.NewGuid();
        PlanningCandidate Candidate(decimal kg) => new(Guid.NewGuid(), "SYNTHETIC",
            Guid.NewGuid(), "Synthetic location", Guid.NewGuid(), commodity,
            "HEAD_RICE", "HEAD_RICE", kg, kg, "ORDINARY", false, false, null,
            Guid.NewGuid().ToString(), 1, true, true, [], []);
        var bad = Candidate(10);
        var goodA = Candidate(6);
        var goodB = Candidate(4);
        var result = await AssessedGreedySearch.SearchAsync([bad, goodA, goodB], 10,
            "HEAD_RICE", commodity, JsonSerializer.SerializeToElement(Array.Empty<object>()),
            (selection, _) => Task.FromResult(new BlendAssessmentResult(
                selection.Allocations.Any(x => x.LotId == bad.LotId) ? "FAIL" : "PASS", 10,
                [])), 10, CancellationToken.None);
        Assert.Equal("PASS", result.Outcome);
        Assert.Equal(2, result.Iterations);
        Assert.DoesNotContain(result.Selection!.Allocations, x => x.LotId == bad.LotId);
        Assert.Equal(10, result.Selection.SelectedMassKg);
    }

    [Fact]
    public async Task UT13_iteration_cap_does_not_claim_global_infeasibility()
    {
        var commodity = Guid.NewGuid();
        var candidate = new PlanningCandidate(Guid.NewGuid(), "SYNTHETIC", Guid.NewGuid(),
            "Synthetic location", Guid.NewGuid(), commodity, "HEAD_RICE", "HEAD_RICE",
            10, 10, "ORDINARY", false, false, null, "revision", 1, true, true, [], []);
        var result = await AssessedGreedySearch.SearchAsync([candidate], 10, "HEAD_RICE",
            commodity, JsonSerializer.SerializeToElement(Array.Empty<object>()),
            (_, _) => Task.FromResult(new BlendAssessmentResult("INCOMPLETE", null, [])),
            1, CancellationToken.None);
        Assert.Equal("SEARCH_EXHAUSTED", result.Outcome);
        Assert.Equal("INCOMPLETE", result.Assessment?.Status);
        Assert.Contains("ITERATION_LIMIT_REACHED", result.Reasons);
    }

    [Fact]
    public async Task UT13_reports_search_exhaustion_when_only_rebalancing_the_same_lots_can_pass()
    {
        const decimal targetKg = 200m;
        const string blendMode = "BLEND_INPUT_STOCK";
        var commodity = Guid.NewGuid();
        var inputCommodity = Guid.NewGuid();
        var market = Guid.NewGuid();
        var substance = Guid.NewGuid();
        var moisture = Guid.NewGuid();
        PlanningCandidate Candidate(string product, decimal availableKg) =>
            new(Guid.NewGuid(), product, Guid.NewGuid(), "Test store", inputCommodity, commodity,
                product, product, availableKg, availableKg, "ORDINARY", false, false, null,
                "revision", 1, true, true, [], []);
        var lotA = Candidate("HEAD_RICE", 140m);
        var lotB = Candidate("BROKEN_RICE", 80m);
        var compatibility = JsonSerializer.SerializeToElement(new[] { new
        {
            id = Guid.NewGuid(),
            outputCatalogCommodityId = commodity,
            inputProducts = new[] { inputCommodity + ":HEAD_RICE", inputCommodity + ":BROKEN_RICE" }
        } });

        var yield = new BlendYieldRule(Guid.NewGuid(), 1, 1m, "Synthetic yield");
        var policy = new BlendAssessmentRule(Guid.NewGuid(), 1, market, commodity,
            [substance], 0.1m, "AS_RECEIVED", "Synthetic assessment policy");
        var mrl = new ApplicableMrlResult("FOUND", Guid.NewGuid(), 1, null, null, null,
            [new ApplicableMrlLimit(substance, 0.01m, "mg/kg")], MarketId: market, CommodityId: commodity);
        var bound = new ResidueResult(substance, "COMPLETE", "0", "0.001", "mg/kg",
            [], [], [], [], "synthetic-profile", commodity, "BLENDED_RICE", "BLENDED_RICE");
        var limit = new SpecificationLimit { Id = Guid.NewGuid(), AttributeId = moisture,
            AttributeCode = "MOISTURE", AttributeName = "Moisture", Unit = "%", MaxValue = 14m };
        var rule = new BlendQualityRule(Guid.NewGuid(), 1, moisture, "%", "MASS_WEIGHTED_MEAN",
            "Synthetic quality rule");
        var observations = new BlendQualityObservation[]
        {
            new(lotA.LotId, moisture, 15m, "%", Guid.NewGuid(), 1),
            new(lotB.LotId, moisture, 12.5m, "%", Guid.NewGuid(), 1)
        };

        BlendAssessmentResult Assess(string mode, IReadOnlyList<CandidateGroupItem> allocations) =>
            BlendAssessment.Evaluate(mode, targetKg, allocations, yield, policy, mrl, [bound],
                [limit], [rule], observations);

        var result = await AssessedGreedySearch.SearchAsync([lotA, lotB], targetKg,
            "BLENDED_RICE", commodity, compatibility,
            (selection, _) => Task.FromResult(Assess(selection.Mode, selection.Allocations
                .Select(x => new CandidateGroupItem(x.LotId, x.LocationId, x.QuantityKg, x.CandidateRevision))
                .ToArray())), 10, CancellationToken.None);

        Assert.Equal("SEARCH_EXHAUSTED", result.Outcome);
        Assert.Contains("SEARCH_SPACE_EXHAUSTED", result.Reasons);
        var selection = Assert.IsType<GreedyStockSelection.Result>(result.Selection);
        Assert.Equal(blendMode, selection.Mode);
        Assert.Equal(targetKg, selection.SelectedMassKg);
        Assert.Equal(140m, Assert.Single(selection.Allocations, x => x.LotId == lotA.LotId).QuantityKg);
        Assert.Equal(60m, Assert.Single(selection.Allocations, x => x.LotId == lotB.LotId).QuantityKg);
        var assessment = Assert.IsType<BlendAssessmentResult>(result.Assessment);
        Assert.Equal("FAIL", assessment.Status);
        var failedCheck = Assert.Single(assessment.Findings, x => x.Status != "PASS");
        Assert.Equal(("QUALITY_LIMIT", "14.25"), (failedCheck.Code, failedCheck.Actual));

        var feasible = Assess(blendMode,
            [new(lotA.LotId, lotA.LocationId, 120m, lotA.CandidateRevision),
             new(lotB.LotId, lotB.LocationId, 80m, lotB.CandidateRevision)]);
        Assert.Equal("PASS", feasible.Status);
        Assert.Equal(targetKg, feasible.PredictedOutputKg);
        Assert.Equal("14", Assert.Single(feasible.Findings, x => x.Code == "QUALITY_LIMIT").Actual);
    }
}
