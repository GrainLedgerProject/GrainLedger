using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GrainLedger.Modules.Identity;

public sealed class LocalAuthService(IdentityDbContext db, IPasswordHasher<User> passwordHasher, JwtConfiguration jwt, TimeProvider clock, IConfiguration configuration)
{
    public const int AccessTokenSeconds = 900;
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    public static string GenerateOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    public static string GenerateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    public string HashSecret(string purpose, string binding, string secret) => jwt.HashSecret(purpose, binding, secret);
    public static bool FixedTimeEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    public static bool IsStrongPassword(string password) => password.Length >= 12 && password.Length <= 128 &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c));

    public string HashPassword(User user, string password) => passwordHasher.HashPassword(user, password);
    public PasswordVerificationResult VerifyPassword(User user, string password) => passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, password);

    public async Task<IssuedTokens> IssueAsync(User user, HttpContext http, Guid? familyId = null, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow(); var refreshToken = GenerateRefreshToken();
        var session = new LoginSession
        {
            User = user, FamilyId = familyId ?? Guid.NewGuid(), RefreshTokenHash = HashSecret("refresh", "token", refreshToken),
            CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(30), IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = http.Request.Headers.UserAgent.ToString()[..Math.Min(http.Request.Headers.UserAgent.ToString().Length, 512)]
        };
        db.Sessions.Add(session); await db.SaveChangesAsync(cancellationToken);
        return new IssuedTokens(jwt.CreateAccessToken(user, session, now, TimeSpan.FromSeconds(AccessTokenSeconds)), refreshToken,
            "Bearer", AccessTokenSeconds, IdentityEndpoints.ToSessionDto(user));
    }

    public bool JwtConfigured => jwt.IsConfigured;

    public async Task BootstrapAdministratorAsync(User user, CancellationToken cancellationToken)
    {
        var bootstrap = configuration["Bootstrap:AdminEmail"]?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(bootstrap) || bootstrap != user.NormalizedEmail) return;
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(714928351)", cancellationToken);
        if (await db.Users.AnyAsync(x => x.Role == Roles.Administrator, cancellationToken)) return;
        var mill = await db.Organizations.SingleOrDefaultAsync(x => x.Kind == OrganizationKind.Mill, cancellationToken);
        if (mill is null) { mill = new Organization { Name = "Tiền Giang Mill", Kind = OrganizationKind.Mill }; db.Organizations.Add(mill); }
        user.Role = Roles.Administrator; user.Organization = mill;
    }
}
