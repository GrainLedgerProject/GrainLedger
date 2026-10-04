namespace GrainLedger.Modules.Identity;

public enum OrganizationKind { Mill, Customer }

public static class Roles
{
    public const string Administrator = nameof(Administrator);
    public const string MillManager = nameof(MillManager);
    public const string MillOperator = nameof(MillOperator);
    public const string CustomerManager = nameof(CustomerManager);
    public const string ExportStaff = nameof(ExportStaff);
    public const string PurchasingStaff = nameof(PurchasingStaff);

    public sealed record Definition(string Name, OrganizationKind OrganizationKind, string[] Permissions);

    public static readonly IReadOnlyDictionary<string, Definition> All = new[]
    {
        new Definition(Administrator, OrganizationKind.Mill, ["identity.manage", "platform.configure", "trace.read.all"]),
        new Definition(MillManager, OrganizationKind.Mill, ["platform.read", "lots.read.all", "conversions.read", "holds.process", "reconciliation.read", "history.read"]),
        new Definition(MillOperator, OrganizationKind.Mill, ["platform.read", "lots.receive", "conversions.write", "containers.operate", "history.read"]),
        new Definition(CustomerManager, OrganizationKind.Customer, ["farms.read", "lots.read.tenant", "lab.write", "blend.approve", "holds.manage", "trace.read.tenant", "history.read"]),
        new Definition(ExportStaff, OrganizationKind.Customer, ["mrl.read", "lots.read.tenant", "lab.read", "blend.propose", "containers.write", "trace.read.tenant", "history.read"]),
        new Definition(PurchasingStaff, OrganizationKind.Customer, ["farms.write", "purchases.write", "history.read"])
    }.ToDictionary(x => x.Name, StringComparer.Ordinal);
}

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public OrganizationKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<User> Users { get; set; } = [];
}

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Provider { get; set; }
    public required string ProviderSubject { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Role { get; set; }
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<LoginSession> Sessions { get; set; } = [];
}

public sealed class LoginSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

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
