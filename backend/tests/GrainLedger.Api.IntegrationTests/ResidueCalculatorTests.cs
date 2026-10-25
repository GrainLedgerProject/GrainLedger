using System.Globalization;
using GrainLedger.Modules.Ledger;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueCalculatorTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Commodity = Guid.NewGuid();
    private static readonly Guid Substance = Guid.NewGuid();
    private static readonly DateTimeOffset At = DateTimeOffset.UtcNow;
    private const string Basis = ResidueScenario.Basis;

    [Fact]
    public void Committed_mass_weighted_intervals_and_fraction_factors_match_worked_example()
    {
        var a = Source(); var b = Source();
        var mixed = Node("BLENDING", "HEAD_RICE", [(a.LotId, "60"), (b.LotId, "40")]);
        var sources = new[] { Bound(a, "0.1", "0.2"), Bound(b, "0.3", "0.8") };
        var nodes = new[] { a, b, mixed };
        var result = Calculate(mixed, nodes, sources, [Factor("BLENDING", "RAW_PADDY", "HEAD_RICE", "1", "1")]);
        Complete(result, "0.18", "0.44");
        var fraction = Calculate(mixed, nodes, sources, [Factor("BLENDING", "RAW_PADDY", "HEAD_RICE", "0.5", "0.5")]);
        Complete(fraction, "0.09", "0.22");
        Assert.Contains(fraction.Dependencies, x => x.ParentLotId == a.LotId && x.QuantityKg == "60");
        Assert.Contains(fraction.Dependencies, x => x.ParentLotId == b.LotId && x.QuantityKg == "40");
    }

    [Fact]
    public void Mixed_fraction_blend_requires_each_parent_mapping_and_keeps_both_rule_versions()
    {
        var source = Source();
        var head = Node("MILLING", "HEAD_RICE", [(source.LotId, "60")]);
        var broken = Node("MILLING", "BROKEN_RICE", [(source.LotId, "40")]);
        var blended = Node("BLENDING", "BLENDED_RICE", [(head.LotId, "60"), (broken.LotId, "40")]);
        var headBlend = Factor("BLENDING", "HEAD_RICE", "BLENDED_RICE", "1", "1");
        var brokenBlend = Factor("BLENDING", "BROKEN_RICE", "BLENDED_RICE", "1", "1");
        var nodes = new[] { source, head, broken, blended };
        var sources = new[] { Bound(source, "0.1", "0.2") };
        var millFactors = new[]
        {
            Factor("MILLING", "RAW_PADDY", "HEAD_RICE", "0.5", "0.5"),
            Factor("MILLING", "RAW_PADDY", "BROKEN_RICE", "2", "2")
        };
        var result = Calculate(blended, nodes, sources, [.. millFactors, headBlend, brokenBlend]);
        Complete(result, "0.11", "0.22");
        Assert.Contains(result.Rules, x => x.Id == headBlend.Rule.Id);
        Assert.Contains(result.Rules, x => x.Id == brokenBlend.Rule.Id);
        Unresolved(Calculate(blended, nodes, sources, [.. millFactors, headBlend]));
    }

    [Theory]
    [InlineData("HEAD_RICE", "0.5", "0.5", "0.05", "0.1")]
    [InlineData("BROKEN_RICE", "0.8", "1.2", "0.08", "0.24")]
    [InlineData("BRAN", "2", "3", "0.2", "0.6")]
    public void Reviewed_fraction_intervals_apply_once_without_output_yield_adjustment(string stage, string lower, string upper, string expectedLower, string expectedUpper)
    {
        var source = Source();
        var output = Node("MILLING", stage, [(source.LotId, "100")]);
        Complete(Calculate(output, [source, output], [Bound(source, "0.1", "0.2")],
            [Factor("MILLING", "RAW_PADDY", stage, lower, upper)]), expectedLower, expectedUpper);
    }

    [Fact]
    public void Cumulative_factor_bypasses_intermediate_factor_instead_of_multiplying_twice()
    {
        var source = Source();
        var dried = Node("DRYING", "DRIED_PADDY", [(source.LotId, "100")]);
        var milled = Node("MILLING", "HEAD_RICE", [(dried.LotId, "80")]);
        Complete(Calculate(milled, [source, dried, milled], [Bound(source, "0.1", "0.2")],
        [
            Factor("DRYING", "RAW_PADDY", "DRIED_PADDY", "2", "2"),
            Factor("MILLING", "RAW_PADDY", "HEAD_RICE", "0.5", "0.5", semantics: "CUMULATIVE")
        ]), "0.05", "0.1");
    }

    [Fact]
    public void Cumulative_rewind_across_mixture_does_not_guess_original_source_mass_weights()
    {
        var a = Source(); var b = Source();
        var mixedDrying = Node("DRYING", "DRIED_PADDY", [(a.LotId, "60"), (b.LotId, "40")]);
        var milled = Node("MILLING", "HEAD_RICE", [(mixedDrying.LotId, "80")]);
        Unresolved(Calculate(milled, [a, b, mixedDrying, milled], [Bound(a, "0.1", "0.2"), Bound(b, "0.3", "0.8")],
        [
            Factor("DRYING", "RAW_PADDY", "DRIED_PADDY", "2", "2"),
            Factor("MILLING", "RAW_PADDY", "HEAD_RICE", "0.5", "0.5", semantics: "CUMULATIVE")
        ]));
    }

    [Fact]
    public void Reconverging_root_keeps_each_physical_path_factor_and_mass()
    {
        var source = Source();
        var first = Node("DRYING", "DRIED_PADDY", [(source.LotId, "60")]);
        var second = Node("MILLING", "DRIED_PADDY", [(source.LotId, "40")]);
        var mixed = Node("BLENDING", "DRIED_PADDY", [(first.LotId, "60"), (second.LotId, "40")]);
        Complete(Calculate(mixed, [source, first, second, mixed], [Bound(source, "0.1", "0.2")],
        [
            Factor("DRYING", "RAW_PADDY", "DRIED_PADDY", "0.5", "0.5"),
            Factor("MILLING", "RAW_PADDY", "DRIED_PADDY", "2", "2"),
            Factor("BLENDING", "DRIED_PADDY", "DRIED_PADDY", "1", "1")
        ]), "0.11", "0.22");
    }

    [Fact]
    public void Tiny_positive_parent_survives_zero_accounting_share_and_upper_rounds_outward()
    {
        var tiny = Source(); var large = Source();
        var mixed = Node("MERGE", "RAW_PADDY", [(tiny.LotId, "0.000001"), (large.LotId, "999999")]);
        var result = Calculate(mixed, [tiny, large, mixed], [Bound(tiny, "1", "1"), Bound(large, "0", "0")],
            [Factor("MERGE", "RAW_PADDY", "RAW_PADDY", "1", "1", precision: 12)]);
        Complete(result, "0.000000000001", "0.000000000002");
        Assert.Contains(result.Dependencies, x => x.ParentLotId == tiny.LotId);
        var unresolved = Calculate(mixed, [tiny, large, mixed], [Bound(large, "0", "0")],
            [Factor("MERGE", "RAW_PADDY", "RAW_PADDY", "1", "1", precision: 12)]);
        Unresolved(unresolved);
    }

    [Fact]
    public void Exact_thirds_round_only_final_endpoints_not_intermediate_paths()
    {
        var a = Source(); var b = Source();
        var oneThird = Node("MERGE", "RAW_PADDY", [(a.LotId, "1"), (b.LotId, "2")]);
        var output = Node("BAGGING", "BAGGED_RICE", [(oneThird.LotId, "3")]);
        Complete(Calculate(output, [a, b, oneThird, output], [Bound(a, "1", "1", 2), Bound(b, "0", "0", 2)],
        [
            Factor("MERGE", "RAW_PADDY", "RAW_PADDY", "1", "1", precision: 2),
            Factor("BAGGING", "RAW_PADDY", "BAGGED_RICE", "3", "3", precision: 2)
        ]), "1", "1");
        Complete(Calculate(oneThird, [a, b, oneThird], [Bound(a, "1", "1", 2), Bound(b, "0", "0", 2)],
            [Factor("MERGE", "RAW_PADDY", "RAW_PADDY", "1", "1", precision: 2)]), "0.33", "0.34");
    }

    [Theory]
    [InlineData("missing-parent")]
    [InlineData("unknown-upper")]
    [InlineData("missing-factor")]
    [InlineData("wrong-basis")]
    [InlineData("conflicting-factor")]
    [InlineData("inconsistent-history")]
    [InlineData("different-parent-basis")]
    public void Unsupported_dependency_or_rule_never_returns_finite_complete_bounds(string fault)
    {
        var a = Source(); var b = Source();
        var output = Node("DRYING", "DRIED_PADDY", [(a.LotId, "60"), (b.LotId, "40")]);
        var nodes = new List<ResidueNode> { a, b, output };
        var sources = new List<ResidueSourceBound> { Bound(a, "0.1", "0.2"), Bound(b, "0.3", "0.8") };
        var factors = new List<ResidueFactor> { Factor("DRYING", "RAW_PADDY", "DRIED_PADDY", "1", "1") };
        switch (fault)
        {
            case "missing-parent": nodes.Remove(b); break;
            case "unknown-upper": sources[1] = sources[1] with { Upper = null, Reasons = ["UNKNOWN_UPPER"] }; break;
            case "missing-factor": factors.Clear(); break;
            case "wrong-basis": factors[0] = factors[0] with { InputBasis = "UNAPPROVED_DRY_MATTER" }; break;
            case "conflicting-factor": factors.Add(Factor("DRYING", "RAW_PADDY", "DRIED_PADDY", "0.5", "0.5")); break;
            case "inconsistent-history": nodes[2] = output with { Inconsistent = true }; break;
            case "different-parent-basis": sources[1] = sources[1] with { Basis = "OTHER_BASIS" }; break;
        }
        Unresolved(Calculate(output, nodes, sources, factors));
    }

    [Fact]
    public void Custody_aliquot_and_remainder_preserve_concentration_without_remaining_balance_weights()
    {
        var a = Source(); var b = Source();
        var pool = Node("CUSTODY_INGRESS", "RAW_PADDY", [(a.LotId, "60"), (b.LotId, "40")]);
        var aliquot = Node("CUSTODY_WITHDRAW", "RAW_PADDY", [(pool.LotId, "10")]);
        var remainder = Node("CUSTODY_WITHDRAW", "RAW_PADDY", [(pool.LotId, "90")]);
        var nodes = new[] { a, b, pool, aliquot, remainder };
        var factors = new[] { Factor("CUSTODY_INGRESS", "RAW_PADDY", "RAW_PADDY", "1", "1"), Factor("CUSTODY_WITHDRAW", "RAW_PADDY", "RAW_PADDY", "1", "1") };
        var sources = new[] { Bound(a, "0.1", "0.2"), Bound(b, "0.3", "0.8") };
        Complete(Calculate(aliquot, nodes, sources, factors), "0.18", "0.44");
        Complete(Calculate(remainder, nodes, sources, factors), "0.18", "0.44");
    }

    [Fact]
    public void Superseding_rule_with_changed_process_does_not_reactivate_withdrawn_factor()
    {
        var source = Source();
        var dried = Node("DRYING", "DRIED_PADDY", [(source.LotId, "100")]);
        var original = Factor("DRYING", "RAW_PADDY", "DRIED_PADDY", "1", "1");
        var replacement = Factor("MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5", "0.5") with
        {
            SupersedesId = original.Rule.Id,
            EffectiveFrom = At.AddMinutes(-1)
        };
        Unresolved(Calculate(dried, [source, dried], [Bound(source, "0.1", "0.2")],
            [original, replacement]));
        var beforeEffective = replacement with { EffectiveFrom = At.AddMinutes(1) };
        Complete(Calculate(dried, [source, dried], [Bound(source, "0.1", "0.2")],
            [original, beforeEffective]), "0.1", "0.2");
    }

    private static ResidueNode Source() => Node("SOURCE", "RAW_PADDY", []);
    private static ResidueNode Node(string kind, string stage, (Guid LotId, string Mass)[] parents) =>
        new(Guid.NewGuid(), Company, Commodity, stage, stage, At, kind,
            kind == "SOURCE" ? null : Guid.NewGuid(), null, parents.Select(x => new ResidueParent(x.LotId, x.Mass)).ToArray());
    private static ResidueSourceBound Bound(ResidueNode node, string lower, string upper, int precision = 12) =>
        new(node.LotId, Substance, lower, upper, "mg/kg", Basis, precision, [],
            [new ResidueProvenance("DIRECT", node.LotId, RuleId: Guid.NewGuid())], []);
    private static ResidueFactor Factor(string kind, string input, string output, string lower, string upper, string semantics = "STEP", int precision = 12) =>
        new(Substance, Commodity, kind, input, output, Basis, Basis, "mg/kg", lower, upper, precision, semantics, null,
            new ResidueRuleReference(Guid.NewGuid(), 1, kind, ResidueScenario.Review), HomogeneousMixing: true);
    private static ResidueResult Calculate(ResidueNode target, IReadOnlyList<ResidueNode> nodes, IReadOnlyList<ResidueSourceBound> sources, IReadOnlyList<ResidueFactor> factors) =>
        Assert.Single(ResidueCalculator.Calculate(target.LotId, [Substance], nodes, sources, factors));
    private static void Complete(ResidueResult result, string lower, string upper)
    {
        Assert.Equal("COMPLETE", result.Status);
        Assert.Equal(decimal.Parse(lower, CultureInfo.InvariantCulture), decimal.Parse(result.Lower!, CultureInfo.InvariantCulture));
        Assert.Equal(decimal.Parse(upper, CultureInfo.InvariantCulture), decimal.Parse(result.Upper!, CultureInfo.InvariantCulture));
        Assert.Empty(result.Reasons);
    }
    private static void Unresolved(ResidueResult result)
    {
        Assert.Equal("UNRESOLVED", result.Status);
        Assert.Null(result.Lower); Assert.Null(result.Upper); Assert.NotEmpty(result.Reasons);
    }
}
