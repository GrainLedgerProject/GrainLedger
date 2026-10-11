using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Capture;

public sealed class Farmer
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Contact { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CaptureOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid SubmittedBy { get; set; }
    public required string DeviceId { get; set; }
    public Guid ClientOperationId { get; set; }
    public required string PayloadHash { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public string? CaptureGeojson { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public required string Status { get; set; }
    public Guid? AppliedEventId { get; set; }
    public string? ResultPayload { get; set; }
    public string? ConflictReason { get; set; }
}

public sealed class CaptureBusinessEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CompanyId { get; set; }
    public Guid? ActorId { get; set; }
    public required string EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid CorrelationId { get; set; }
    public required string Payload { get; set; }
}

public sealed class CaptureDbContext(DbContextOptions<CaptureDbContext> options) : DbContext(options)
{
    public DbSet<Farmer> Farmers => Set<Farmer>();
    public DbSet<Plot> Plots => Set<Plot>();
    public DbSet<CaptureOperation> OfflineOperations => Set<CaptureOperation>();
    public DbSet<CaptureBusinessEvent> BusinessEvents => Set<CaptureBusinessEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // InitialCapture adds foreign keys to identity tables in its migration.
        // Those tables belong to the Identity DbContext and are not remapped here.
        modelBuilder.HasDefaultSchema("capture");
        modelBuilder.Entity<Farmer>(b =>
        {
            b.ToTable("farmers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Contact).HasColumnName("contact");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ux_farmers_company_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_farmers_company_id");
            b.HasIndex(x => new { x.CompanyId, x.CreatedAt, x.Id });
        });
        modelBuilder.Entity<Plot>(b =>
        {
            b.ToTable("plots", table => table.HasCheckConstraint("ck_plots_area_ha_positive", "area_ha > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.FarmerId).HasColumnName("farmer_id");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.BoundaryGeojson).HasColumnName("boundary_geojson").HasColumnType("jsonb").IsRequired();
            b.Property(x => x.AreaHa).HasColumnName("area_ha").HasPrecision(24, 12);
            b.HasIndex(x => new { x.FarmerId, x.Code }).IsUnique().HasDatabaseName("ux_plots_farmer_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_plots_company_id");
        });
        modelBuilder.Entity<CaptureOperation>(b =>
        {
            b.ToTable("offline_operations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.SubmittedBy).HasColumnName("submitted_by");
            b.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(100);
            b.Property(x => x.ClientOperationId).HasColumnName("client_operation_id");
            b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
            b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            b.Property(x => x.CapturedAt).HasColumnName("captured_at");
            b.Property(x => x.CaptureGeojson).HasColumnName("capture_geojson").HasColumnType("jsonb");
            b.Property(x => x.ReceivedAt).HasColumnName("received_at");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            b.Property(x => x.AppliedEventId).HasColumnName("applied_event_id");
            b.Property(x => x.ResultPayload).HasColumnName("result_payload").HasColumnType("jsonb");
            b.Property(x => x.ConflictReason).HasColumnName("conflict_reason");
            b.HasIndex(x => new { x.CompanyId, x.DeviceId, x.ClientOperationId }).IsUnique()
                .HasDatabaseName("ux_offline_operations_company_device_operation");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique();
            b.HasIndex(x => x.SubmittedBy);
            b.HasIndex(x => x.AppliedEventId).IsUnique();
        });
        modelBuilder.Entity<CaptureBusinessEvent>(b =>
        {
            b.ToTable("business_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(60);
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            b.HasIndex(x => x.CompanyId);
            b.HasIndex(x => x.ActorId);
        });
    }
}
