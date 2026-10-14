namespace GrainLedger.Modules.Capture;

public sealed class Procurement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public Guid FarmerId { get; set; }
    public Guid CollectionSiteId { get; set; }
    public Guid ReceivedBy { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string Status { get; set; }
    public Guid? EventId { get; set; }
}

public sealed class ProcurementItem
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProcurementId { get; set; }
    public int LineNo { get; set; }
    public Guid PlotSeasonId { get; set; }
    public decimal NetMassKg { get; set; }
    public decimal MeasuredValue { get; set; }
    public required string MeasuredUnit { get; set; }
    public decimal MoisturePercent { get; set; }
}

public sealed class SourceLot
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LotId { get; set; }
    public Guid ProcurementItemId { get; set; }
}
