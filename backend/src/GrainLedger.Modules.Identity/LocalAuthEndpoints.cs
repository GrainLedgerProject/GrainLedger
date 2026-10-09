using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GrainLedger.Modules.Identity;

public sealed record RegisterRequest([property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(128)] string Password,
    [property: Required, StringLength(120, MinimumLength = 1)] string DisplayName);
public sealed record RegistrationPendingDto(Guid RegistrationId, string Email, int ExpiresIn);
public sealed record VerifyEmailRequest([property: Required, EmailAddress] string Email, Guid RegistrationId,
    [property: Required, RegularExpression("^[0-9]{6}$")] string Otp);
public sealed record ResendVerificationRequest([property: Required, EmailAddress] string Email, Guid RegistrationId);
public sealed record LoginRequest([property: Required, EmailAddress] string Email, [property: Required] string Password);
public sealed record RefreshRequest([property: Required] string RefreshToken);
public sealed record ForgotPasswordRequest([property: Required, EmailAddress] string Email);
public sealed record ResetPasswordRequest([property: Required, EmailAddress] string Email,
    [property: Required, RegularExpression("^[0-9]{6}$")] string Otp, [property: Required] string NewPassword);

public static partial class IdentityEndpoints
{
    internal static RouteGroupBuilder MapLocalAuthEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/register", RegisterAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("RegisterLocalAccount").WithDescription("Start local account registration and email a verification code.")
            .Produces<RegistrationPendingDto>(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        auth.MapPost("/verify-email", VerifyEmailAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("VerifyLocalEmail").WithDescription("Verify a registration with its six-digit email code.")
            .Produces<SessionUserDto>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        auth.MapPost("/resend-verification", ResendVerificationAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("ResendLocalVerification").WithDescription("Send a fresh verification code for the same registration attempt.")
            .Produces<RegistrationPendingDto>(StatusCodes.Status202Accepted).ProducesProblem(StatusCodes.Status429TooManyRequests);
        auth.MapPost("/login", LoginLocalAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("LoginLocalAccount").WithDescription("Sign in with a verified local email and password.").Produces<IssuedTokens>().ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/refresh", RefreshAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("RefreshLocalTokens").WithDescription("Rotate a single-use refresh token.").Produces<IssuedTokens>().ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/forgot-password", ForgotPasswordAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("ForgotLocalPassword").WithDescription("Email a password reset code when the verified local account exists.").Produces(StatusCodes.Status202Accepted);
        auth.MapPost("/reset-password", ResetPasswordAsync).AllowAnonymous().WithMetadata(new CredentialEndpointMetadata()).RequireRateLimiting("authentication")
            .WithName("ResetLocalPassword").WithDescription("Reset a verified local account password with its email code.").Produces(StatusCodes.Status204NoContent);
        return auth;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, HttpContext http, IdentityDbContext db, LocalAuthService auth,
        IEmailSender emailSender, TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured || !emailSender.IsConfigured) return Unavailable("Local registration is not configured.");
        var email = request.Email?.Trim().ToLowerInvariant() ?? ""; var normalized = LocalAuthService.NormalizeEmail(email);
        var displayName = request.DisplayName?.Trim() ?? "";
        if (!IsEmail(email) || displayName.Length is < 1 or > 120 || !LocalAuthService.IsStrongPassword(request.Password ?? ""))
            return ValidationProblem("A valid email, display name, and 12+ character mixed-case password with a digit and symbol are required.");
        var now = clock.GetUtcNow(); var otp = LocalAuthService.GenerateOtp(); var attempt = new RegistrationAttempt
        {
            Email = email, NormalizedEmail = normalized, DisplayName = displayName, PasswordHash = auth.HashPassword(new User
            { Provider = "local", ProviderSubject = Guid.NewGuid().ToString("N"), Email = email, NormalizedEmail = normalized, DisplayName = displayName }, request.Password!),
            CodeHash = "pending", CreatedAt = now, LastSentAt = now, ExpiresAt = now.AddMinutes(10)
        };
        attempt.CodeHash = auth.HashSecret("verify-email", attempt.Id.ToString("N"), otp);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        if (await db.RegistrationAttempts.AnyAsync(x => x.NormalizedEmail == normalized && x.LastSentAt > now.AddSeconds(-60), ct))
            return Results.Problem(statusCode: 429, title: "Wait before requesting another code.");
        if (await db.Users.AnyAsync(x => x.Provider == "local" && x.NormalizedEmail == normalized, ct))
            return Results.Conflict(new { title = "A local account already exists for this email." });
        await db.RegistrationAttempts.Where(x => x.NormalizedEmail == normalized && x.InvalidatedAt == null && x.ConsumedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.InvalidatedAt, now), ct);
        db.RegistrationAttempts.Add(attempt);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.registration_started", null, null, new { registrationId = attempt.Id, email }));
        await db.SaveChangesAsync(ct); await emailSender.SendVerificationCodeAsync(email, displayName, otp, ct); await tx.CommitAsync(ct);
        return Results.Accepted(value: new RegistrationPendingDto(attempt.Id, email, 600));
    }

    private static async Task<IResult> VerifyEmailAsync(VerifyEmailRequest request, HttpContext http, IdentityDbContext db, LocalAuthService auth,
        TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured) return Unavailable("Local authentication is not configured.");
        if (!IsEmail(request.Email ?? "") || request.RegistrationId == Guid.Empty || !IsOtp(request.Otp)) return InvalidCode();
        var normalized = LocalAuthService.NormalizeEmail(request.Email ?? ""); var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928351)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        var attempt = await db.RegistrationAttempts.FromSqlInterpolated($"SELECT * FROM identity.registration_attempts WHERE \"Id\" = {request.RegistrationId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (attempt is null || attempt.NormalizedEmail != normalized || attempt.InvalidatedAt != null || attempt.ConsumedAt != null || attempt.ExpiresAt <= now || attempt.FailedAttempts >= 5)
            return InvalidCode();
        var actual = auth.HashSecret("verify-email", attempt.Id.ToString("N"), request.Otp ?? "");
        if (!LocalAuthService.FixedTimeEquals(attempt.CodeHash, actual))
        {
            attempt.FailedAttempts++; db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.email_verification_failed", null, null, new { registrationId = attempt.Id }));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return InvalidCode();
        }
        if (await db.Users.AnyAsync(x => x.Provider == "local" && x.NormalizedEmail == normalized, ct)) return Results.Conflict(new { title = "A local account already exists for this email." });
        var user = new User { Provider = "local", ProviderSubject = Guid.NewGuid().ToString("N"), Email = attempt.Email, NormalizedEmail = normalized,
            DisplayName = attempt.DisplayName, PasswordHash = attempt.PasswordHash, CreatedAt = now, UpdatedAt = now };
        await auth.BootstrapAdministratorAsync(user, ct); db.Users.Add(user); attempt.ConsumedAt = now;
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "user.registered", user.Id, user.Id, new { provider = "local" }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(ToSessionDto(user));
    }

    private static async Task<IResult> ResendVerificationAsync(ResendVerificationRequest request, HttpContext http, IdentityDbContext db,
        LocalAuthService auth, IEmailSender emailSender, TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured || !emailSender.IsConfigured) return Unavailable("Local registration is not configured.");
        if (!IsEmail(request.Email ?? "") || request.RegistrationId == Guid.Empty) return InvalidCode();
        var normalized = LocalAuthService.NormalizeEmail(request.Email ?? ""); var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        var attempt = await db.RegistrationAttempts.FromSqlInterpolated($"SELECT * FROM identity.registration_attempts WHERE \"Id\" = {request.RegistrationId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (attempt is null || attempt.NormalizedEmail != normalized || attempt.InvalidatedAt != null || attempt.ConsumedAt != null) return InvalidCode();
        if (attempt.LastSentAt.AddSeconds(60) > now) return Results.Problem(statusCode: 429, title: "Wait before requesting another code.");
        var otp = LocalAuthService.GenerateOtp(); attempt.CodeHash = auth.HashSecret("verify-email", attempt.Id.ToString("N"), otp);
        attempt.FailedAttempts = 0; attempt.LastSentAt = now; attempt.ExpiresAt = now.AddMinutes(10);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.email_verification_resent", null, null, new { registrationId = attempt.Id }));
        await db.SaveChangesAsync(ct); await emailSender.SendVerificationCodeAsync(attempt.Email, attempt.DisplayName, otp, ct); await tx.CommitAsync(ct);
        return Results.Accepted(value: new RegistrationPendingDto(attempt.Id, attempt.Email, 600));
    }

    private static async Task<IResult> LoginLocalAsync(LoginRequest request, HttpContext http, IdentityDbContext db, LocalAuthService auth, TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured) return Unavailable("Local authentication is not configured.");
        if (!IsEmail(request.Email ?? "") || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128) return Results.Unauthorized();
        var normalized = LocalAuthService.NormalizeEmail(request.Email ?? ""); var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM identity.users WHERE \"Provider\" = 'local' AND \"NormalizedEmail\" = {normalized} FOR UPDATE").Include(x => x.Organization).SingleOrDefaultAsync(ct);
        if (user is null || !user.IsActive || user.LockoutEnd > now || user.PasswordHash is null || auth.VerifyPassword(user, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            if (user is not null && !(user.LockoutEnd > now)) { user.FailedLoginCount++; if (user.FailedLoginCount >= 5) { user.LockoutEnd = now.AddMinutes(15); user.FailedLoginCount = 0; } }
            db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.login_failed", user?.Id, user?.Id, new { provider = "local" })); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Results.Unauthorized();
        }
        user.FailedLoginCount = 0; user.LockoutEnd = null; db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.login", user.Id, user.Id, new { provider = "local" }));
        var tokens = await auth.IssueAsync(user, http, cancellationToken: ct); await tx.CommitAsync(ct); return Results.Ok(tokens);
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest request, HttpContext http, IdentityDbContext db, LocalAuthService auth, TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured) return Unavailable("Local authentication is not configured.");
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 512) return Results.Unauthorized();
        var hash = auth.HashSecret("refresh", "token", request.RefreshToken); var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var candidate = await db.Sessions.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, ct);
        if (candidate is null) return Results.Unauthorized();
        var refreshLock = candidate.User.NormalizedEmail ?? candidate.UserId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({refreshLock}))", ct);
        var session = await db.Sessions.FromSqlInterpolated($"SELECT * FROM identity.sessions WHERE \"RefreshTokenHash\" = {hash} FOR UPDATE")
            .Include(x => x.User).ThenInclude(x => x.Organization).SingleOrDefaultAsync(ct);
        if (session is null) return Results.Unauthorized();
        if (session.RevokedAt is not null || session.RefreshTokenUsedAt is not null)
        {
            await db.Sessions.Where(x => x.UserId == session.UserId && x.FamilyId == session.FamilyId && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, now), ct);
            db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.refresh_reuse", session.UserId, session.UserId, new { session.FamilyId }));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Unauthorized();
        }
        if (session.ExpiresAt <= now || !session.User.IsActive || session.User.LockoutEnd > now) return Results.Unauthorized();
        session.RefreshTokenUsedAt = now; var tokens = await auth.IssueAsync(session.User, http, session.FamilyId, ct);
        var replacementHash = auth.HashSecret("refresh", "token", tokens.RefreshToken);
        session.ReplacedBySessionId = await db.Sessions.Where(x => x.RefreshTokenHash == replacementHash).Select(x => x.Id).SingleAsync(ct);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.refresh", session.UserId, session.UserId, new { sessionId = session.Id }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(tokens);
    }

    private static async Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request, HttpContext http, IdentityDbContext db, LocalAuthService auth,
        IEmailSender emailSender, TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured || !emailSender.IsConfigured) return Unavailable("Password recovery is not configured.");
        if (!IsEmail(request.Email ?? "")) return Results.Accepted();
        var normalized = LocalAuthService.NormalizeEmail(request.Email ?? ""); var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Provider == "local" && x.NormalizedEmail == normalized && x.PasswordHash != null, ct);
        if (user is null) { await tx.CommitAsync(ct); return Results.Accepted(); }
        var recent = await db.PasswordResetCodes.AnyAsync(x => x.UserId == user.Id && x.ConsumedAt == null && x.LastSentAt > now.AddSeconds(-60), ct);
        if (recent) { await tx.CommitAsync(ct); return Results.Accepted(); }
        await db.PasswordResetCodes.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ExecuteUpdateAsync(x => x.SetProperty(c => c.ConsumedAt, now), ct);
        var otp = LocalAuthService.GenerateOtp(); var code = new PasswordResetCode { User = user, CodeHash = "pending", CreatedAt = now, LastSentAt = now, ExpiresAt = now.AddMinutes(10) };
        code.CodeHash = auth.HashSecret("reset-password", code.Id.ToString("N"), otp); db.PasswordResetCodes.Add(code);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.password_reset_requested", user.Id, user.Id, new { }));
        await db.SaveChangesAsync(ct); await emailSender.SendPasswordResetCodeAsync(user.Email, user.DisplayName, otp, ct); await tx.CommitAsync(ct); return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request, HttpContext http, IdentityDbContext db, LocalAuthService auth, TimeProvider clock, CancellationToken ct)
    {
        if (!auth.JwtConfigured) return Unavailable("Password recovery is not configured.");
        if (!IsEmail(request.Email ?? "") || !IsOtp(request.Otp)) return InvalidCode();
        if (!LocalAuthService.IsStrongPassword(request.NewPassword ?? "")) return ValidationProblem("The new password must be 12+ characters and include upper/lower case, a digit, and a symbol.");
        var normalized = LocalAuthService.NormalizeEmail(request.Email ?? ""); var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({normalized}))", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Provider == "local" && x.NormalizedEmail == normalized && x.PasswordHash != null, ct);
        if (user is null) return InvalidCode();
        var code = await db.PasswordResetCodes.FromSqlInterpolated($"SELECT * FROM identity.password_reset_codes WHERE \"UserId\" = {user.Id} AND \"ConsumedAt\" IS NULL ORDER BY \"CreatedAt\" DESC LIMIT 1 FOR UPDATE").SingleOrDefaultAsync(ct);
        if (code is null || code.ExpiresAt <= now || code.FailedAttempts >= 5) return InvalidCode();
        var actual = auth.HashSecret("reset-password", code.Id.ToString("N"), request.Otp ?? "");
        if (!LocalAuthService.FixedTimeEquals(code.CodeHash, actual)) { code.FailedAttempts++; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return InvalidCode(); }
        user.PasswordHash = auth.HashPassword(user, request.NewPassword!); user.FailedLoginCount = 0; user.LockoutEnd = null; user.Version = Guid.NewGuid(); user.UpdatedAt = now;
        await db.Sessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, now), ct);
        await db.PasswordResetCodes.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ExecuteUpdateAsync(x => x.SetProperty(c => c.ConsumedAt, now), ct);
        await db.RegistrationAttempts.Where(x => x.NormalizedEmail == normalized && x.InvalidatedAt == null && x.ConsumedAt == null).ExecuteUpdateAsync(x => x.SetProperty(a => a.InvalidatedAt, now), ct);
        db.AuditEvents.Add(IdentityHelpers.Audit(http, "auth.password_reset", user.Id, user.Id, new { })); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.NoContent();
    }

    private static bool IsEmail(string value) { if (value.Length is < 3 or > 320) return false; try { return new MailAddress(value).Address == value; } catch { return false; } }
    private static bool IsOtp(string? value) => value is { Length: 6 } && value.All(char.IsAsciiDigit);
    private static IResult InvalidCode() => Results.Problem(statusCode: 400, title: "The code is invalid or expired.");
    private static IResult Unavailable(string title) => Results.Problem(statusCode: 503, title: title);
}
