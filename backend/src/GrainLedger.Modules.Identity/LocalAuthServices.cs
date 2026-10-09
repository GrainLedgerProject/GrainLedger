using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace GrainLedger.Modules.Identity;

public sealed class JwtConfiguration
{
    private readonly byte[] _key;
    public bool IsConfigured { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public SymmetricSecurityKey SigningKey { get; }

    public JwtConfiguration(IConfiguration configuration)
    {
        var configured = configuration["Authentication:Jwt:SigningKey"];
        _key = string.IsNullOrEmpty(configured) ? RandomNumberGenerator.GetBytes(32) : Encoding.UTF8.GetBytes(configured);
        IsConfigured = !string.IsNullOrWhiteSpace(configured) && _key.Length >= 32;
        Issuer = string.IsNullOrWhiteSpace(configuration["Authentication:Jwt:Issuer"]) ? "GrainLedger" : configuration["Authentication:Jwt:Issuer"]!.Trim();
        Audience = string.IsNullOrWhiteSpace(configuration["Authentication:Jwt:Audience"]) ? "GrainLedger.Api" : configuration["Authentication:Jwt:Audience"]!.Trim();
        SigningKey = new SymmetricSecurityKey(_key);
    }

    public string CreateAccessToken(User user, LoginSession session, DateTimeOffset now, TimeSpan lifetime)
    {
        if (!IsConfigured) throw new InvalidOperationException("JWT authentication is not configured.");
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", user.Id.ToString()), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("sid", session.Id.ToString()),
                new Claim("session_id", session.Id.ToString()), new Claim("provider", user.Provider)],
            now.UtcDateTime, now.Add(lifetime).UtcDateTime, new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string HashSecret(string purpose, string binding, string secret)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(purpose + ":" + binding + ":" + secret)));
    }
}

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken);
    Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken);
}

public sealed class EmailDeliveryException : Exception
{
    public string Stage { get; }
    public string FailureType { get; }
    public int? SmtpStatus { get; }

    public EmailDeliveryException() : this("unknown", null) { }

    public EmailDeliveryException(string stage, Exception? cause) : base("Email delivery is temporarily unavailable.")
    {
        Stage = stage;
        FailureType = cause?.GetType().Name ?? "Unknown";
        SmtpStatus = cause is SmtpCommandException smtp ? (int)smtp.StatusCode : null;
    }
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Mail:Smtp:Host"]) &&
        !string.IsNullOrWhiteSpace(configuration["Mail:FromAddress"]);

    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, "Verify your GrainLedger email", $"Your GrainLedger verification code is {code}. It expires in 10 minutes.", cancellationToken);

    public Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken cancellationToken) =>
        SendAsync(email, displayName, "Reset your GrainLedger password", $"Your GrainLedger password reset code is {code}. It expires in 10 minutes.", cancellationToken);

    private async Task SendAsync(string email, string displayName, string subject, string text, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email delivery is not configured.");
        var stage = "message";
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(configuration["Mail:FromName"] ?? "GrainLedger", configuration["Mail:FromAddress"]!));
            message.To.Add(new MailboxAddress(displayName, email)); message.Subject = subject; message.Body = new TextPart("plain") { Text = text };
            using var client = new SmtpClient { Timeout = 15000 };
            var port = int.TryParse(configuration["Mail:Smtp:Port"], out var parsed) ? parsed : 587;
            var tls = !bool.TryParse(configuration["Mail:Smtp:UseStartTls"], out var startTls) || startTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
            stage = "connect-tls";
            await client.ConnectAsync(configuration["Mail:Smtp:Host"]!, port, tls, cancellationToken);
            var username = configuration["Mail:Smtp:Username"];
            if (!string.IsNullOrWhiteSpace(username))
            {
                stage = "authenticate";
                await client.AuthenticateAsync(username, configuration["Mail:Smtp:Password"] ?? "", cancellationToken);
            }
            stage = "send";
            await client.SendAsync(message, cancellationToken); await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw new EmailDeliveryException(stage, exception); }
    }
}

public sealed record IssuedTokens(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, SessionUserDto User);

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
