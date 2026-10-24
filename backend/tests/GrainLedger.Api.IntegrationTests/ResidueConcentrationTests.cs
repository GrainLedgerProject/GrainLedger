using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Ledger;
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
            "NUMERIC", "0.15", "mg/kg", "0.01", "0.02", "EXACT_METHOD", null, at, null, null, null, null, null, false);
        var selection = new ResidueReportSelection(Guid.NewGuid(), company, sourceId, substance, resultId, [],
            "AS_RECEIVED", "ANALYTE", null, null, from, null, null, "Synthetic evidence", "Synthetic applicability",
            "Reviewer", "Test authority", from);
        var rule = new ResidueRuleVersion(Guid.NewGuid(), "SYNTHETIC", 1, "INTERPRETATION", substance, "ANALYTE",
            node.Stage, node.Stage, "AS_RECEIVED", "AS_RECEIVED", "", "", "", "mg/kg", 12, kind, threshold,
            null, null, null, false, from, null, null, "Synthetic evidence", "Synthetic applicability", "Reviewer", "Test authority", from,
            CommodityId: node.CommodityId, AnalyticalMethod: "EXACT_METHOD", RelativeUncertaintyPercent: kind == "NUMERIC_RELATIVE_UNCERTAINTY" ? "10" : null);
        var setting = new ResiduePolicySettingVersion(Guid.NewGuid(), company, rule.Code, 1, "SYSTEM_DEFAULT", from,
            null, null, "Synthetic adoption", "Synthetic applicability", "Reviewer", "Test authority", from);
        return new(node, source, result, selection, new([rule], [setting]), at);
    }
}
