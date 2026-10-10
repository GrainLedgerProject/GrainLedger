namespace GrainLedger.Modules.Identity;

public static class Roles
{
    public const string Administrator = nameof(Administrator);
    public const string MillManager = nameof(MillManager);
    public const string MillOperator = nameof(MillOperator);
    public const string CustomerManager = nameof(CustomerManager);
    public const string ExportStaff = nameof(ExportStaff);
    public const string PurchasingStaff = nameof(PurchasingStaff);

    public sealed record Definition(string Name, OrganizationKind OrganizationKind, string[] Permissions);

    public static readonly IReadOnlyDictionary<string, Definition> All = new[]
    {
        new Definition(Administrator, OrganizationKind.Mill, ["identity.manage", "platform.configure", "trace.read.all"]),
        new Definition(MillManager, OrganizationKind.Mill, ["platform.read", "lots.read.all", "conversions.read", "conversions.confirm", "corrections.reconcile", "receiving.reconcile", "holds.process", "reconciliation.read", "history.read"]),
        new Definition(MillOperator, OrganizationKind.Mill, ["platform.read", "lots.read.facility", "lots.receive", "conversions.write", "containers.operate", "history.read"]),
        new Definition(CustomerManager, OrganizationKind.Customer, ["farms.read", "lots.read.tenant", "lab.write", "blend.approve", "holds.manage", "trace.read.tenant", "history.read"]),
        new Definition(ExportStaff, OrganizationKind.Customer, ["mrl.read", "lots.read.tenant", "lab.read", "blend.propose", "containers.write", "trace.read.tenant", "history.read"]),
        new Definition(PurchasingStaff, OrganizationKind.Customer, ["farms.write", "purchases.write", "history.read"])
    }.ToDictionary(x => x.Name, StringComparer.Ordinal);
}
