namespace GrainLedger.Modules.Exports;

public static class ExportTargetProducts
{
    private static readonly string[] Rice = ["HEAD_RICE", "BROKEN_RICE", "BLENDED_RICE"];
    private static readonly string[] Paddy =
        ["RAW_PADDY", "DRIED_PADDY", "HEAD_RICE", "BROKEN_RICE", "BRAN", "HUSK", "BLENDED_RICE"];
    private static readonly string[] Bran = ["BRAN"];
    private static readonly string[] Husk = ["HUSK"];

    // Ledger currently retains the source Catalog commodity through processing.
    // PADDY therefore covers its derived fractions; the product identity still
    // distinguishes the actual output. Unmapped Catalog codes fail closed.
    public static IReadOnlyList<string> ForCommodity(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "RICE" or "RICE_POLISHED" => Rice,
        "PADDY" => Paddy,
        "RICE_BRAN" => Bran,
        "HUSK" or "RICE_HUSK" => Husk,
        _ => []
    };
}
