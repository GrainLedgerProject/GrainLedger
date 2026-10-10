using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

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
