using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BrowserPasswordSessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Web_login_requires_csrf_returns_only_profile_and_restores_session_without_bearer()
    {
        using var client = Client();
        var email = await RegisterAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/web-login", new { email, password = "StrongPass!123" })).StatusCode);
        var login = await LoginAsync(client, email);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.False(body.TryGetProperty("refreshToken", out _));
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), x => x.StartsWith("__Host-GrainLedger.Session="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("secure", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        Assert.Contains("path=/", cookie.ToLowerInvariant());
        Assert.Contains("no-store", login.Headers.CacheControl!.ToString());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        // Reload has no JS tokens or Authorization header; the browser sends only its cookie.
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/profile", new { displayName = "Changed", phoneNumber = "" })).StatusCode);
        await CsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/profile", new { displayName = "Changed", phoneNumber = "" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var session = await db.Sessions.SingleAsync(x => x.User.Email == email);
        Assert.Null(session.RefreshTokenHash);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("logout-all")]
    [InlineData("disable")]
    [InlineData("expire")]
    [InlineData("password-reset")]
    [InlineData("access-change")]
    public async Task Revoked_or_expired_browser_session_cannot_be_restored(string action)
    {
        using var client = Client();
        var email = await RegisterAsync(client);
        await LoginAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        if (action is "logout" or "logout-all")
        {
            await CsrfAsync(client);
            var logout = await client.PostAsync($"/api/auth/{action}", null);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Contains(logout.Headers.GetValues("Set-Cookie"), x => x.StartsWith("__Host-GrainLedger.Session=;"));
        }
        else if (action == "password-reset")
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/reset-password", new { email, otp = factory.Email.ResetCode(email), newPassword = "ChangedPass!456" })).StatusCode);
        }
        else if (action == "access-change")
        {
            var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator);
            using var manager = factory.CreateRealCookieClient(admin, adminSession);
            using var scope = factory.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.SingleAsync(x => x.Email == email);
            manager.DefaultRequestHeaders.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{user.Version:N}\""));
            await CsrfAsync(manager);
            Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync($"/api/admin/users/{user.Id}/access", new { role = (string?)null, organizationId = (Guid?)null, isActive = false })).StatusCode);
        }
        else
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            if (action == "disable") await db.Users.Where(x => x.Email == email).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            else await db.Sessions.Where(x => x.User.Email == email).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);
    }

    [Fact]
    public async Task Web_password_login_retains_failed_password_lockout()
    {
        using var client = Client();
        var email = await RegisterAsync(client);
        await CsrfAsync(client);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/web-login", new { email, password = "WrongPass!123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/web-login", new { email, password = "StrongPass!123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"browser-{Guid.NewGuid():N}@example.com";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "StrongPass!123", displayName = "Browser User" });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        var pending = (await registration.Content.ReadFromJsonAsync<RegistrationPendingDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/verify-email", new { email, registrationId = pending.RegistrationId, otp = factory.Email.VerificationCode(email) })).StatusCode);
        return email;
    }

    private static async Task CsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email)
    {
        await CsrfAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/web-login", new { email, password = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response;
    }
}
