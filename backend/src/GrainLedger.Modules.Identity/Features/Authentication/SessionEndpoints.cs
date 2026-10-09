using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GrainLedger.Modules.Identity;

public static partial class IdentityEndpoints
{
    private static void MapAuthenticationEndpoints(RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth").WithTags("Authentication");
        auth.MapLocalAuthEndpoints();
        auth.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new { token = tokens.RequestToken });
        });
        auth.MapGet("/google", (IConfiguration configuration, string? returnUrl) =>
        {
            if (string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) || string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]))
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Google authentication is not configured");
            var safeReturnUrl = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
            return Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = "/api/auth/google/complete?returnUrl=" + Uri.EscapeDataString(safeReturnUrl)
            }, [AuthSchemes.Google]);
        }).RequireRateLimiting("authentication").Produces(StatusCodes.Status302Found).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        auth.MapGet("/google/complete", CompleteGoogleLoginAsync).Produces(StatusCodes.Status302Found);
        auth.MapGet("/session", GetSessionAsync).RequireAuthorization().Produces<SessionUserDto>().Produces(StatusCodes.Status401Unauthorized);
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization().Produces(StatusCodes.Status204NoContent);
        auth.MapPost("/logout-all", LogoutAllAsync).RequireAuthorization().Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetSessionAsync(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Organization).SingleOrDefaultAsync(x => x.Id == principal.RequiredUserId() && x.IsActive, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(ToSessionDto(user));
    }

    private static async Task<IResult> LogoutAsync(ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var userId = principal.RequiredUserId();
        var account = await db.Users.Where(x => x.Id == userId).Select(x => new { x.Id, x.NormalizedEmail }).SingleAsync(ct);
        var accountKey = account.NormalizedEmail ?? account.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({accountKey}))", ct);
        if (Guid.TryParse(principal.FindFirstValue("session_id"), out var sid))
        {
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sid, ct);
            if (session is not null)
                await db.Sessions.Where(x => x.UserId == session.UserId && x.FamilyId == session.FamilyId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow), ct);
            db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.logout", principal.RequiredUserId(), principal.RequiredUserId(), new { sessionId = sid }));
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        await http.SignOutAsync(AuthSchemes.Application);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(ClaimsPrincipal principal, HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        var id = principal.RequiredUserId(); var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var account = await db.Users.Where(x => x.Id == id).Select(x => new { x.Id, x.NormalizedEmail }).SingleAsync(ct);
        var accountKey = account.NormalizedEmail ?? account.Id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({accountKey}))", ct);
        await db.Sessions.Where(x => x.UserId == id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, now), ct);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.logout_all", id, id, new { })); await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await http.SignOutAsync(AuthSchemes.Application);
        return Results.NoContent();
    }

    internal static SessionUserDto ToSessionDto(User user) => new(user.Id, user.Email, user.DisplayName, user.PhoneNumber, user.Role,
        user.OrganizationId, user.Organization?.Name, user.IsActive,
        user.Role is not null && Roles.All.TryGetValue(user.Role, out var role) ? role.Permissions : []);
}
