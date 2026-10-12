using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Catalogs;

public sealed class ActiveSubstance
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public uint Version { get; set; }
}

public sealed class Market
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public uint Version { get; set; }
}

public sealed class Commodity
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public uint Version { get; set; }
}

public sealed class QualityAttribute
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Unit { get; set; }
    public uint Version { get; set; }
}

public sealed class RiceVariety
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public uint Version { get; set; }
}

public sealed class MeasurementUnit
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string QuantityKind { get; set; }
    public uint Version { get; set; }
}

public sealed class CatalogEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorId { get; set; }
    public required string EventType { get; set; }
    public required string ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid CorrelationId { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
}

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<ActiveSubstance> ActiveSubstances => Set<ActiveSubstance>();
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<Commodity> Commodities => Set<Commodity>();
    public DbSet<QualityAttribute> QualityAttributes => Set<QualityAttribute>();
    public DbSet<RiceVariety> RiceVarieties => Set<RiceVariety>();
    public DbSet<MeasurementUnit> MeasurementUnits => Set<MeasurementUnit>();
    public DbSet<CatalogEvent> CatalogEvents => Set<CatalogEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");
        modelBuilder.Entity<ActiveSubstance>(b =>
        {
            b.ToTable("active_substances");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<Market>(b =>
        {
            b.ToTable("markets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<Commodity>(b =>
        {
            b.ToTable("commodities");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<QualityAttribute>(b =>
        {
            b.ToTable("quality_attributes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<RiceVariety>(b =>
        {
            b.ToTable("rice_varieties");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<MeasurementUnit>(b =>
        {
            b.ToTable("measurement_units", table => table.HasCheckConstraint("ck_measurement_units_quantity_kind",
                "quantity_kind IN ('MASS', 'AREA', 'RESIDUE_CONCENTRATION', 'APPLICATION_RATE', 'FRACTION')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.QuantityKind).HasColumnName("quantity_kind").HasMaxLength(32).IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<CatalogEvent>(b =>
        {
            b.ToTable("catalog_events", table => table.HasCheckConstraint("ck_catalog_events_has_state",
                "before_state IS NOT NULL OR after_state IS NOT NULL"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(60).IsRequired();
            b.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(60).IsRequired();
            b.Property(x => x.ResourceId).HasColumnName("resource_id");
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            b.Property(x => x.BeforeState).HasColumnName("before_state").HasColumnType("jsonb");
            b.Property(x => x.AfterState).HasColumnName("after_state").HasColumnType("jsonb");
            b.HasIndex(x => new { x.ResourceType, x.ResourceId, x.OccurredAt });
            b.HasIndex(x => x.CorrelationId);
            b.HasIndex(x => x.ActorId);
        });
    }
}
