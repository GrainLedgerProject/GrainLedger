using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LocalAuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Registration_binds_password_and_code_to_attempt_then_login_returns_tokens()
    {
        var email = $"local-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient();
        var first = await RegisterAsync(client, email, "FirstPass!123");
        await MakeRegistrationResendableAsync(first.RegistrationId);
        var second = await RegisterAsync(client, email, "SecondPass!123");
        var secondOtp = factory.Email.VerificationCode(email);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email",
            new { email, registrationId = first.RegistrationId, otp = secondOtp })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/verify-email",
            new { email, registrationId = second.RegistrationId, otp = secondOtp })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "FirstPass!123" })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "SecondPass!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString()); Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Bearer_can_access_and_mutate_profile_and_logout_revokes_it()
    {
        var email = $"bearer-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient();
        var tokens = await CreateAndLoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/profile", new { displayName = "Bearer User", phoneNumber = "+84900000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Refresh_rotation_replay_revokes_the_entire_family()
    {
        var email = $"rotate-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient();
        var original = await CreateAndLoginAsync(client, email);
        var rotatedResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
        var rotated = await ReadTokensAsync(rotatedResponse);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = original.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = rotated.RefreshToken })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_changes_password_and_revokes_all_sessions()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient();
        var old = await CreateAndLoginAsync(client, email);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode);
        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = factory.Email.ResetCode(email), newPassword = "ChangedPass!456" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", old.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "StrongPass!123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "ChangedPass!456" })).StatusCode);
    }

    [Fact]
    public async Task Google_identity_with_same_email_stays_a_separate_provider()
    {
        var email = $"shared-{Guid.NewGuid():N}@example.com";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(new User { Provider = "google", ProviderSubject = Guid.NewGuid().ToString("N"), Email = email, DisplayName = "Google User" });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient(); await CreateAndLoginAsync(client, email);
        using var verify = factory.Services.CreateScope(); var users = await verify.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Where(x => x.Email == email).OrderBy(x => x.Provider).ToListAsync();
        Assert.Equal(2, users.Count); Assert.Equal(["google", "local"], users.Select(x => x.Provider)); Assert.Null(users.Single(x => x.Provider == "google").PasswordHash);
    }

    [Fact]
    public async Task Bearer_admin_authorization_uses_database_role()
    {
        var email = $"admin-bearer-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient();
        await RegisterAndVerifyAsync(client, email);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>(); var user = await db.Users.SingleAsync(x => x.Provider == "local" && x.NormalizedEmail == email.ToUpperInvariant());
            user.Role = Roles.Administrator; await db.SaveChangesAsync();
        }
        var login = await LoginAsync(client, email, "StrongPass!123"); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/roles")).StatusCode);
    }

    [Fact]
    public async Task Verification_allows_only_five_failures_and_codes_are_single_use()
    {
        var email = $"otp-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient(); var pending = await RegisterAsync(client, email, "StrongPass!123");
        var correct = factory.Email.VerificationCode(email); var wrong = correct == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email", new { email, registrationId = pending.RegistrationId, otp = wrong })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email", new { email, registrationId = pending.RegistrationId, otp = correct })).StatusCode);
        using var scope = factory.Services.CreateScope(); var attempt = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().RegistrationAttempts.SingleAsync(x => x.Id == pending.RegistrationId);
        Assert.Equal(5, attempt.FailedAttempts);

        var replayEmail = $"replay-{Guid.NewGuid():N}@example.com"; var replay = await RegisterAsync(client, replayEmail, "StrongPass!123"); var otp = factory.Email.VerificationCode(replayEmail);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/verify-email", new { email = replayEmail, registrationId = replay.RegistrationId, otp })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email", new { email = replayEmail, registrationId = replay.RegistrationId, otp })).StatusCode);

        var expiredEmail = $"expired-{Guid.NewGuid():N}@example.com"; var expired = await RegisterAsync(client, expiredEmail, "StrongPass!123");
        using (var expiryScope = factory.Services.CreateScope())
            await expiryScope.ServiceProvider.GetRequiredService<IdentityDbContext>().RegistrationAttempts.Where(x => x.Id == expired.RegistrationId)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/verify-email",
            new { email = expiredEmail, registrationId = expired.RegistrationId, otp = factory.Email.VerificationCode(expiredEmail) })).StatusCode);
    }

    [Fact]
    public async Task Failed_email_delivery_returns_service_unavailable_and_rolls_back_registration()
    {
        var email = $"mailfail-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient(); factory.Email.FailDelivery = true;
        try
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "StrongPass!123", displayName = "No Mail" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally { factory.Email.FailDelivery = false; }
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().RegistrationAttempts.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Fact]
    public async Task Five_bad_passwords_lock_the_account_for_fifteen_minutes()
    {
        var email = $"lockout-{Guid.NewGuid():N}@example.com"; using var client = factory.CreateClient(); await RegisterAndVerifyAsync(client, email);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPass!999" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "StrongPass!123" })).StatusCode);
        using var scope = factory.Services.CreateScope(); var user = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task Invalid_or_expired_bearer_never_falls_back_to_a_valid_cookie()
    {
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff); using var client = factory.CreateRealCookieClient(user, session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("tests-only-signing-key-at-least-thirty-two-bytes-long"));
        var expired = new JwtSecurityToken("GrainLedger", "GrainLedger.Api",
            [new Claim("sub", user.Id.ToString()), new Claim("sid", session.Id.ToString()), new Claim("session_id", session.Id.ToString())],
            expires: DateTime.UtcNow.AddMinutes(-2), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(expired));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_and_logout_cannot_leave_a_successor_session_active()
    {
        var email = $"race-{Guid.NewGuid():N}@example.com"; using var setup = factory.CreateClient(); var tokens = await CreateAndLoginAsync(setup, email);
        using var refreshClient = factory.CreateClient(); using var logoutClient = factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var refreshTask = refreshClient.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        var logoutTask = logoutClient.PostAsync("/api/auth/logout", null); await Task.WhenAll(refreshTask, logoutTask);
        var logoutResponse = await logoutTask; var refreshResponse = await refreshTask;
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        if (refreshResponse.StatusCode == HttpStatusCode.OK)
        {
            var successor = await ReadTokensAsync(refreshResponse); refreshClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", successor.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await refreshClient.GetAsync("/api/auth/session")).StatusCode);
        }
        else Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    private async Task<RegistrationPendingDto> RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password, displayName = "Local User" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); return (await response.Content.ReadFromJsonAsync<RegistrationPendingDto>())!;
    }
    private async Task RegisterAndVerifyAsync(HttpClient client, string email)
    {
        var pending = await RegisterAsync(client, email, "StrongPass!123"); var response = await client.PostAsJsonAsync("/api/auth/verify-email",
            new { email, registrationId = pending.RegistrationId, otp = factory.Email.VerificationCode(email) }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private async Task<TokenPair> CreateAndLoginAsync(HttpClient client, string email) { await RegisterAndVerifyAsync(client, email); return await LoginAsync(client, email, "StrongPass!123"); }
    private static async Task<TokenPair> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await ReadTokensAsync(response);
    }
    private static async Task<TokenPair> ReadTokensAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); return new(json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }
    private async Task MakeRegistrationResendableAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.RegistrationAttempts.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(a => a.LastSentAt, DateTimeOffset.UtcNow.AddMinutes(-2)));
    }
    private sealed record TokenPair(string AccessToken, string RefreshToken);
}
