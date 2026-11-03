using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Api.Features.Capture;

public sealed class Farmer
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string Code { get; set; }
    public long? CodeNumber { get; set; }
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

internal static class CaptureModel
{
    internal static void Configure(ModelBuilder modelBuilder)
    {
        LabReportMappings.Configure(modelBuilder);
        modelBuilder.Entity<CaptureCodeCounter>(b =>
        {
            b.ToTable("capture_code_counters");
            b.HasKey(x => new { x.CompanyId, x.Kind, x.Scope });
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(3);
            b.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(32);
            b.Property(x => x.NextValue).HasColumnName("next_value");
        });
        modelBuilder.Entity<Farmer>(b =>
        {
            b.ToTable("capture_farmers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            b.Property(x => x.CodeNumber).HasColumnName("code_number");
            b.HasIndex(x => new { x.CompanyId, x.CodeNumber }).IsUnique()
                .HasDatabaseName("capture_ux_farmers_company_code_number");
            b.Property(x => x.Name).HasColumnName("name").IsRequired();
            b.Property(x => x.Contact).HasColumnName("contact");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsConcurrencyToken();
            b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("capture_ux_farmers_company_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_farmers_company_id");
            b.HasIndex(x => new { x.CompanyId, x.CreatedAt, x.Id });
            b.Property(x => x.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);
            b.HasQueryFilter(x => !x.IsDeleted);
        });
        modelBuilder.Entity<Plot>(b =>
        {
            b.ToTable("capture_plots", table => table.HasCheckConstraint("capture_ck_plots_area_ha_positive", "area_ha > 0"));
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
            b.HasIndex(x => new { x.FarmerId, x.Code }).IsUnique().HasDatabaseName("capture_ux_plots_farmer_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_plots_company_id");
        });
        modelBuilder.Entity<CropSeason>(b =>
        {
            b.ToTable("capture_crop_seasons", table => table.HasCheckConstraint("capture_ck_crop_seasons_dates", "ends_on >= starts_on"));
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
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_crop_seasons_company_id");
        });
        modelBuilder.Entity<PlotSeason>(b =>
        {
            b.ToTable("capture_plot_seasons");
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
            b.HasIndex(x => new { x.PlotId, x.SeasonId }).IsUnique().HasDatabaseName("capture_ux_plot_seasons_plot_season");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_plot_seasons_company_id");
            b.HasIndex(x => x.SeasonId);
        });
        modelBuilder.Entity<SprayDeclaration>(b =>
        {
            b.ToTable("capture_spray_declarations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.PlotSeasonId).HasColumnName("plot_season_id");
            b.Property(x => x.AppliedOn).HasColumnName("applied_on");
            b.Property(x => x.RecordedBy).HasColumnName("recorded_by");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.Property(x => x.EvidenceRef).HasColumnName("evidence_ref");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_spray_declarations_company_id");
            b.HasIndex(x => x.PlotSeasonId);
            b.HasIndex(x => x.RecordedBy);
        });
        modelBuilder.Entity<SprayItem>(b =>
        {
            b.ToTable("capture_spray_items", table =>
            {
                table.HasCheckConstraint("capture_ck_spray_items_product_or_substance", "product_name IS NOT NULL OR substance_id IS NOT NULL");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.DeclarationId).HasColumnName("declaration_id");
            b.Property(x => x.ProductName).HasColumnName("product_name");
            b.Property(x => x.SubstanceId).HasColumnName("substance_id");
            b.Property(x => x.Rate).HasColumnName("rate").HasPrecision(24, 12);
            b.Property(x => x.RateUnit).HasColumnName("rate_unit").HasMaxLength(40);
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_spray_items_company_id");
            b.HasIndex(x => x.DeclarationId);
            b.HasIndex(x => x.SubstanceId);
        });
        modelBuilder.Entity<Procurement>(b =>
        {
            b.ToTable("capture_procurements", table => table.HasCheckConstraint("capture_ck_procurements_status", "status IN ('DRAFT', 'COMMITTED')"));
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
            b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("capture_ux_procurements_company_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_procurements_company_id");
            b.HasIndex(x => x.EventId).IsUnique();
            b.HasIndex(x => new { x.CompanyId, x.ReceivedAt, x.Id });
        });
        modelBuilder.Entity<ProcurementItem>(b =>
        {
            b.ToTable("capture_procurement_items", table =>
            {
                table.HasCheckConstraint("capture_ck_procurement_items_net_mass", "net_mass_kg > 0");
                table.HasCheckConstraint("capture_ck_procurement_items_measured", "measured_value > 0");
                table.HasCheckConstraint("capture_ck_procurement_items_moisture", "moisture_percent >= 0 AND moisture_percent <= 100");
                table.HasCheckConstraint("capture_ck_procurement_items_line_no", "line_no > 0");
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
            b.HasIndex(x => new { x.ProcurementId, x.LineNo }).IsUnique().HasDatabaseName("capture_ux_procurement_items_receipt_line");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_procurement_items_company_id");
            b.HasIndex(x => x.PlotSeasonId);
        });
        modelBuilder.Entity<SourceLot>(b =>
        {
            b.ToTable("capture_source_lots");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.LotId).HasColumnName("lot_id");
            b.Property(x => x.ProcurementItemId).HasColumnName("procurement_item_id");
            b.HasIndex(x => x.LotId).IsUnique();
            b.HasIndex(x => x.ProcurementItemId).IsUnique();
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_source_lots_company_id");
        });
        modelBuilder.Entity<Sample>(b =>
        {
            b.ToTable("capture_samples");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            b.Property(x => x.SourceLotId).HasColumnName("source_lot_id");
            b.Property(x => x.SealCode).HasColumnName("seal_code").HasMaxLength(100).IsRequired();
            b.Property(x => x.CollectedBy).HasColumnName("collected_by");
            b.Property(x => x.CollectedAt).HasColumnName("collected_at");
            b.Property(x => x.MaterialDescription).HasColumnName("material_description").HasMaxLength(200);
            b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("capture_ux_samples_company_code");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_samples_company_id");
            b.HasIndex(x => x.SourceLotId);
            b.HasIndex(x => x.CollectedBy);
        });
        modelBuilder.Entity<SampleHandover>(b =>
        {
            b.ToTable("capture_sample_handovers", t =>
            {
                t.HasCheckConstraint("capture_ck_sample_handovers_kind", "handover_kind IN ('HANDOVER','LAB_DISPATCH','LAB_ACKNOWLEDGEMENT')");
                t.HasCheckConstraint("capture_ck_sample_handovers_lab", "handover_kind = 'HANDOVER' OR (laboratory_name IS NOT NULL AND length(btrim(laboratory_name)) > 0)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.SampleId).HasColumnName("sample_id");
            b.Property(x => x.RecordedBy).HasColumnName("recorded_by");
            b.Property(x => x.FromParty).HasColumnName("from_party").IsRequired();
            b.Property(x => x.ToParty).HasColumnName("to_party").IsRequired();
            b.Property(x => x.HandedOverAt).HasColumnName("handed_over_at");
            b.Property(x => x.SealCondition).HasColumnName("seal_condition").IsRequired();
            b.Property(x => x.EvidenceRef).HasColumnName("evidence_ref");
            b.Property(x => x.HandoverKind).HasColumnName("handover_kind").HasMaxLength(32).HasDefaultValue("HANDOVER");
            b.Property(x => x.LaboratoryName).HasColumnName("laboratory_name").HasMaxLength(200);
            b.Property(x => x.LaboratoryReference).HasColumnName("laboratory_reference").HasMaxLength(200);
            b.Property(x => x.LaboratorySampleReference).HasColumnName("laboratory_sample_reference").HasMaxLength(200);
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique().HasDatabaseName("capture_ux_sample_handovers_company_id");
            b.HasIndex(x => x.SampleId);
            b.HasIndex(x => x.RecordedBy);
        });
        modelBuilder.Entity<CaptureOperation>(b =>
        {
            b.ToTable("capture_offline_operations");
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
                .HasDatabaseName("capture_ux_offline_operations_company_device_operation");
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique();
            b.HasIndex(x => x.SubmittedBy);
            b.HasIndex(x => x.AppliedEventId).IsUnique();
        });
        modelBuilder.Entity<CaptureBusinessEvent>(b =>
        {
            b.ToTable("capture_business_events");
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
