using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class AuthAcceptanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Acceptance!Password2026";

    private async Task<User> CreateUserAsync(string? role = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var email = $"qa-{Guid.NewGuid():N}@example.com";
        var user = new User { Provider = "local", ProviderSubject = Guid.NewGuid().ToString("N"), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), DisplayName = "QA User", Role = role };
        if (role is not null)
        {
            var kind = Roles.All[role].OrganizationKind;
            user.Organization = await db.Organizations.FirstOrDefaultAsync(x => x.Kind == kind)
                ?? new Organization { Name = kind + " QA", Kind = kind };
        }
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, Password);
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private static async Task<IssuedTokens> LoginAsync(HttpClient client, User user)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedTokens>())!;
    }

    [Theory]
    [InlineData(Roles.Administrator, HttpStatusCode.OK)]
    [InlineData(Roles.MillManager, HttpStatusCode.Forbidden)]
    [InlineData(Roles.MillOperator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.CustomerManager, HttpStatusCode.Forbidden)]
    [InlineData(Roles.ExportStaff, HttpStatusCode.Forbidden)]
    [InlineData(Roles.PurchasingStaff, HttpStatusCode.Forbidden)]
    public async Task Every_local_role_can_read_own_profile_but_only_admin_can_manage_accounts(string role, HttpStatusCode expected)
    {
        var user = await CreateUserAsync(role); using var client = factory.CreateClient();
        var tokens = await LoginAsync(client, user);
        Assert.Equal(role, tokens.User.Role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/api/admin/organizations")).StatusCode);
        Assert.Equal(expected, (await client.GetAsync("/api/admin/roles")).StatusCode);
    }

    [Fact]
    public async Task Local_bearer_preserves_ledger_role_grants_and_capture_tenant_boundaries()
    {
        using var client = factory.CreateClient();
        var manager = await CreateUserAsync(Roles.MillManager);
        var managerTokens = await LoginAsync(client, manager);
        foreach (var permission in new[] { "conversions.confirm", "corrections.reconcile", "receiving.reconcile" })
            Assert.Contains(permission, managerTokens.User.Permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ledger/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/capture-mock/farmers")).StatusCode);

        var operatorUser = await CreateUserAsync(Roles.MillOperator);
        var operatorTokens = await LoginAsync(client, operatorUser);
        Assert.Contains("lots.read.facility", operatorTokens.User.Permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ledger/lots")).StatusCode);

        var purchaser = await CreateUserAsync(Roles.PurchasingStaff);
        var purchaserTokens = await LoginAsync(client, purchaser);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", purchaserTokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ledger/lookups")).StatusCode);
        var farmerId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/capture-mock/farmers",
            new { id = farmerId, ownerOrganizationId = purchaser.OrganizationId, name = "Bearer farmer" })).StatusCode);
        var foreign = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/capture-mock/farmers",
            new { id = Guid.NewGuid(), ownerOrganizationId = foreign.Id, name = "Forbidden farmer" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ledger/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/capture-mock/farmers")).StatusCode);
    }

    [Fact]
    public async Task Disabling_local_user_rejects_existing_access_refresh_and_new_login()
    {
        var user = await CreateUserAsync(); using var client = factory.CreateClient();
        var tokens = await LoginAsync(client, user);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(x => x.Id == user.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { user.Email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Reset_code_five_failures_expiry_and_replay_are_rejected()
    {
        var user = await CreateUserAsync(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { user.Email })).StatusCode);
        var code = factory.Email.ResetCode(user.Email);
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new { user.Email, otp = wrong, newPassword = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new { user.Email, otp = code, newPassword = Password })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.PasswordResetCodes.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(x => x.SetProperty(c => c.LastSentAt, DateTimeOffset.UtcNow.AddMinutes(-2)));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { user.Email })).StatusCode);
        code = factory.Email.ResetCode(user.Email);
        await db.PasswordResetCodes.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ExecuteUpdateAsync(x => x.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new { user.Email, otp = code, newPassword = Password })).StatusCode);
        await db.PasswordResetCodes.Where(x => x.UserId == user.Id && x.ConsumedAt == null).ExecuteUpdateAsync(x => x.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(10)));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/reset-password", new { user.Email, otp = code, newPassword = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset-password", new { user.Email, otp = code, newPassword = Password })).StatusCode);
    }

    [Fact]
    public async Task Bearer_logout_all_revokes_both_login_sessions_and_refresh_tokens()
    {
        var user = await CreateUserAsync(); using var client = factory.CreateClient();
        var first = await LoginAsync(client, user); var second = await LoginAsync(client, user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout-all", null)).StatusCode);
        foreach (var tokens in new[] { first, second })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/profile")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken })).StatusCode);
        }
    }
}
