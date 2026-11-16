using System.Globalization;
using GrainLedger.Api.Features.Ledger;

namespace GrainLedger.Api.Features.Exports;

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
            var resolved = rows.OfType<ResidueResult>().ToArray();
            if (resolved.Length == 0) continue;
            var complete = rows.Length == inputs.Count && inputs.Count > 0 &&
                rows.All(x => x is { Status: "COMPLETE", Upper: not null, Unit: not null }) &&
                rows.Select(x => x!.Unit).Distinct().Count() == 1 &&
                rows.Select(x => x!.CommodityId).Distinct().Count() == 1 &&
                rows.Select(x => x!.ProductStage).Distinct().Count() == 1 &&
                rows.Select(x => x!.ProductIdentity).Distinct().Count() == 1 &&
                rows.All(x => decimal.TryParse(x!.Upper, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out _));
            output.Add(resolved[0] with
            {
                Status = complete ? "COMPLETE" : "INCOMPLETE",
                Lower = null,
                Upper = complete ? resolved.Max(x => decimal.Parse(x.Upper!,
                    CultureInfo.InvariantCulture)).ToString("0.############################",
                    CultureInfo.InvariantCulture) : null,
                Unit = complete ? resolved[0].Unit : null,
                ProfileCode = resolved.Select(x => x.ProfileCode).Distinct().Count() == 1
                    ? resolved[0].ProfileCode : null,
                Reasons = complete ? [] : ["DIRECT_LOT_RESIDUE_INCOMPLETE"],
                Provenance = resolved.SelectMany(x => x.Provenance).ToArray(),
                Rules = resolved.SelectMany(x => x.Rules).ToArray(),
                Dependencies = resolved.SelectMany(x => x.Dependencies).ToArray()
            });
        }
        return output;
    }
}
