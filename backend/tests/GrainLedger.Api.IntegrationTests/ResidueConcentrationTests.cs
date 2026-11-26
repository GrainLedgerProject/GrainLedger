using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Ledger;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueConcentrationTests
{
    [Theory]
    [InlineData("µg/kg")]
    [InlineData("μg/kg")]
    [InlineData("ug/kg")]
    public void Microgram_evidence_scales_value_limits_and_reviewed_uncertainty_exactly_once(string unit)
    {
        var f = Fixture();
        var raw = f.Result with { Unit = unit, NumericValue = "150", Lod = "10", Loq = "20", Uncertainty = "±50 as reported" };
        var selection = f.Selection with { AbsoluteUncertainty = "50" };
        var normalized = ResidueConcentration.Normalize(raw, selection);
        Assert.Null(normalized.Reason);
        Assert.Equal("0.15", normalized.Evidence.NumericValue);
        Assert.Equal("0.01", normalized.Evidence.Lod);
        Assert.Equal("0.02", normalized.Evidence.Loq);
        Assert.Equal("0.05", normalized.Selection.AbsoluteUncertainty);
        var actual = f.Resolve(raw, selection);
        var canonical = f.Resolve(f.Result with { Uncertainty = raw.Uncertainty }, f.Selection with { AbsoluteUncertainty = "0.05" });
        Assert.Equal(canonical.Lower, actual.Lower); Assert.Equal(canonical.Upper, actual.Upper);
        Assert.Equal("0.1", actual.Lower); Assert.Equal("0.2", actual.Upper);
        var provenance = Assert.Single(actual.Provenance);
        Assert.Equal(unit, provenance.OriginalUnit); Assert.Equal("mg/kg", provenance.CanonicalUnit);
        Assert.Equal("0.001", provenance.ConcentrationScale); Assert.Equal("50", provenance.OriginalAbsoluteUncertainty);
        Assert.Equal("0.05", provenance.AbsoluteUncertainty);
        Assert.Equal("150", raw.NumericValue); Assert.Equal("50", selection.AbsoluteUncertainty);
    }

    [Theory]
    [InlineData("ND", "LOD", "0.01")]
    [InlineData("ND", "LOQ", "0.02")]
    [InlineData("ND", "REPORTING_THRESHOLD", "0.03")]
    [InlineData("BELOW_LIMIT", null, "0.15")]
    public void Censored_results_scale_reported_and_explicit_thresholds(string kind, string? threshold, string upper)
    {
        var f = Fixture(kind, threshold);
        var raw = f.Result with { Unit = "µg/kg", ResultKind = kind == "ND" ? "NOT_DETECTED" : kind,
            NumericValue = kind == "ND" ? null : "150", Lod = "10", Loq = "20" };
        var selection = f.Selection with { ReportingThreshold = threshold == "REPORTING_THRESHOLD" ? "30" : null };
        var actual = f.Resolve(raw, selection);
        Assert.Empty(actual.Reasons); Assert.Equal("0", actual.Lower); Assert.Equal(upper, actual.Upper);
        Assert.Equal("mg/kg", actual.Unit);
        Assert.Equal(threshold == "REPORTING_THRESHOLD" ? "30" : null, Assert.Single(actual.Provenance).OriginalReportingThreshold);
    }

    [Fact]
    public void One_profile_resolves_numeric_below_limit_and_not_detected_forms()
    {
        var f = Fixture();
        var numeric = f.Resolve(f.Result with { Uncertainty = "±0.05 as reported" }, f.Selection with { AbsoluteUncertainty = "0.05" });
        var below = f.Resolve(f.Result with { ResultKind = "BELOW_LIMIT" }, f.Selection);
        var notDetected = f.Resolve(f.Result with { ResultKind = "NOT_DETECTED", NumericValue = null }, f.Selection);
        Assert.Equal("0.1", numeric.Lower); Assert.Equal("0.2", numeric.Upper);
        Assert.Equal("0", below.Lower); Assert.Equal("0.15", below.Upper);
        Assert.Equal("0", notDetected.Lower); Assert.Equal("0.01", notDetected.Upper);
        Assert.Empty(numeric.Reasons); Assert.Empty(below.Reasons); Assert.Empty(notDetected.Reasons);
    }

    [Fact]
    public void Relative_percent_stays_dimensionless_and_raw_uncertainty_never_gets_parsed()
    {
        var f = Fixture("NUMERIC_RELATIVE_UNCERTAINTY");
        var raw = f.Result with { Unit = "ug/kg", NumericValue = "150" };
        var actual = f.Resolve(raw, f.Selection);
        Assert.Empty(actual.Reasons); Assert.Equal("0.135", actual.Lower); Assert.Equal("0.165", actual.Upper);
        Assert.Equal("10", Assert.Single(actual.Provenance).RelativeUncertaintyPercent);
        var unresolved = f.Resolve(raw with { Uncertainty = "±10%" }, f.Selection);
        Assert.Null(unresolved.Upper);
        Assert.Contains("Reported uncertainty requires explicit reviewed normalization", unresolved.Reasons);
    }

    [Theory]
    [InlineData("ppb")]
    [InlineData("mg/L")]
    [InlineData("µg / kg")]
    public void Ambiguous_or_unsupported_units_stay_unresolved(string unit)
    {
        var f = Fixture();
        var actual = f.Resolve(f.Result with { Unit = unit }, f.Selection);
        Assert.Null(actual.Upper); Assert.NotEmpty(actual.Reasons);
    }

    [Fact]
    public void Malformed_exact_fields_stay_unresolved_without_losing_raw_facts()
    {
        var f = Fixture();
        var raw = f.Result with { Unit = "µg/kg", Loq = "not an exact number" };
        var actual = f.Resolve(raw, f.Selection);
        Assert.Null(actual.Upper); Assert.NotEmpty(actual.Reasons);
        Assert.Equal("not an exact number", raw.Loq);
    }

    [Fact]
    public void Export_source_lab_ignores_group_first_without_changing_general_F12()
    {
        var f = Fixture();
        var selection = f.Selection with { AbsoluteUncertainty = "0.05" };
        var evidence = new ResidueEvidenceSnapshot([f.Source], [f.Result with { Uncertainty = "±0.05" }], [Group(f)], [selection]);
        var general = Assert.Single(ResidueEvidenceResolver.Resolve([f.Node], [f.Result.SubstanceId], evidence, f.Policy, f.At));
        Assert.Equal("0.001", general.Lower);
        Assert.Contains(general.Provenance, p => p.Kind == "SOURCE_GROUP");
        var strict = Assert.Single(ResidueEvidenceResolver.Resolve([f.Node], [f.Result.SubstanceId], evidence, f.Policy, f.At, requireSourceLab: true));
        Assert.Empty(strict.Reasons);
        Assert.Equal("0.1", strict.Lower);
        Assert.Equal("0.2", strict.Upper);
        Assert.All(strict.Provenance, p => Assert.Equal("DIRECT", p.Kind));
    }

    [Theory]
    [InlineData("NO_LAB")]
    [InlineData("NO_SELECTION")]
    [InlineData("CONFLICTING_REPORTS")]
    [InlineData("EXPIRED_SELECTION")]
    [InlineData("MISSING_SUBSTANCE")]
    [InlineData("NO_PROFILE")]
    [InlineData("WRONG_SOURCE_MAPPING")]
    public void Export_source_lab_cannot_be_replaced_by_an_approved_group(string gap)
    {
        var f = Fixture();
        var selection = f.Selection with { AbsoluteUncertainty = "0.05" };
        var confirmed = f.Result with { Uncertainty = "±0.05" };
        ResidueLabEvidence[] results = gap == "NO_LAB" ? [] : [confirmed];
        if (gap == "CONFLICTING_REPORTS") results = [confirmed, confirmed with { ResultId = Guid.NewGuid(), ReportId = Guid.NewGuid() }];
        if (gap == "MISSING_SUBSTANCE") results = [confirmed with { SubstanceId = Guid.NewGuid() }];
        if (gap == "EXPIRED_SELECTION") selection = selection with { EffectiveTo = f.At.AddSeconds(-1) };
        var sources = gap == "WRONG_SOURCE_MAPPING" ? new[] { f.Source with { CompanyId = Guid.NewGuid() } } : [f.Source];
        var evidence = new ResidueEvidenceSnapshot(sources, results, [Group(f)], gap == "NO_SELECTION" ? [] : [selection]);
        var policy = gap == "NO_PROFILE" ? new ResiduePolicySnapshot([], []) : f.Policy;
        var result = Assert.Single(ResidueEvaluationService.CalculateResults(f.Node.LotId, [f.Result.SubstanceId],
            [f.Node], evidence, policy, f.At, requireSourceLab: true));
        Assert.Equal("UNRESOLVED", result.Status);
        Assert.Null(result.Upper);
        Assert.NotEmpty(result.Reasons);
        Assert.DoesNotContain(result.Provenance, p => p.Kind == "SOURCE_GROUP");
    }

    [Fact]
    public void Every_positive_source_dependency_needs_lab_even_for_a_tiny_contribution()
    {
        var f = Fixture();
        var other = f.Node with { LotId = Guid.NewGuid() };
        var missingSource = f.Source with { SourceId = Guid.NewGuid(), LotId = other.LotId };
        var output = f.Node with { LotId = Guid.NewGuid(), Kind = "BLENDING", Stage = "BLENDED_RICE", ProductIdentity = "BLENDED_RICE",
            Parents = [new(f.Node.LotId, "999999"), new(other.LotId, "0.000001")] };
        var evidence = new ResidueEvidenceSnapshot([f.Source, missingSource], [f.Result with { Uncertainty = "±0.05" }], [Group(f)],
            [f.Selection with { AbsoluteUncertainty = "0.05" }]);
        var result = Assert.Single(ResidueEvaluationService.CalculateResults(output.LotId, [f.Result.SubstanceId],
            [f.Node, other, output], evidence, f.Policy, f.At, requireSourceLab: true));
        Assert.Equal("UNRESOLVED", result.Status);
        Assert.Contains(result.Reasons, r => r.StartsWith("SOURCE_LAB_REQUIRED:"));
        Assert.Contains(result.Dependencies, d => d.ParentLotId == other.LotId);
    }

    [Theory]
    [InlineData("NOT_DETECTED")]
    [InlineData("BELOW_LIMIT")]
    public void Reviewed_censored_lab_results_remain_eligible_for_export(string kind)
    {
        var f = Fixture();
        var raw = f.Result with { ResultKind = kind, NumericValue = kind == "NOT_DETECTED" ? null : "0.02" };
        var bound = Assert.Single(ResidueEvidenceResolver.Resolve([f.Node], [raw.SubstanceId],
            new([f.Source], [raw], [Group(f)], [f.Selection]), f.Policy, f.At, requireSourceLab: true));
        Assert.Empty(bound.Reasons);
        Assert.Equal("0", bound.Lower);
        Assert.Equal(kind == "NOT_DETECTED" ? "0.01" : "0.02", bound.Upper);
    }

    private static ResidueSourceGroupVersion Group(Case f) => new(Guid.NewGuid(), f.Node.CompanyId, "SYNTHETIC-GROUP", 1,
        f.Result.SubstanceId, [f.Source.SourceId], f.Node.Stage, f.Node.ProductIdentity, "ANALYTE", "0.001", "0.002",
        "mg/kg", "GROUP_FIRST", [], f.At.AddDays(-2), null, null, "Synthetic", "Synthetic", "Reviewer", "Test", f.At.AddDays(-2),
        DecimalPlaces: 12, SchemaVersion: 2, AsReceivedConfirmed: true, ProfileCode: "SYNTHETIC",
        ProfileBindings: [new(f.Source.SourceId, f.Policy.Rules[0].Id, f.Policy.Settings[0].Id)]);

    private sealed record Case(ResidueNode Node, ResidueSource Source, ResidueLabEvidence Result,
        ResidueReportSelection Selection, ResiduePolicySnapshot Policy, DateTimeOffset At)
    {
        public ResidueSourceBound Resolve(ResidueLabEvidence result, ResidueReportSelection selection) =>
            Assert.Single(ResidueEvidenceResolver.Resolve([Node], [Result.SubstanceId],
                new([Source], [result], [], [selection]), Policy, At));
    }

    private static Case Fixture(string kind = "NUMERIC_ABSOLUTE_UNCERTAINTY", string? threshold = null)
    {
        var at = DateTimeOffset.UtcNow; var from = at.AddDays(-2); var company = Guid.NewGuid();
        var lot = Guid.NewGuid(); var sourceId = Guid.NewGuid(); var substance = Guid.NewGuid(); var resultId = Guid.NewGuid();
        var node = new ResidueNode(lot, company, Guid.NewGuid(), "RAW_PADDY", "RAW_PADDY", at.AddDays(-1), "SOURCE", null, null, []);
        var source = new ResidueSource(sourceId, lot, company, node.At, node.Stage, node.ProductIdentity);
        var result = new ResidueLabEvidence(sourceId, lot, Guid.NewGuid(), Guid.NewGuid(), resultId, Guid.NewGuid(), substance,
            "NUMERIC", "0.15", "mg/kg", "0.01", "0.02", "EXACT_METHOD", null, at, null, null, null, null, false);
        var rule = new ResidueProfileVersion(Guid.NewGuid(), "SYNTHETIC", 1, "FLOW", 2, substance, node.CommodityId,
            "ANALYTE", "mg/kg", 12,
            [new ResidueLaboratorySettings("EXACT_METHOD",
                kind == "NUMERIC_RELATIVE_UNCERTAINTY" ? kind : "NUMERIC_ABSOLUTE_UNCERTAINTY",
                kind == "NUMERIC_RELATIVE_UNCERTAINTY" ? "10" : null, false, true, threshold ?? "LOD")],
            [], true, from, null, null, "Synthetic evidence", "Synthetic applicability", "Reviewer", "Test authority", from);
        var setting = new ResiduePolicySettingVersion(Guid.NewGuid(), company, rule.Code, 1, "SYSTEM_DEFAULT", from,
            null, null, "Synthetic adoption", "Synthetic applicability", "Reviewer", "Test authority", from);
        var selection = new ResidueReportSelection(Guid.NewGuid(), company, sourceId, substance, resultId, [],
            "ANALYTE", null, null, from, null, null, "Synthetic evidence", "Synthetic applicability",
            "Reviewer", "Test authority", from, ProfileRuleId: rule.Id, ProfileSettingId: setting.Id,
            SchemaVersion: 2, AsReceivedConfirmed: true, ProfileCode: rule.Code, LaboratoryMethod: "EXACT_METHOD");
        return new(node, source, result, selection, new([rule], [setting]), at);
    }
}
