using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

public sealed class ExportOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public Guid MarketId { get; set; }
    public Guid CommodityId { get; set; }
    public decimal RequiredMassKg { get; set; }
    public string OriginalMass { get; set; } = "";
    public string OriginalUnit { get; set; } = "";
    public DateOnly ShippingFrom { get; set; }
    public DateOnly ShippingTo { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
    public int CurrentSpecificationVersion { get; set; } = 1;
}

public sealed class OrderSpecification
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CompanyId { get; set; }
    public int VersionNo { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class SpecificationLimit
{
    public Guid Id { get; set; }
    public Guid SpecificationId { get; set; }
    public Guid AttributeId { get; set; }
    public string AttributeCode { get; set; } = "";
    public string AttributeName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
}

public sealed class ExportContainer
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CompanyId { get; set; }
    public string InternalCode { get; set; } = "";
    public decimal TargetMassKg { get; set; }
    public string OriginalMass { get; set; } = "";
    public string OriginalUnit { get; set; } = "";
    public string Status { get; set; } = "PLANNED";
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ExportEvent
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

public sealed class ExportOperation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ActorId { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public Guid OrderId { get; set; }
    public Guid? ContainerId { get; set; }
    public int? SpecificationVersion { get; set; }
    public string ResponseJson { get; set; } = "";
    public int ResponseStatus { get; set; }
    public int ResponseVersion { get; set; }
}

public sealed class ExportCodeCounter
{
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = "";
    public long NextValue { get; set; }
}

public sealed class ExportDbContext(DbContextOptions<ExportDbContext> options) : DbContext(options)
{
    public DbSet<ExportOrder> Orders => Set<ExportOrder>();
    public DbSet<OrderSpecification> Specifications => Set<OrderSpecification>();
    public DbSet<SpecificationLimit> Limits => Set<SpecificationLimit>();
    public DbSet<ExportContainer> Containers => Set<ExportContainer>();
    public DbSet<ExportEvent> Events => Set<ExportEvent>();
    public DbSet<ExportOperation> Operations => Set<ExportOperation>();
    public DbSet<ExportCodeCounter> Counters => Set<ExportCodeCounter>();
    public DbSet<BlendPlanDraft> BlendPlanDrafts => Set<BlendPlanDraft>();
    public DbSet<BlendPlanEvent> BlendPlanEvents => Set<BlendPlanEvent>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.HasDefaultSchema("exports");
        BlendPlanModel.Configure(m);
        m.Entity<ExportOrder>(b =>
        {
            b.ToTable("export_orders"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.Code).HasColumnName("code"); b.Property(x => x.MarketId).HasColumnName("market_id");
            b.Property(x => x.CommodityId).HasColumnName("commodity_id");
            b.Property(x => x.RequiredMassKg).HasColumnName("required_mass_kg").HasPrecision(20, 6);
            b.Property(x => x.OriginalMass).HasColumnName("original_mass"); b.Property(x => x.OriginalUnit).HasColumnName("original_unit");
            b.Property(x => x.ShippingFrom).HasColumnName("shipping_from"); b.Property(x => x.ShippingTo).HasColumnName("shipping_to");
            b.Property(x => x.CreatedBy).HasColumnName("created_by"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            b.Property(x => x.CurrentSpecificationVersion).HasColumnName("current_specification_version");
        });
        m.Entity<OrderSpecification>(b =>
        {
            b.ToTable("order_specifications"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.OrderId).HasColumnName("order_id");
            b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VersionNo).HasColumnName("version_no");
            b.Property(x => x.RecordedBy).HasColumnName("recorded_by"); b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.HasOne<ExportOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<SpecificationLimit>(b =>
        {
            b.ToTable("specification_limits"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.SpecificationId).HasColumnName("specification_id");
            b.Property(x => x.AttributeId).HasColumnName("attribute_id"); b.Property(x => x.AttributeCode).HasColumnName("attribute_code");
            b.Property(x => x.AttributeName).HasColumnName("attribute_name"); b.Property(x => x.Unit).HasColumnName("unit");
            b.Property(x => x.MinValue).HasColumnName("min_value").HasPrecision(24, 12);
            b.Property(x => x.MaxValue).HasColumnName("max_value").HasPrecision(24, 12);
            b.HasOne<OrderSpecification>().WithMany().HasForeignKey(x => x.SpecificationId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ExportContainer>(b =>
        {
            b.ToTable("containers"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.OrderId).HasColumnName("order_id");
            b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.InternalCode).HasColumnName("internal_code");
            b.Property(x => x.TargetMassKg).HasColumnName("target_mass_kg").HasPrecision(20, 6);
            b.Property(x => x.OriginalMass).HasColumnName("original_mass"); b.Property(x => x.OriginalUnit).HasColumnName("original_unit");
            b.Property(x => x.Status).HasColumnName("status"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasOne<ExportOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ExportEvent>(b =>
        {
            b.ToTable("events"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.OrderId).HasColumnName("order_id");
            b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.Action).HasColumnName("action"); b.Property(x => x.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.HasOne<ExportOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ExportOperation>(b =>
        {
            b.ToTable("idempotent_operations"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ActorId).HasColumnName("actor_id"); b.Property(x => x.OperationId).HasColumnName("operation_id");
            b.Property(x => x.RequestHash).HasColumnName("request_hash"); b.Property(x => x.OrderId).HasColumnName("order_id");
            b.Property(x => x.ContainerId).HasColumnName("container_id"); b.Property(x => x.SpecificationVersion).HasColumnName("specification_version");
            b.Property(x => x.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
            b.Property(x => x.ResponseStatus).HasColumnName("response_status");
            b.Property(x => x.ResponseVersion).HasColumnName("response_version");
            b.HasOne<ExportOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ExportCodeCounter>(b =>
        {
            b.ToTable("code_counters"); b.HasKey(x => new { x.CompanyId, x.Kind });
            b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.Kind).HasColumnName("kind");
            b.Property(x => x.NextValue).HasColumnName("next_value");
        });
    }
}
