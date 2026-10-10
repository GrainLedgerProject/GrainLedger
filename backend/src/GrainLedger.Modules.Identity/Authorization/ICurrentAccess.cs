namespace GrainLedger.Modules.Identity;

public interface ICurrentAccess
{
    Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default);
    Task<bool> CanAccessOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
