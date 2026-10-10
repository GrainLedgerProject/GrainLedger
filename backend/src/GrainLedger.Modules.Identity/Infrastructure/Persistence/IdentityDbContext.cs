using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Identity;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<IdentityAuditEvent> AuditEvents => Set<IdentityAuditEvent>();
    public DbSet<RegistrationAttempt> RegistrationAttempts => Set<RegistrationAttempt>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.Entity<Organization>(b =>
        {
            b.ToTable("organizations"); b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => x.Name).IsUnique();
            b.HasIndex(x => x.Kind).IsUnique().HasFilter("\"Kind\" = 'Mill'");
        });
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users"); b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(30); b.Property(x => x.ProviderSubject).HasMaxLength(255);
            b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.DisplayName).HasMaxLength(120);
            b.Property(x => x.PhoneNumber).HasMaxLength(32); b.Property(x => x.Role).HasMaxLength(40);
            b.Property(x => x.NormalizedEmail).HasMaxLength(320); b.Property(x => x.PasswordHash).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.Provider, x.ProviderSubject }).IsUnique();
            b.HasIndex(x => x.Email);
            b.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("\"Provider\" = 'local'");
            b.HasOne(x => x.Organization).WithMany(x => x.Users).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<LoginSession>(b =>
        {
            b.ToTable("sessions"); b.HasKey(x => x.Id); b.Property(x => x.IpAddress).HasMaxLength(64); b.Property(x => x.UserAgent).HasMaxLength(512);
            b.Property(x => x.RefreshTokenHash).HasMaxLength(64);
            b.HasIndex(x => new { x.UserId, x.RevokedAt });
            b.HasIndex(x => x.RefreshTokenHash).IsUnique().HasFilter("\"RefreshTokenHash\" IS NOT NULL");
            b.HasIndex(x => x.FamilyId);
            b.HasOne(x => x.User).WithMany(x => x.Sessions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<RegistrationAttempt>(b =>
        {
            b.ToTable("registration_attempts"); b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(320); b.Property(x => x.NormalizedEmail).HasMaxLength(320);
            b.Property(x => x.DisplayName).HasMaxLength(120); b.Property(x => x.PasswordHash).HasMaxLength(1000);
            b.Property(x => x.CodeHash).HasMaxLength(64); b.HasIndex(x => new { x.NormalizedEmail, x.InvalidatedAt });
        });
        modelBuilder.Entity<PasswordResetCode>(b =>
        {
            b.ToTable("password_reset_codes"); b.HasKey(x => x.Id); b.Property(x => x.CodeHash).HasMaxLength(64);
            b.HasIndex(x => new { x.UserId, x.ConsumedAt });
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IdentityAuditEvent>(b =>
        {
            b.ToTable("audit_events"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(100); b.Property(x => x.DetailsJson).HasColumnType("jsonb");
            b.HasIndex(x => x.OccurredAt);
        });
    }
}
