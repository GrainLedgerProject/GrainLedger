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
    public Guid? CreatedBy { get; set; }
    public int Version { get; set; } = 1;
    public bool IsDeleted { get; set; }
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
    public DbSet<CropSeason> CropSeasons => Set<CropSeason>();
    public DbSet<PlotSeason> PlotSeasons => Set<PlotSeason>();
    public DbSet<SprayDeclaration> SprayDeclarations => Set<SprayDeclaration>();
    public DbSet<SprayItem> SprayItems => Set<SprayItem>();
    public DbSet<Procurement> Procurements => Set<Procurement>();
    public DbSet<ProcurementItem> ProcurementItems => Set<ProcurementItem>();
    public DbSet<SourceLot> SourceLots => Set<SourceLot>();
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
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
            b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ux_farmers_company_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_farmers_company_id");
            b.HasIndex(x => new { x.CompanyId, x.CreatedAt, x.Id });
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.HasQueryFilter(x => !x.IsDeleted);
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
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.HasQueryFilter(x => !x.IsDeleted);
            b.HasIndex(x => new { x.FarmerId, x.Code }).IsUnique().HasDatabaseName("ux_plots_farmer_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_plots_company_id");
        });
        modelBuilder.Entity<CropSeason>(b =>
        {
            b.ToTable("crop_seasons", table => table.HasCheckConstraint("ck_crop_seasons_dates", "ends_on >= starts_on"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.StartsOn).HasColumnName("starts_on").HasColumnType("date");
            b.Property(x => x.EndsOn).HasColumnName("ends_on").HasColumnType("date");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.HasQueryFilter(x => !x.IsDeleted);
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_crop_seasons_company_id");
        });
        modelBuilder.Entity<PlotSeason>(b =>
        {
            b.ToTable("plot_seasons");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.PlotId).HasColumnName("plot_id");
            b.Property(x => x.SeasonId).HasColumnName("season_id");
            b.Property(x => x.RiceVariety).HasColumnName("rice_variety").IsRequired();
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.HasQueryFilter(x => !x.IsDeleted);
            b.HasIndex(x => new { x.PlotId, x.SeasonId }).IsUnique().HasDatabaseName("ux_plot_seasons_plot_season");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_plot_seasons_company_id");
            b.HasIndex(x => x.SeasonId);
        });
        modelBuilder.Entity<SprayDeclaration>(b =>
        {
            b.ToTable("spray_declarations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.PlotSeasonId).HasColumnName("plot_season_id");
            b.Property(x => x.AppliedOn).HasColumnName("applied_on");
            b.Property(x => x.RecordedBy).HasColumnName("recorded_by");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.Property(x => x.EvidenceRef).HasColumnName("evidence_ref");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_spray_declarations_company_id");
            b.HasIndex(x => x.PlotSeasonId);
            b.HasIndex(x => x.RecordedBy);
        });
        modelBuilder.Entity<SprayItem>(b =>
        {
            b.ToTable("spray_items", table =>
            {
                table.HasCheckConstraint("ck_spray_items_rate_positive", "rate > 0");
                table.HasCheckConstraint("ck_spray_items_product_or_substance", "product_name IS NOT NULL OR substance_id IS NOT NULL");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.DeclarationId).HasColumnName("declaration_id");
            b.Property(x => x.ProductName).HasColumnName("product_name");
            b.Property(x => x.SubstanceId).HasColumnName("substance_id");
            b.Property(x => x.Rate).HasColumnName("rate").HasPrecision(24, 12);
            b.Property(x => x.RateUnit).HasColumnName("rate_unit").HasMaxLength(40).IsRequired();
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_spray_items_company_id");
            b.HasIndex(x => x.DeclarationId);
            b.HasIndex(x => x.SubstanceId);
        });
        modelBuilder.Entity<Procurement>(b =>
        {
            b.ToTable("procurements", table => table.HasCheckConstraint("ck_procurements_status", "status IN ('DRAFT', 'COMMITTED')"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.FarmerId).HasColumnName("farmer_id");
            b.Property(x => x.CollectionSiteId).HasColumnName("collection_site_id");
            b.Property(x => x.ReceivedBy).HasColumnName("received_by");
            b.Property(x => x.ReceivedAt).HasColumnName("received_at");
            b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            b.Property(x => x.EventId).HasColumnName("event_id");
            b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ux_procurements_company_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_procurements_company_id");
            b.HasIndex(x => x.EventId).IsUnique();
            b.HasIndex(x => new { x.CompanyId, x.ReceivedAt, x.Id });
        });
        modelBuilder.Entity<ProcurementItem>(b =>
        {
            b.ToTable("procurement_items", table =>
            {
                table.HasCheckConstraint("ck_procurement_items_net_mass", "net_mass_kg > 0");
                table.HasCheckConstraint("ck_procurement_items_measured", "measured_value > 0");
                table.HasCheckConstraint("ck_procurement_items_moisture", "moisture_percent >= 0 AND moisture_percent <= 100");
                table.HasCheckConstraint("ck_procurement_items_line_no", "line_no > 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ProcurementId).HasColumnName("procurement_id");
            b.Property(x => x.LineNo).HasColumnName("line_no");
            b.Property(x => x.PlotSeasonId).HasColumnName("plot_season_id");
            b.Property(x => x.NetMassKg).HasColumnName("net_mass_kg").HasPrecision(20, 6);
            b.Property(x => x.MeasuredValue).HasColumnName("measured_value").HasPrecision(24, 12);
            b.Property(x => x.MeasuredUnit).HasColumnName("measured_unit").HasMaxLength(20).IsRequired();
            b.Property(x => x.MoisturePercent).HasColumnName("moisture_percent").HasPrecision(24, 12);
            b.HasIndex(x => new { x.ProcurementId, x.LineNo }).IsUnique().HasDatabaseName("ux_procurement_items_receipt_line");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_procurement_items_company_id");
            b.HasIndex(x => x.PlotSeasonId);
        });
        modelBuilder.Entity<SourceLot>(b =>
        {
            b.ToTable("source_lots");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.ProcurementItemId).HasColumnName("procurement_item_id");
            b.HasIndex(x => x.LotId).IsUnique();
            b.HasIndex(x => x.ProcurementItemId).IsUnique();
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("ux_source_lots_company_id");
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
