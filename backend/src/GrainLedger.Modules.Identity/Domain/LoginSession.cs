namespace GrainLedger.Modules.Identity;

public sealed class LoginSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid FamilyId { get; set; } = Guid.NewGuid();
    public string? RefreshTokenHash { get; set; }
    public DateTimeOffset? RefreshTokenUsedAt { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
