using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Identity;

public sealed class CurrentAccess(IHttpContextAccessor accessor, IdentityDbContext db) : ICurrentAccess
{
    public async Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var role = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => x.Role).SingleOrDefaultAsync(cancellationToken);
        return role is not null && Roles.All.TryGetValue(role, out var definition) && definition.Permissions.Contains(permission);
    }

    public async Task<bool> CanAccessOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return false;
        var access = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => new { x.Role, x.OrganizationId }).SingleOrDefaultAsync(cancellationToken);
        return access?.Role is not null && Roles.All.ContainsKey(access.Role) && access.OrganizationId == organizationId;
    }
}
