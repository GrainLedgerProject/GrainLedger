namespace GrainLedger.Modules.Capture;

public sealed class Sample
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public Guid SourceLotId { get; set; }
    public required string SealCode { get; set; }
    public Guid CollectedBy { get; set; }
    public DateTimeOffset CollectedAt { get; set; }
    public string? MaterialDescription { get; set; }
}

public sealed class SampleHandover
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SampleId { get; set; }
    public Guid RecordedBy { get; set; }
    public required string FromParty { get; set; }
    public required string ToParty { get; set; }
    public DateTimeOffset HandedOverAt { get; set; }
    public required string SealCondition { get; set; }
    public string? EvidenceRef { get; set; }
    public string HandoverKind { get; set; } = "HANDOVER";
    public string? LaboratoryName { get; set; }
    public string? LaboratoryReference { get; set; }
    public string? LaboratorySampleReference { get; set; }
}
