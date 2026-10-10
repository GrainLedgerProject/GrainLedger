using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace GrainLedger.Modules.Identity;

public static partial class IdentityEndpoints
{
    private static async Task<IResult> CompleteGoogleLoginAsync(HttpContext http, IdentityDbContext db, GoogleIdentityProvisioner provisioner, TimeProvider clock, string? returnUrl)
    {
        var external = await http.AuthenticateAsync(AuthSchemes.External);
        if (!external.Succeeded || external.Principal is null) return Results.Redirect(GoogleLoginDiagnostics.FailureRedirect(http, "external_session"));
        var principal = external.Principal;
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var verified = string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || !verified)
        {
            await http.SignOutAsync(AuthSchemes.External);
            return Results.Redirect(GoogleLoginDiagnostics.FailureRedirect(http,
                string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) ? "incomplete_identity" : "unverified_email"));
        }

        var now = clock.GetUtcNow();
        var (user, created) = await provisioner.ProvisionAsync(new GoogleIdentity(subject, email,
            principal.FindFirstValue(ClaimTypes.Name)?.Trim() ?? email, verified), http.RequestAborted);
        if (!user.IsActive)
        {
            await http.SignOutAsync(AuthSchemes.External);
            return Results.Redirect(GoogleLoginDiagnostics.FailureRedirect(http, "inactive"));
        }

        var session = new LoginSession
        {
            User = user, ExpiresAt = now.AddMinutes(15), IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http.Request.Headers.UserAgent.ToString()[..Math.Min(http.Request.Headers.UserAgent.ToString().Length, 512)]
        };
        db.Sessions.Add(session);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, created ? "user.provisioned" : "auth.login", user.Id, user.Id, new { provider = "google" }));
        await db.SaveChangesAsync(http.RequestAborted);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("session_id", session.Id.ToString()) };
        await http.SignInAsync(AuthSchemes.Application, new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Application)),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = session.ExpiresAt, AllowRefresh = true });
        await http.SignOutAsync(AuthSchemes.External);
        return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/");
    }

    private static bool IsLocalReturnUrl(string? value) => !string.IsNullOrWhiteSpace(value) && value[0] == '/' &&
        (value.Length == 1 || value[1] != '/' && value[1] != '\\') && !value.Contains('\\') &&
        !value.Any(char.IsControl) && Uri.TryCreate(value, UriKind.Relative, out _);
}
