namespace GrainLedger.Modules.Identity;

public static class Permissions
{
    public const string IdentityManage = "identity.manage";
    public const string PlatformConfigure = "platform.configure";
    public const string TraceReadAll = "trace.read.all";
    public const string PlatformRead = "platform.read";
    public const string LotsReadAll = "lots.read.all";
    public const string ConversionsRead = "conversions.read";
    public const string ConversionsConfirm = "conversions.confirm";
    public const string CorrectionsReconcile = "corrections.reconcile";
    public const string ReceivingReconcile = "receiving.reconcile";
    public const string HoldsProcess = "holds.process";
    public const string ReconciliationRead = "reconciliation.read";
    public const string HistoryRead = "history.read";
    public const string LotsReadFacility = "lots.read.facility";
    public const string LotsReceive = "lots.receive";
    public const string ConversionsWrite = "conversions.write";
    public const string ContainersOperate = "containers.operate";
    public const string FarmsRead = "farms.read";
    public const string LotsReadTenant = "lots.read.tenant";
    public const string LabWrite = "lab.write";
    public const string BlendApprove = "blend.approve";
    public const string HoldsManage = "holds.manage";
    public const string TraceReadTenant = "trace.read.tenant";
    public const string MrlRead = "mrl.read";
    public const string LabRead = "lab.read";
    public const string BlendPropose = "blend.propose";
    public const string ContainersWrite = "containers.write";
    public const string FarmsWrite = "farms.write";
    public const string PurchasesWrite = "purchases.write";
    public const string ExportOrdersRead = "export-orders.read";
    public const string ExportOrdersWrite = "export-orders.write";
    public const string ResiduePolicyManage = "residue.policy.manage";
    public const string ResidueCompanyPolicyManage = "residue.company-policy.manage";
    public const string ResidueEvidenceManage = "residue.evidence.manage";
    public const string ResidueEvaluate = "residue.evaluate";
    public const string BlendAssessmentPolicyManage = "blend.assessment-policy.manage";
    public const string BlendYieldPolicyManage = "blend.yield-policy.manage";
    public const string BlendQualityEvidenceManage = "blend.quality-evidence.manage";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly<string>(
    [
        IdentityManage, PlatformConfigure, TraceReadAll, PlatformRead, LotsReadAll,
        ConversionsRead, ConversionsConfirm, CorrectionsReconcile, ReceivingReconcile,
        HoldsProcess, ReconciliationRead, HistoryRead, LotsReadFacility, LotsReceive,
        ConversionsWrite, ContainersOperate, FarmsRead, LotsReadTenant, LabWrite,
        BlendApprove, HoldsManage, TraceReadTenant, MrlRead, LabRead, BlendPropose,
        ContainersWrite, FarmsWrite, PurchasesWrite, ExportOrdersRead, ExportOrdersWrite,
        ResiduePolicyManage, ResidueCompanyPolicyManage, ResidueEvidenceManage, ResidueEvaluate,
        BlendAssessmentPolicyManage, BlendYieldPolicyManage, BlendQualityEvidenceManage
    ]);

    public static string[] ForRole(string role) => role switch
    {
        Roles.Administrator => [IdentityManage, PlatformConfigure, TraceReadAll, ResiduePolicyManage],
        Roles.MillManager => [PlatformRead, LotsReadAll, ConversionsRead, ConversionsConfirm, CorrectionsReconcile, ReceivingReconcile, HoldsProcess, ReconciliationRead, HistoryRead, ResidueEvaluate, BlendYieldPolicyManage],
        Roles.MillOperator => [PlatformRead, LotsReadFacility, LotsReceive, ConversionsWrite, ContainersOperate, HistoryRead, ResidueEvaluate],
        Roles.CustomerManager => [FarmsRead, LotsReadTenant, LabWrite, BlendApprove, HoldsManage, TraceReadTenant, HistoryRead, ExportOrdersRead, ResidueCompanyPolicyManage, ResidueEvidenceManage, ResidueEvaluate, BlendAssessmentPolicyManage, BlendQualityEvidenceManage],
        Roles.ExportStaff => [MrlRead, LotsReadTenant, LabRead, BlendPropose, ContainersWrite, TraceReadTenant, HistoryRead, ExportOrdersRead, ExportOrdersWrite, ResidueEvaluate],
        Roles.PurchasingStaff => [FarmsWrite, PurchasesWrite, HistoryRead],
        _ => []
    };
}
