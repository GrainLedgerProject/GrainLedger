using Microsoft.EntityFrameworkCore;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Catalogs;
using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Exports;

namespace GrainLedger.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<LoginSession> Sessions => Set<LoginSession>();
    public DbSet<IdentityAuditEvent> AuditEvents => Set<IdentityAuditEvent>();
    public DbSet<RegistrationAttempt> RegistrationAttempts => Set<RegistrationAttempt>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<ActiveSubstance> ActiveSubstances => Set<ActiveSubstance>();
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<Commodity> Commodities => Set<Commodity>();
    public DbSet<QualityAttribute> QualityAttributes => Set<QualityAttribute>();
    public DbSet<RiceVariety> RiceVarieties => Set<RiceVariety>();
    public DbSet<MeasurementUnit> MeasurementUnits => Set<MeasurementUnit>();
    public DbSet<MrlVersion> MrlVersions => Set<MrlVersion>();
    public DbSet<MrlLimit> MrlLimits => Set<MrlLimit>();
    public DbSet<CatalogEvent> CatalogEvents => Set<CatalogEvent>();
    public DbSet<Farmer> Farmers => Set<Farmer>();
    public DbSet<CaptureCodeCounter> CodeCounters => Set<CaptureCodeCounter>();
    public DbSet<Plot> Plots => Set<Plot>();
    public DbSet<CropSeason> CropSeasons => Set<CropSeason>();
    public DbSet<PlotSeason> PlotSeasons => Set<PlotSeason>();
    public DbSet<SprayDeclaration> SprayDeclarations => Set<SprayDeclaration>();
    public DbSet<SprayItem> SprayItems => Set<SprayItem>();
    public DbSet<Procurement> Procurements => Set<Procurement>();
    public DbSet<ProcurementItem> ProcurementItems => Set<ProcurementItem>();
    public DbSet<SourceLot> SourceLots => Set<SourceLot>();
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<SampleHandover> SampleHandovers => Set<SampleHandover>();
    public DbSet<LabReport> LabReports => Set<LabReport>();
    public DbSet<LabReportRetirement> LabReportRetirements => Set<LabReportRetirement>();
    public DbSet<LabResultSet> LabResultSets => Set<LabResultSet>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<LabReportConfirmation> LabReportConfirmations => Set<LabReportConfirmation>();
    public DbSet<LabConfirmedAssociation> LabConfirmedAssociations => Set<LabConfirmedAssociation>();
    public DbSet<CaptureOperation> OfflineOperations => Set<CaptureOperation>();
    public DbSet<CaptureBusinessEvent> BusinessEvents => Set<CaptureBusinessEvent>();
    public DbSet<ExportOrder> Orders => Set<ExportOrder>();
    public DbSet<OrderSpecification> Specifications => Set<OrderSpecification>();
    public DbSet<SpecificationLimit> Limits => Set<SpecificationLimit>();
    public DbSet<ExportContainer> Containers => Set<ExportContainer>();
    public DbSet<ExportEvent> Events => Set<ExportEvent>();
    public DbSet<ExportOperation> Operations => Set<ExportOperation>();
    public DbSet<ExportCodeCounter> Counters => Set<ExportCodeCounter>();
    public DbSet<BlendPlanDraft> BlendPlanDrafts => Set<BlendPlanDraft>();
    public DbSet<BlendPlanEvent> BlendPlanEvents => Set<BlendPlanEvent>();
    public DbSet<BlendPlanPreview> BlendPlanPreviews => Set<BlendPlanPreview>();
    public DbSet<BlendPlanProposal> BlendPlanProposals => Set<BlendPlanProposal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("grainledger");
        IdentityModel.Configure(modelBuilder);
        CatalogModel.Configure(modelBuilder);
        CaptureModel.Configure(modelBuilder);
        ExportModel.Configure(modelBuilder);
    }
}
