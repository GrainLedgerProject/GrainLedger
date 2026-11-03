using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Api.Features.Identity;

public sealed class SessionCookieEvents(AppDbContext db, TimeProvider clock) : CookieAuthenticationEvents
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
        var claimedUserId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(claimedUserId, out var userId) || userId.ToString() != claimedUserId)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Application);
            return;
        }
        var now = clock.GetUtcNow();
        // Renew both clocks together. A conditional update cannot undo concurrent logout.
        var renewed = await db.Sessions.Where(x => x.Id == sessionId && x.UserId == userId &&
                x.RevokedAt == null && x.ExpiresAt > now && x.User.IsActive)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.LastSeenAt, now).SetProperty(x => x.ExpiresAt, now.AddMinutes(15)), context.HttpContext.RequestAborted);
        if (renewed == 0)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(AuthSchemes.Application);
            return;
        }
        context.ShouldRenew = true;
    }
}
