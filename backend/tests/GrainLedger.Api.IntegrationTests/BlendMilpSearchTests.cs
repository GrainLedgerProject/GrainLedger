using System.Globalization;
using System.Text.Json;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Exports;
using GrainLedger.Api.Features.Ledger;
using Xunit;
using Xunit.Abstractions;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BlendMilpSearchTests(ITestOutputHelper output)
{
    private record Lot(decimal Kg, decimal? Moisture, decimal Broken, decimal? X = .020m,
        decimal Y = .020m, bool Eligible = true, bool BrokenRice = false, int? Location = null, bool OtherVariety = false, string VarietyStatus = "SINGLE");
    private record Fixture(decimal Target, decimal MaxMoisture, decimal MaxBroken, Lot[] Lots,
        bool Pass = true, decimal Yield = 1, bool Direct = false, bool Compatibility = true,
        bool Minimize = false, bool TwoSubstances = false, bool RetainHeadRice = false,
        BlendPlanningObjective Priority = BlendPlanningObjective.None);

    [Theory]
    [InlineData("BL-01")]
    [InlineData("BL-02")]
    [InlineData("BL-03")]
    [InlineData("BL-04")]
    [InlineData("BL-05")]
    [InlineData("BL-06")]
    [InlineData("BL-07")]
    [InlineData("BL-08")]
    [InlineData("BL-09")]
    [InlineData("BL-10")]
    [InlineData("BL-11")]
    [InlineData("BL-12")]
    [InlineData("BL-13")]
    [InlineData("BL-14")]
    [InlineData("BL-15")]
    public async Task Scenario_uses_the_same_model_for_each_order(string id)
    {
        Fixture[] fixtures = id switch
        {
            "BL-01" => [new(1000, 14, 5, [new(600, 13.2m, 3.5m), new(400, 13.8m, 4.5m)])],
            "BL-02" => [new(1000, 14, 5, [new(980, 13.1m, 1), new(200, 13, 98, .040m, BrokenRice: true)])],
            "BL-03" => [new(200, 14, 5, [new(140, 14.7m, 3.2m), new(80, 12.95m, 4.2m)])],
            "BL-04" => [new(2100, 14, 5, [new(1200, 13.1m, 3.2m), new(800, 13.6m, 4.4m)], Pass: false)],
            "BL-05" => [new(1000, 14, 5, [new(600, 14.15m, 3.5m), new(550, 14.35m, 4.8m)], Pass: false)],
            "BL-06" => [new(1200, 14, 5, [new(750, 14.30m, 2.2m, .085m, .038m),
                new(650, 13, 7.4m, .099m, .028m), new(500, 13.55m, 4, .075m, .045m)], TwoSubstances: true)],
            "BL-07" => [new(1850, 14, 5, [new(1000, 13.25m, 4), new(800, 13.55m, 4.2m),
                new(1000, 13.3m, 3.9m, Eligible: false), new(400, 13.45m, 3.85m, Eligible: false)], Pass: false)],
            "BL-08" => [new(1500, 14, 5, [new(850, 13.1m, 3), new(650, 14.5m, 4)], Pass: false, Direct: true),
                new(1500, 14, 5, [new(850, 13.1m, 3), new(650, 14.5m, 4)])],
            "BL-09" => [new(1200, 14, 25, [new(950, 13.6m, 4.5m), new(600, 13.2m, 98, BrokenRice: true)],
                Pass: false, Compatibility: false),
                new(1200, 14, 25, [new(950, 13.6m, 4.5m), new(600, 13.2m, 98, BrokenRice: true)])],
            "BL-10" => [new(3000, 14, 5, [new(1400, 13.1m, 2.6m), new(1100, 13.6m, 4),
                new(500, 13.2m, 98, BrokenRice: true), new(350, 13.5m, 96, BrokenRice: true)], Pass: false),
                new(3000, 14, 25, [new(1400, 13.1m, 2.6m), new(1100, 13.6m, 4),
                new(500, 13.2m, 98, BrokenRice: true), new(350, 13.5m, 96, BrokenRice: true)])],
            "BL-11" => [new(2940, 14, 5, [new(1700, 13.2m, 3), new(1310, 13.8m, 4.4m)], Yield: .98m)],
            "BL-12" => [new(6000, 14, 5, [new(3000, 13.15m, 3.8m), new(2500, null, 4),
                new(800, 13.55m, 4.2m), new(1100, 13.3m, 4.4m)], Pass: false),
                new(6000, 14, 5, [new(3000, 13.15m, 3.8m), new(2500, 13.35m, 4),
                new(800, 13.55m, 4.2m), new(1100, 13.3m, 4.4m, null)]),
                new(6000, 14, 5, [new(3000, 13.15m, 3.8m), new(2500, 13.35m, 4),
                new(800, 13.55m, 4.2m), new(1100, 13.3m, 4.4m)])],
            "BL-13" => [new(1500, 13.75m, 5, [new(900, 14.6m, 1), new(700, 12.8m, 8), new(500, 13.5m, 4)])],
            "BL-14" => [
                // C1 and C2 can each be proposed against the initial stock.
                new(12000, 14, 5, [new(8000, 13.1m, 3), new(5000, 13.7m, 4.5m),
                    new(7000, 13.6m, 8), new(5000, 13.3m, 15)]),
                new(12000, 14, 25, [new(8000, 13.1m, 3), new(5000, 13.7m, 4.5m),
                    new(7000, 13.6m, 8), new(5000, 13.3m, 15)]),
                // Availability after reserving 8000 A + 4000 C for C2.
                new(12000, 14, 5, [new(5000, 13.7m, 4.5m), new(3000, 13.6m, 8),
                    new(5000, 13.3m, 15)], Pass: false),
                // Replan C2 after release and reserving 8000 A + 4000 B for C1.
                new(12000, 14, 25, [new(1000, 13.7m, 4.5m), new(7000, 13.6m, 8),
                    new(5000, 13.3m, 15)])],
            "BL-15" => [new(9800, 14, 5, [new(8000, 14.2m, 2), new(6500, 13.4m, 3),
                new(2600, 13.8m, 4), new(2000, 13.5m, 4.5m)], Yield: .98m, Minimize: true)],
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        foreach (var fixture in fixtures)
        {
            var (milp, greedy) = await RunAsync(fixture);
            Assert.Equal(fixture.Pass, milp.Selection is not null);
            if (id is "BL-03" or "BL-06" or "BL-13") Assert.NotEqual("PASS", greedy.Outcome);
            if (id == "BL-15")
            {
                Assert.Equal(2, milp.Positions);
                Assert.True(milp.Optimal);
                Assert.NotNull(milp.Range);
                Assert.Equal("PASS", greedy.Outcome);
                Assert.Equal(3, greedy.Selection!.Allocations.Count);
                var attempt = Assert.Single(milp.Attempts, entry => entry.AssessmentStatus == "PASS");
                output.WriteLine($"BL-15: {attempt.Status}; {attempt.Variables} variables, {attempt.Constraints} constraints; " +
                    $"objective {attempt.Objective}, bound {attempt.BestBound}, nodes {attempt.Nodes}; " +
                    $"allocations {string.Join(", ", milp.Selection!.Allocations.Select(a => $"{a.LotCode}={a.QuantityKg} kg"))}.");
            }
        }
    }

    [Theory]
    [InlineData((int)BlendPlanningObjective.None)]
    [InlineData((int)BlendPlanningObjective.FewestLocations)]
    [InlineData((int)BlendPlanningObjective.FewestLots)]
    [InlineData((int)BlendPlanningObjective.LowestMoisture)]
    [InlineData((int)BlendPlanningObjective.RetainHeadRice)]
    [InlineData((int)BlendPlanningObjective.ResidueHeadroom)]
    public async Task Every_objective_excludes_other_mixed_and_unknown_varieties(int objective)
    {
        var (result, _) = await RunAsync(new(100, 14, 5,
            [new(100, 10, 1, .001m, OtherVariety: true),
             new(100, 10, 1, .001m, VarietyStatus: "MIXED"),
             new(100, 10, 1, .001m, VarietyStatus: "UNKNOWN"),
             new(60, 13, 3), new(60, 13.5m, 4)], Priority: (BlendPlanningObjective)objective));
        Assert.NotNull(result.Selection);
        Assert.All(result.Selection!.Allocations, x => Assert.Contains(x.LotCode, new[] { "TEST-3", "TEST-4" }));
        Assert.Equal(100m, result.Selection.Allocations.Sum(x => x.QuantityKg));
    }

    [Fact]
    public async Task Shared_location_counts_once_with_different_lot_order_and_fractional_target()
    {
        var (milp, _) = await RunAsync(new(103.987m, 14, 5,
            [new(50, 13, 4, Location: 33), new(75, 13.5m, 4), new(55, 13.6m, 4, Location: 33)],
            Minimize: true));
        Assert.Equal(1, milp.Positions);
        Assert.Equal(2, milp.Selection!.Allocations.Count);
        Assert.True(milp.Optimal);
    }

    [Theory]
    [InlineData(5, "959")]
    [InlineData(4, "970")]
    public async Task Retaining_head_rice_minimizes_kg_within_the_broken_limit(int maxBroken, string expectedKg)
    {
        var (milp, _) = await RunAsync(new(1000, 14, maxBroken,
            [new(200, 13, 98, .040m, BrokenRice: true), new(980, 13.1m, 1)], RetainHeadRice: true));
        var allocations = Assert.IsType<GreedyStockSelection.Result>(milp.Selection).Allocations;
        Assert.Equal(1000m, allocations.Sum(a => a.QuantityKg));
        Assert.Equal(decimal.Parse(expectedKg, CultureInfo.InvariantCulture), allocations.OrderBy(a => a.QuantityKg).Last().QuantityKg);
    }

    [Fact]
    public async Task Fewest_lots_prefers_one_lot_even_when_two_lots_share_a_location()
    {
        var (milp, _) = await RunAsync(new(100, 14, 5,
            [new(60, 13, 3, Location: 1), new(60, 13, 3, Location: 1), new(100, 13, 3, Location: 2)],
            Priority: BlendPlanningObjective.FewestLots));
        Assert.Single(milp.Selection!.Allocations);
        Assert.True(milp.Optimal);
    }

    [Fact]
    public async Task Lowest_moisture_maximizes_use_of_the_drier_lot_within_quality_limits()
    {
        var (milp, _) = await RunAsync(new(100, 14, 5,
            [new(80, 12, 6), new(100, 14, 2)],
            Priority: BlendPlanningObjective.LowestMoisture));
        Assert.Equal(new[] { 25m, 75m }, milp.Selection!.Allocations.Select(x => x.QuantityKg).Order().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Residue_headroom_minimizes_the_highest_fraction_of_each_substances_own_limit(bool direct)
    {
        var (milp, _) = await RunAsync(new(100, 14, 5,
            [new(100, 13, 3, .090m, .009m), new(100, 13, 3, .018m, .045m), new(100, 13, 3, .065m, .033m)],
            Direct: direct, TwoSubstances: true, Priority: BlendPlanningObjective.ResidueHeadroom));
        var allocations = milp.Selection!.Allocations;
        if (direct)
        {
            Assert.Single(allocations);
            Assert.Equal(100m, allocations[0].QuantityKg);
            Assert.InRange(Assert.Single(milp.Attempts).Objective!.Value, .73333, .73334);
        }
        else
        {
            Assert.Equal(new[] { 50m, 50m }, allocations.Select(x => x.QuantityKg).Order().ToArray());
            Assert.Equal(.6, Assert.Single(milp.Attempts).Objective!.Value, 8);
        }
    }

    private static async Task<(AssessedMilpSearch.Result Milp, AssessedGreedySearch.Result Greedy)> RunAsync(Fixture f)
    {
        var commodity = Guid.NewGuid();
        var ledgerCommodity = Guid.NewGuid();
        var market = Guid.NewGuid();
        var compatibility = Guid.NewGuid();
        var xId = Guid.NewGuid();
        var yId = Guid.NewGuid();
        var moisture = Guid.NewGuid();
        var broken = Guid.NewGuid();
        var candidates = f.Lots.Select((lot, i) =>
        {
            var product = lot.BrokenRice ? "BROKEN_RICE" : "HEAD_RICE";
            return new PlanningCandidate(Guid.NewGuid(), $"TEST-{i}",
                lot.Location is { } location ? new Guid(location, 0, 0, new byte[8]) : Guid.NewGuid(),
                "Test store", ledgerCommodity, commodity, product, product, lot.Kg, lot.Kg,
                "ORDINARY", false, false, null, "revision", 1, true, lot.Eligible, [], [], RiceVarietyId: lot.OtherVariety ? Guid.NewGuid() : commodity, VarietyStatus: lot.VarietyStatus);
        }).ToArray();
        var lots = candidates.Select((candidate, i) => (candidate.LotId, f.Lots[i]))
            .ToDictionary(item => item.LotId, item => item.Item2);
        var rules = JsonSerializer.SerializeToElement(f.Compatibility ? new[] { new
        {
            id = compatibility, outputCatalogCommodityId = commodity, outputLedgerCommodityId = ledgerCommodity,
            inputProducts = candidates.Select(c => $"{ledgerCommodity}:{c.ProductIdentity}").Distinct().ToArray()
        }} : []);
        var snapshot = new PlanningCandidateSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "PLANNED",
            commodity, f.Target, DateTimeOffset.UtcNow, "revision", default, rules, candidates, RequiredRiceVarietyId: commodity);
        var policy = new BlendAssessmentRule(Guid.NewGuid(), 1, market, commodity, f.TwoSubstances ? [xId, yId] : [xId],
            .10m, "AS_RECEIVED", "test");
        var mrl = new ApplicableMrlResult("FOUND", Guid.NewGuid(), 1, null, null, null,
            [new(xId, .100m, "mg/kg"), new(yId, .050m, "mg/kg")], MarketId: market, CommodityId: commodity);
        var limits = new SpecificationLimit[]
        {
            new() { AttributeId = moisture, Unit = "%", MaxValue = f.MaxMoisture },
            new() { AttributeId = broken, Unit = "%", MaxValue = f.MaxBroken }
        };
        var qcRules = limits.Select(l => new BlendQualityRule(policy.Id, 1, l.AttributeId, "%", "MASS_WEIGHTED_MEAN", "test")).ToArray();
        var observations = candidates.SelectMany(c => lots[c.LotId].Moisture is { } m
            ? new[] { new BlendQualityObservation(c.LotId, moisture, m, "%", Guid.NewGuid(), 1),
                new BlendQualityObservation(c.LotId, broken, lots[c.LotId].Broken, "%", Guid.NewGuid(), 1) }
            : []).ToArray();

        ResidueResult Bound(Guid substance, decimal? value) => new(substance, value.HasValue ? "COMPLETE" : "INCOMPLETE",
            null, value?.ToString("0.000", CultureInfo.InvariantCulture), "mg/kg", [], [], [], [], "bench", commodity,
            "BLENDED_RICE", "BLENDED_RICE");

        Task<IReadOnlyDictionary<Guid, IReadOnlyList<ResidueResult>>> Bounds(Guid[] ids, Guid? output, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<ResidueResult>>>(ids.ToDictionary(id => id,
                id => (IReadOnlyList<ResidueResult>)[Bound(xId, lots[id].X), Bound(yId, lots[id].Y)]));

        Task<BlendAssessmentResult> Assess(GreedyStockSelection.Result selection, CancellationToken ct)
        {
            var chosen = selection.Allocations;
            decimal? Upper(Func<Lot, decimal?> get)
            {
                var values = chosen.Select(a => (Value: get(lots[a.LotId]), Kg: a.QuantityKg)).ToArray();
                if (values.Any(v => v.Value is null)) return null;
                var concentration = selection.Mode == "DIRECT_STOCK"
                    ? values.Max(v => v.Value!.Value) : values.Sum(v => v.Value!.Value * v.Kg) / values.Sum(v => v.Kg);
                return decimal.Ceiling(concentration * 1000) / 1000;
            }
            return Task.FromResult(BlendAssessment.Evaluate(selection.Mode, f.Target, chosen.Select(a =>
                    new CandidateGroupItem(a.LotId, a.LocationId, a.QuantityKg, a.CandidateRevision)).ToArray(),
                new BlendYieldRule(compatibility, 1, f.Yield, "test"), policy, mrl,
                [Bound(xId, Upper(l => l.X)), Bound(yId, Upper(l => l.Y))], limits, qcRules, observations));
        }
        decimal? Required(Guid id) => AssessedMilpSearch.MinimumBlendInput(f.Target, f.Yield);
        var target = f.Direct ? "HEAD_RICE" : "BLENDED_RICE";
        var priority = f.RetainHeadRice ? BlendPlanningObjective.RetainHeadRice
            : f.Minimize ? BlendPlanningObjective.FewestLocations : f.Priority;
        var milp = await AssessedMilpSearch.SearchAsync(snapshot, target, limits, qcRules, observations,
            policy, mrl, Required, priority, 32, TimeSpan.FromSeconds(5), Bounds, Assess, CancellationToken.None, moisture);
        var greedy = await AssessedGreedySearch.SearchAsync(candidates, f.Target, target, commodity, rules,
            Assess, 32, CancellationToken.None, Required);
        if (milp.Selection is { } selection)
        {
            var verified = await Assess(selection, CancellationToken.None);
            Assert.Equal("PASS", verified.Status);
            Assert.True(verified.PredictedOutputKg >= f.Target);
            Assert.All(selection.Allocations, a => Assert.Equal(decimal.Truncate(a.QuantityKg), a.QuantityKg));
        }
        return (milp, greedy);
    }
}
