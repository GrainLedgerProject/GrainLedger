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
        new Definition(Administrator, OrganizationKind.Mill, Permissions.ForRole(Administrator)),
        new Definition(MillManager, OrganizationKind.Mill, Permissions.ForRole(MillManager)),
        new Definition(MillOperator, OrganizationKind.Mill, Permissions.ForRole(MillOperator)),
        new Definition(CustomerManager, OrganizationKind.Customer, Permissions.ForRole(CustomerManager)),
        new Definition(ExportStaff, OrganizationKind.Customer, Permissions.ForRole(ExportStaff)),
        new Definition(PurchasingStaff, OrganizationKind.Customer, Permissions.ForRole(PurchasingStaff))
    }.ToDictionary(x => x.Name, StringComparer.Ordinal);
}
