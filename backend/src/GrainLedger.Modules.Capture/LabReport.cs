namespace GrainLedger.Modules.Capture;

public sealed class LabReport
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string LaboratoryName { get; set; }
    public required string ReportNumber { get; set; }
    public DateOnly IssuedOn { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string ReportKind { get; set; }
    public Guid? RelatedReportId { get; set; }
    public string? RelationshipReason { get; set; }
    public string? LaboratoryConclusion { get; set; }
    public string? StatedStandard { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public int FileSize { get; set; }
    public required string Sha256 { get; set; }
    public required byte[] Content { get; set; }
    public bool CompanyOnlyConfirmed { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class LabReportRetirement
{
    public Guid ReportId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ReplacementReportId { get; set; }
    public required string Status { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class LabResultSet
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ReportId { get; set; }
    public int Position { get; set; }
    public Guid? SampleId { get; set; }
    public string? LaboratorySampleReference { get; set; }
    public string? StatedSampleCode { get; set; }
    public string? StatedSealCode { get; set; }
    public string? SectionReference { get; set; }
}

public sealed class LabResult
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ResultSetId { get; set; }
    public int Position { get; set; }
    public required string TestLabel { get; set; }
    public Guid? SubstanceId { get; set; }
    public required string OriginalResultText { get; set; }
    public required string ResultKind { get; set; }
    // Exact reported decimal text, constrained in PostgreSQL; interpretation is deliberately gated.
    public string? NumericValue { get; set; }
    public string? Unit { get; set; }
    public string? Lod { get; set; }
    public string? Loq { get; set; }
    public string? Method { get; set; }
    public string? Uncertainty { get; set; }
    public string? Reference { get; set; }
}

public sealed class LabReportConfirmation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ReportId { get; set; }
    public Guid ConfirmedBy { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
    public bool IdentityChecked { get; set; }
    public bool TranscriptionChecked { get; set; }
}

public sealed class LabConfirmedAssociation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConfirmationId { get; set; }
    public Guid ResultSetId { get; set; }
    public Guid SampleId { get; set; }
    public required string IdentityBasis { get; set; }
    public string? EvidenceRef { get; set; }
}
