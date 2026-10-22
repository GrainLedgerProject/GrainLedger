using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Exports;

// A draft pins the demand only. It grants no stock reservation or execution authority.
public sealed class BlendPlanDraft
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ContainerId { get; set; }
    public Guid SpecificationId { get; set; }
    public int VersionNo { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "DRAFT";
    public decimal TargetMassKg { get; set; }
    public string DemandSnapshot { get; set; } = "{}";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public sealed class BlendPlanEvent
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ActorId { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset RecordedAt { get; set; }
}

internal static class BlendPlanModel
{
    public static void Configure(ModelBuilder m)
    {
        m.Entity<BlendPlanDraft>(b =>
        {
            b.ToTable("blend_plan_drafts", t =>
            {
                t.HasCheckConstraint("ck_plan_draft_versions", "version_no > 0 AND version > 0");
                t.HasCheckConstraint("ck_plan_draft_mass", "target_mass_kg > 0");
                t.HasCheckConstraint("ck_plan_draft_state", "(status = 'DRAFT' AND cancelled_by IS NULL AND cancelled_at IS NULL) OR (status = 'CANCELLED' AND cancelled_by IS NOT NULL AND cancelled_at IS NOT NULL)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.OrderId).HasColumnName("order_id");
            b.Property(x => x.ContainerId).HasColumnName("container_id");
            b.Property(x => x.SpecificationId).HasColumnName("specification_id");
            b.Property(x => x.VersionNo).HasColumnName("version_no");
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            b.Property(x => x.Status).HasColumnName("status");
            b.Property(x => x.TargetMassKg).HasColumnName("target_mass_kg").HasPrecision(20, 6);
            b.Property(x => x.DemandSnapshot).HasColumnName("demand_snapshot").HasColumnType("jsonb");
            b.Property(x => x.CreatedBy).HasColumnName("created_by");
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.Property(x => x.CancelledBy).HasColumnName("cancelled_by");
            b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
            b.HasIndex(x => new { x.ContainerId, x.VersionNo }).IsUnique();
            b.HasIndex(x => new { x.CompanyId, x.OrderId });
            b.HasIndex(x => new { x.CompanyId, x.Id }).IsUnique();
        });
        m.Entity<BlendPlanEvent>(b =>
        {
            b.ToTable("blend_plan_events"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.PlanId).HasColumnName("plan_id");
            b.Property(x => x.CompanyId).HasColumnName("company_id");
            b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.Action).HasColumnName("action");
            b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
            b.HasIndex(x => new { x.PlanId, x.RecordedAt, x.Id });
        });
    }
}

public sealed record CreateBlendPlanDraftRequest(Guid OperationId, int OrderVersion, int ContainerVersion,
    string? TargetProductIdentity);
public sealed record CancelBlendPlanDraftRequest(Guid OperationId);
public sealed record BlendPlanDraftDto(Guid Id, Guid OrderId, Guid ContainerId, Guid SpecificationId,
    int VersionNo, int Version, string Status, string TargetMassKg, string? TargetProductIdentity, string DemandSnapshot,
    DateTimeOffset CreatedAt, DateTimeOffset? CancelledAt, bool DemandCurrent, bool CanCancel);
public sealed record PlanningDependency(string Code, string Description);
public sealed record BlendPlanningContextDto(Guid ContainerId, int OrderVersion, int ContainerVersion,
    bool StorageReady, bool CanCreateDraft, bool CandidateReadReady, bool CompatibilityReadReady, bool CanPropose,
    IReadOnlyList<PlanningDependency> MissingDependencies);
