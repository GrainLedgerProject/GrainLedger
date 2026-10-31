using System.Globalization;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Ledger;

namespace GrainLedger.Modules.Exports;

// These are explicit, versioned inputs. None has a runtime default: an absent
// approved rule or observation cannot turn an F14 assessment into PASS.
internal sealed record BlendYieldRule(Guid Id, int Version, decimal OutputFraction,
    string EvidenceReference);
internal sealed record BlendAssessmentRule(Guid Id, int Version, Guid MarketId,
    Guid CommodityId, IReadOnlyList<Guid> RequiredSubstances, decimal MrlMarginFraction,
    string ResidueBasis, string EvidenceReference);
internal sealed record BlendQualityRule(Guid Id, int Version, Guid AttributeId,
    string Unit, string Calculation, string EvidenceReference);
internal sealed record BlendQualityObservation(Guid LotId, Guid AttributeId,
    decimal Value, string Unit, Guid EvidenceId, int EvidenceVersion);
internal sealed record BlendAssessmentFinding(string Code, string Status, string? Actual,
    string? Limit, string? Unit, string? Reference);
internal sealed record BlendAssessmentResult(string Status, decimal? PredictedOutputKg,
    IReadOnlyList<BlendAssessmentFinding> Findings);

internal static class BlendAssessment
{
    internal static BlendAssessmentResult Evaluate(string mode, decimal targetKg,
        IReadOnlyList<CandidateGroupItem> allocations, BlendYieldRule? yield,
        BlendAssessmentRule? policy, ApplicableMrlResult mrl,
        IReadOnlyList<ResidueResult>? residue,
        IReadOnlyList<SpecificationLimit> specification,
        IReadOnlyList<BlendQualityRule> qualityRules,
        IReadOnlyList<BlendQualityObservation> qualityObservations)
    {
        var findings = new List<BlendAssessmentFinding>();
        void Add(string code, string status, string? actual = null, string? limit = null,
            string? unit = null, string? reference = null) =>
            findings.Add(new(code, status, actual, limit, unit, reference));

        decimal? output = null;
        if (allocations.Count == 0 || allocations.Any(x => x.QuantityKg <= 0) ||
            allocations.Select(x => (x.LotId, x.LocationId)).Distinct().Count() != allocations.Count)
            Add("ALLOCATION_INVALID", "FAIL");
        else if (mode == "DIRECT_STOCK")
            output = allocations.Sum(x => x.QuantityKg);
        else if (mode != "BLEND_INPUT_STOCK")
            Add("MODE_INVALID", "FAIL");
        else if (yield is null || yield.Id == Guid.Empty || yield.Version <= 0 ||
            string.IsNullOrWhiteSpace(yield.EvidenceReference))
            Add("OUTPUT_YIELD_POLICY_MISSING", "INCOMPLETE");
        else if (yield.OutputFraction <= 0 || yield.OutputFraction > 1)
            Add("OUTPUT_YIELD_POLICY_INVALID", "FAIL", reference: yield.Id.ToString());
        else
        {
            var predicted = allocations.Sum(x => x.QuantityKg) * yield.OutputFraction;
            if (decimal.Round(predicted, 6) != predicted)
                Add("OUTPUT_MASS_PRECISION_UNRESOLVED", "INCOMPLETE", reference: yield.Id.ToString());
            else output = predicted;
        }
        if (output is { } kg)
            Add("OUTPUT_MASS", kg >= targetKg ? "PASS" : "FAIL", Text(kg),
                Text(targetKg), "kg", yield?.Id.ToString());

        if (policy is null || policy.Id == Guid.Empty || policy.Version <= 0 ||
            string.IsNullOrWhiteSpace(policy.EvidenceReference) ||
            policy.RequiredSubstances.Count == 0)
            Add("ASSESSMENT_POLICY_MISSING", "INCOMPLETE");
        else if (policy.MrlMarginFraction < 0 || policy.MrlMarginFraction >= 1 ||
            policy.RequiredSubstances.Contains(Guid.Empty) ||
            policy.RequiredSubstances.Distinct().Count() != policy.RequiredSubstances.Count)
            Add("ASSESSMENT_POLICY_INVALID", "FAIL", reference: policy.Id.ToString());
        else if (mrl.Status != "FOUND" || mrl.MarketId != policy.MarketId ||
            mrl.CommodityId != policy.CommodityId)
            Add("APPLICABLE_MRL_MISSING", "INCOMPLETE", reference: policy.Id.ToString());
        else
        {
            var limits = mrl.Limits.ToLookup(x => x.SubstanceId);
            var bounds = (residue ?? []).ToLookup(x => x.SubstanceId);
            foreach (var substance in policy.RequiredSubstances.Order())
            {
                var legalMatches = limits[substance].ToArray();
                var boundMatches = bounds[substance].ToArray();
                var legal = legalMatches.Length == 1 ? legalMatches[0] : null;
                var bound = boundMatches.Length == 1 ? boundMatches[0] : null;
                if (legal is null || bound is null || bound.Status != "COMPLETE" ||
                    !decimal.TryParse(bound.Upper, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var upper) ||
                    bound.Unit != legal.Unit || bound.Basis != policy.ResidueBasis)
                {
                    Add("RESIDUE_COVERAGE_INCOMPLETE", "INCOMPLETE", reference: substance.ToString());
                    continue;
                }
                var threshold = legal.LimitValue * (1 - policy.MrlMarginFraction);
                Add("RESIDUE_LIMIT", upper <= threshold ? "PASS" : "FAIL",
                    Text(upper), Text(threshold), legal.Unit, substance.ToString());
            }
        }

        foreach (var limit in specification.OrderBy(x => x.AttributeId))
        {
            var matchingRules = qualityRules.Where(x => x.AttributeId == limit.AttributeId).ToArray();
            var rule = matchingRules.Length == 1 ? matchingRules[0] : null;
            if (mode == "BLEND_INPUT_STOCK" && (rule is null || rule.Id == Guid.Empty ||
                rule.Version <= 0 || string.IsNullOrWhiteSpace(rule.EvidenceReference) ||
                rule.Unit != limit.Unit || rule.Calculation != "MASS_WEIGHTED_MEAN"))
            {
                Add("QUALITY_RULE_MISSING", "INCOMPLETE", reference: limit.AttributeId.ToString());
                continue;
            }
            var values = new List<(decimal Value, decimal Kg)>();
            var missing = false;
            foreach (var allocation in allocations)
            {
                var matchingObservations = qualityObservations.Where(x =>
                    x.LotId == allocation.LotId && x.AttributeId == limit.AttributeId &&
                    x.Unit == limit.Unit && x.EvidenceId != Guid.Empty && x.EvidenceVersion > 0).ToArray();
                if (matchingObservations.Length != 1) { missing = true; break; }
                var observation = matchingObservations[0];
                values.Add((observation.Value, allocation.QuantityKg));
            }
            if (missing || allocations.Count == 0)
            {
                Add("QUALITY_EVIDENCE_MISSING", "INCOMPLETE", reference: limit.AttributeId.ToString());
                continue;
            }
            var lower = mode == "DIRECT_STOCK" ? values.Min(x => x.Value) :
                values.Sum(x => x.Value * x.Kg) / values.Sum(x => x.Kg);
            var upper = mode == "DIRECT_STOCK" ? values.Max(x => x.Value) : lower;
            var pass = (limit.MinValue is null || lower >= limit.MinValue) &&
                (limit.MaxValue is null || upper <= limit.MaxValue);
            Add("QUALITY_LIMIT", pass ? "PASS" : "FAIL",
                lower == upper ? Text(lower) : Text(lower) + ".." + Text(upper),
                $"{limit.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "-"}.." +
                (limit.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "-"),
                limit.Unit, limit.AttributeId.ToString());
        }
        if (specification.Count == 0)
            Add("QUALITY_SPECIFICATION_MISSING", "INCOMPLETE");

        var status = findings.Any(x => x.Status == "FAIL") ? "FAIL" :
            findings.Any(x => x.Status == "INCOMPLETE") ? "INCOMPLETE" : "PASS";
        return new(status, output, findings);
    }

    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
