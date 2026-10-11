namespace GrainLedger.Modules.Capture;

public sealed class Plot
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FarmerId { get; set; }
    public required string Code { get; set; }
    public required string BoundaryGeojson { get; set; }
    public decimal AreaHa { get; set; }
}
