namespace GrainLedger.Modules.Capture;

public sealed class CropSeason
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Name { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public Guid? CreatedBy { get; set; }
    public int Version { get; set; } = 1;
    public bool IsDeleted { get; set; }
}
