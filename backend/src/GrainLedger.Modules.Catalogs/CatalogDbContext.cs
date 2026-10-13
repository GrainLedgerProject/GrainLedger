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

public sealed class MrlVersion
{
    public Guid Id { get; set; }
    public Guid MarketId { get; set; }
    public Guid CommodityId { get; set; }
    public int VersionNo { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public Guid? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public required string SourceRef { get; set; }
    public uint Version { get; set; }
}

public sealed class MrlLimit
{
    public Guid Id { get; set; }
    public Guid MrlVersionId { get; set; }
    public Guid SubstanceId { get; set; }
    public decimal LimitValue { get; set; }
    public required string Unit { get; set; }
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
    public DbSet<MrlVersion> MrlVersions => Set<MrlVersion>();
    public DbSet<MrlLimit> MrlLimits => Set<MrlLimit>();
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
        modelBuilder.Entity<MrlVersion>(b =>
        {
            b.ToTable("mrl_versions", table =>
            {
                table.HasCheckConstraint("ck_mrl_versions_effective_interval",
                    "effective_to IS NULL OR effective_to > effective_from");
                table.HasCheckConstraint("ck_mrl_versions_publication",
                    "(status = 'DRAFT' AND published_by IS NULL AND published_at IS NULL) OR " +
                    "(status = 'PUBLISHED' AND published_by IS NOT NULL AND published_at IS NOT NULL)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.MarketId).HasColumnName("market_id");
            b.Property(x => x.CommodityId).HasColumnName("commodity_id");
            b.Property(x => x.VersionNo).HasColumnName("version_no");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            b.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
            b.Property(x => x.EffectiveTo).HasColumnName("effective_to");
            b.Property(x => x.PublishedBy).HasColumnName("published_by");
            b.Property(x => x.PublishedAt).HasColumnName("published_at");
            b.Property(x => x.SourceRef).HasColumnName("source_ref").IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Commodity>().WithMany().HasForeignKey(x => x.CommodityId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.MarketId, x.CommodityId, x.VersionNo }).IsUnique();
            b.HasIndex(x => x.CommodityId);
            b.HasIndex(x => x.PublishedBy);
        });
        modelBuilder.Entity<MrlLimit>(b =>
        {
            b.ToTable("mrl_limits", table =>
                table.HasCheckConstraint("ck_mrl_limits_nonnegative", "limit_value >= 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.MrlVersionId).HasColumnName("mrl_version_id");
            b.Property(x => x.SubstanceId).HasColumnName("substance_id");
            b.Property(x => x.LimitValue).HasColumnName("limit_value").HasPrecision(24, 12);
            b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(20).IsRequired();
            b.HasOne<MrlVersion>().WithMany().HasForeignKey(x => x.MrlVersionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ActiveSubstance>().WithMany().HasForeignKey(x => x.SubstanceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.MrlVersionId, x.SubstanceId }).IsUnique();
            b.HasIndex(x => x.SubstanceId);
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
