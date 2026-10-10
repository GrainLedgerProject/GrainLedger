using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Identity;

public sealed class PermissionAuthorizationHandler(IdentityDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return;
        var access = await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => new { x.Role }).SingleOrDefaultAsync();
        if (access?.Role is { } role && Roles.All.TryGetValue(role, out var definition) && definition.Permissions.Contains(requirement.Permission))
            context.Succeed(requirement);
    }
}
