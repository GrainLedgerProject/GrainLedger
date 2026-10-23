using GrainLedger.Modules.Capture;

namespace GrainLedger.Modules.Ledger;

public static class ResidueConcentration
{
    // ppb lacks an explicit mass-fraction marker in F11. Never infer its meaning from the label.
    public static string? CanonicalUnit(string? unit) => unit switch
    {
        "mg/kg" or "µg/kg" or "μg/kg" or "ug/kg" => "mg/kg",
        _ => null
    };

    public static (ResidueLabEvidence Evidence, ResidueReportSelection Selection, string? Reason) Normalize(
        ResidueLabEvidence raw, ResidueReportSelection selection)
    {
        if (CanonicalUnit(raw.Unit) is null) return (raw, selection, "Unsupported mass-concentration unit; explicit mg/kg or µg/kg required");
        try
        {
            var scale = ResidueRational.Parse(raw.Unit == "mg/kg" ? "1" : "0.001");
            string? Convert(string? value)
            {
                if (value is null) return null;
                var exact = ResidueRational.Parse(value);
                if (exact.CompareTo(ResidueRational.Zero) < 0) throw new FormatException();
                return (exact * scale).ExactDecimal();
            }
            return (raw with { Unit = "mg/kg", NumericValue = Convert(raw.NumericValue), Lod = Convert(raw.Lod),
                    Loq = Convert(raw.Loq), AbsoluteUncertainty = Convert(raw.AbsoluteUncertainty), ReportingThreshold = Convert(raw.ReportingThreshold) },
                selection with { AbsoluteUncertainty = Convert(selection.AbsoluteUncertainty), ReportingThreshold = Convert(selection.ReportingThreshold) }, null);
        }
        catch (FormatException)
        {
            return (raw, selection, "Unsupported exact concentration value, threshold or absolute uncertainty");
        }
    }
}
