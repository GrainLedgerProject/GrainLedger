using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Exports;
using GrainLedger.Modules.Ledger;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BlendAssessmentTests
{
    private static readonly Guid Market = Guid.NewGuid();
    private static readonly Guid Commodity = Guid.NewGuid();
    private static readonly Guid Substance = Guid.NewGuid();
    private static readonly Guid Attribute = Guid.NewGuid();
    private static readonly Guid Lot = Guid.NewGuid();
    private static readonly CandidateGroupItem[] Allocation =
        [new(Lot, Guid.NewGuid(), 10m, "revision")];

    private static BlendAssessmentRule Policy => new(Guid.NewGuid(), 1, Market, Commodity,
        [Substance], 0.1m, "AS_RECEIVED", "approved synthetic policy");
    private static ApplicableMrlResult Mrl => new("FOUND", Guid.NewGuid(), 1, null, null,
        null, [new ApplicableMrlLimit(Substance, 0.01m, "mg/kg")],
        MarketId: Market, CommodityId: Commodity);
    private static ResidueResult Bound(decimal upper) => new(Substance, "COMPLETE", "0",
        upper.ToString(System.Globalization.CultureInfo.InvariantCulture), "mg/kg",
        "AS_RECEIVED", [], [], [], []);
    private static SpecificationLimit QualityLimit => new()
    {
        Id = Guid.NewGuid(), AttributeId = Attribute, AttributeCode = "MOISTURE",
        AttributeName = "Moisture", Unit = "%", MaxValue = 14m
    };
    private static BlendQualityRule QualityRule => new(Guid.NewGuid(), 1, Attribute,
        "%", "MASS_WEIGHTED_MEAN", "approved synthetic rule");
    private static BlendQualityObservation Observation => new(Lot, Attribute, 13m, "%",
        Guid.NewGuid(), 1);

    [Fact]
    public void MissingPoliciesAndEvidenceFailClosed()
    {
        var result = BlendAssessment.Evaluate("BLEND_INPUT_STOCK", 9m, Allocation,
            null, null, new ApplicableMrlResult("NOT_AVAILABLE", null, null, null,
                null, null, []), null, [QualityLimit], [], []);
        Assert.Equal("INCOMPLETE", result.Status);
        Assert.Contains(result.Findings, x => x.Code == "OUTPUT_YIELD_POLICY_MISSING");
        Assert.Contains(result.Findings, x => x.Code == "ASSESSMENT_POLICY_MISSING");
        Assert.Contains(result.Findings, x => x.Code == "QUALITY_RULE_MISSING");
    }

    [Fact]
    public void ExplicitSyntheticRulesCanPassWithoutAssumingDefaultYieldOrMargin()
    {
        var result = BlendAssessment.Evaluate("BLEND_INPUT_STOCK", 9m, Allocation,
            new BlendYieldRule(Guid.NewGuid(), 1, 0.9m, "approved synthetic yield"),
            Policy, Mrl, [Bound(0.008m)], [QualityLimit], [QualityRule], [Observation]);
        Assert.Equal("PASS", result.Status);
        Assert.Equal(9m, result.PredictedOutputKg);
        Assert.All(result.Findings, x => Assert.Equal("PASS", x.Status));
    }

    [Fact]
    public void ProvenMrlFailureIsNotHiddenByMissingQualityEvidence()
    {
        var result = BlendAssessment.Evaluate("DIRECT_STOCK", 10m, Allocation,
            null, Policy, Mrl, [Bound(0.0091m)], [QualityLimit], [QualityRule], []);
        Assert.Equal("FAIL", result.Status);
        Assert.Contains(result.Findings, x => x.Code == "RESIDUE_LIMIT" && x.Status == "FAIL");
        Assert.Contains(result.Findings, x => x.Code == "QUALITY_EVIDENCE_MISSING" && x.Status == "INCOMPLETE");
    }

    [Fact]
    public void ConflictingQualityEvidenceCannotBeChosenSilently()
    {
        var result = BlendAssessment.Evaluate("DIRECT_STOCK", 10m, Allocation,
            null, Policy, Mrl, [Bound(0.001m)], [QualityLimit], [QualityRule],
            [Observation, Observation with { EvidenceId = Guid.NewGuid(), Value = 15m }]);
        Assert.Equal("INCOMPLETE", result.Status);
        Assert.Contains(result.Findings, x => x.Code == "QUALITY_EVIDENCE_MISSING");
    }

    [Fact]
    public void Direct_lots_do_not_average_away_a_failing_quality_result()
    {
        var second = Guid.NewGuid();
        var allocations = new[] { Allocation[0],
            new CandidateGroupItem(second, Guid.NewGuid(), 10m, "revision") };
        var result = BlendAssessment.Evaluate("DIRECT_STOCK", 20m, allocations,
            null, Policy, Mrl, [Bound(0.001m)], [QualityLimit], [],
            [Observation with { Value = 13m },
                Observation with { LotId = second, Value = 15m }]);
        Assert.Equal("FAIL", result.Status);
        Assert.Contains(result.Findings, x => x.Code == "QUALITY_LIMIT" &&
            x.Status == "FAIL" && x.Actual == "13..15");
    }

    [Fact]
    public void Direct_lots_use_the_highest_residue_upper_bound()
    {
        var first = new ResidueEvaluationPreview(Guid.NewGuid(), Guid.NewGuid(),
            DateTimeOffset.UtcNow, "synthetic", [Bound(0.001m)]);
        var second = first with { LotId = Guid.NewGuid(), Results = [Bound(0.0091m)] };
        var bounds = DirectStockResidueAssessment.ConservativeBounds(
            [first, second], [Substance]);
        Assert.Equal("0.0091", Assert.Single(bounds).Upper);
    }
}
