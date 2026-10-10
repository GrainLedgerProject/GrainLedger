using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Identity;

public sealed class SessionCookieEvents(IdentityDbContext db, TimeProvider clock) : CookieAuthenticationEvents
{
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue("session_id"), out var sessionId))
        {
            context.RejectPrincipal(); return;
        }
        var now = clock.GetUtcNow();
        var session = await db.Sessions.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == sessionId, context.HttpContext.RequestAborted);
        var claimedUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (session is null || session.UserId.ToString() != claimedUserId || session.RevokedAt is not null || session.ExpiresAt <= now || !session.User.IsActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Application);
            return;
        }
        // Renew both clocks together. A conditional update cannot undo concurrent logout.
        var renewed = await db.Sessions.Where(x => x.Id == sessionId && x.RevokedAt == null && x.ExpiresAt > now && x.User.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, now.AddMinutes(15)), context.HttpContext.RequestAborted);
        if (renewed == 0) { context.RejectPrincipal(); return; }
        context.ShouldRenew = true;
    }
}
