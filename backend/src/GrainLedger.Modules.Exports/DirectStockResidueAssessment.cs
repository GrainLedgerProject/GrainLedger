using System.Globalization;
using GrainLedger.Modules.Ledger;

namespace GrainLedger.Modules.Exports;

// Directly packed lots remain physically distinct. Every lot must satisfy the
// MRL; averaging their concentrations would hide a violating lot.
internal static class DirectStockResidueAssessment
{
    internal static IReadOnlyList<ResidueResult> ConservativeBounds(
        IReadOnlyList<ResidueEvaluationPreview> inputs,
        IReadOnlyList<Guid> requiredSubstances)
    {
        var output = new List<ResidueResult>();
        foreach (var substance in requiredSubstances)
        {
            var rows = inputs.Select(x => x.Results.SingleOrDefault(r =>
                r.SubstanceId == substance)).ToArray();
            var complete = rows.Length == inputs.Count && inputs.Count > 0 &&
                rows.All(x => x is { Status: "COMPLETE", Upper: not null,
                    Unit: not null, Basis: not null }) &&
                rows.Select(x => x!.Unit).Distinct().Count() == 1 &&
                rows.Select(x => x!.Basis).Distinct().Count() == 1 &&
                rows.All(x => decimal.TryParse(x!.Upper, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out _));
            var resolved = rows.OfType<ResidueResult>().ToArray();
            output.Add(new ResidueResult(substance, complete ? "COMPLETE" : "INCOMPLETE",
                null, complete ? resolved.Max(x => decimal.Parse(x.Upper!,
                    CultureInfo.InvariantCulture)).ToString("0.############################",
                    CultureInfo.InvariantCulture) : null,
                complete ? resolved[0].Unit : null, complete ? resolved[0].Basis : null,
                complete ? [] : ["DIRECT_LOT_RESIDUE_INCOMPLETE"],
                resolved.SelectMany(x => x.Provenance).ToArray(),
                resolved.SelectMany(x => x.Rules).ToArray(),
                resolved.SelectMany(x => x.Dependencies).ToArray()));
        }
        return output;
    }
}
