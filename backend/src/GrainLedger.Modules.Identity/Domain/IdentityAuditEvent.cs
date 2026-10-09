namespace GrainLedger.Modules.Identity;

public sealed class IdentityAuditEvent
{
    public long Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? SubjectUserId { get; set; }
    public required string Action { get; set; }
    public required string DetailsJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IpAddress { get; set; }
}
