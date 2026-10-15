namespace GrainLedger.Modules.Capture;

public sealed class PlotSeason
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PlotId { get; set; }
    public Guid SeasonId { get; set; }
    public required string RiceVariety { get; set; }
    public Guid? CreatedBy { get; set; }
    public int Version { get; set; } = 1;
    public bool IsDeleted { get; set; }
}
