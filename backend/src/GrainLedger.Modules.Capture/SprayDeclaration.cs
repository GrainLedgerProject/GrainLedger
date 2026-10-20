namespace GrainLedger.Modules.Capture;

public sealed class SprayDeclaration
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PlotSeasonId { get; set; }
    public DateOnly AppliedOn { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string? EvidenceRef { get; set; }
}

public sealed class SprayItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DeclarationId { get; set; }
    public string? ProductName { get; set; }
    public Guid? SubstanceId { get; set; }
    // Legacy values remain readable. New declarations do not collect application rate.
    public decimal? Rate { get; set; }
    public string? RateUnit { get; set; }
}
