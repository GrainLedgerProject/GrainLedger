using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class IdentityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<string> AddCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = csrf.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        return token;
    }

    [Fact]
    public async Task Logout_does_not_revoke_another_users_session_with_same_family()
    {
        var (first, firstSession) = await factory.SeedUserAsync(Roles.ExportStaff);
        var (second, secondSession) = await factory.SeedUserAsync(Roles.ExportStaff);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Sessions.Where(x => x.Id == firstSession.Id || x.Id == secondSession.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.FamilyId, Guid.Empty));
        }
        using var firstClient = factory.CreateRealCookieClient(first, firstSession);
        using var secondClient = factory.CreateRealCookieClient(second, secondSession);
        await AddCsrfAsync(firstClient);
        Assert.Equal(HttpStatusCode.NoContent, (await firstClient.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await firstClient.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Session_requires_authentication()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Revoked_server_session_is_rejected()
    {
        var (user, session) = await factory.SeedUserAsync(Roles.CustomerManager);
        using var client = factory.CreateRealCookieClient(user, session);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Sessions.Where(x => x.Id == session.Id).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTimeOffset.UtcNow));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Pending_user_has_no_admin_access_and_mutations_require_csrf()
    {
        var (pending, session) = await factory.SeedUserAsync(null!);
        using var pendingClient = factory.CreateRealCookieClient(pending, session);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendingClient.GetAsync("/api/admin/users")).StatusCode);

        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        var response = await adminClient.PostAsJsonAsync("/api/admin/organizations", new { name = "Customer A", kind = "Customer" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Google_provisioning_requires_verified_email_uses_subject_and_bootstraps_exact_email()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Sessions.ExecuteDeleteAsync(); await db.AuditEvents.ExecuteDeleteAsync(); await db.Users.ExecuteDeleteAsync(); await db.Organizations.ExecuteDeleteAsync();
        var provisioner = scope.ServiceProvider.GetRequiredService<GoogleIdentityProvisioner>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(new("bad", "admin@example.com", "Bad", false)));
        var (admin, created) = await provisioner.ProvisionAsync(new("subject-admin", "ADMIN@example.com", "Admin", true));
        Assert.True(created); Assert.Equal(Roles.Administrator, admin.Role);
        await db.SaveChangesAsync();

        var (other, otherCreated) = await provisioner.ProvisionAsync(new("different-subject", "admin@example.com", "Other", true));
        Assert.True(otherCreated); Assert.NotEqual(admin.Id, other.Id); Assert.Null(other.Role);
    }

    [Fact]
    public async Task Production_cookie_is_bound_to_its_server_session_user()
    {
        var (first, _) = await factory.SeedUserAsync(Roles.ExportStaff);
        var (_, secondSession) = await factory.SeedUserAsync(Roles.ExportStaff);
        using var client = factory.CreateRealCookieClient(first, secondSession);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Admin_access_update_requires_matching_etag_and_protects_self()
    {
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator);
        using var client = factory.CreateRealCookieClient(admin, session);
        await AddCsrfAsync(client);

        var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{admin.Id}/access") { Content = JsonContent.Create(new { role = Roles.Administrator, organizationId = (Guid?)null, isActive = true }) };
        stale.Headers.TryAddWithoutValidation("If-Match", Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);

        var selfDisable = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{admin.Id}/access") { Content = JsonContent.Create(new { role = Roles.Administrator, organizationId = (Guid?)null, isActive = false }) };
        selfDisable.Headers.TryAddWithoutValidation("If-Match", admin.Version.ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(selfDisable)).StatusCode);
    }

    [Fact]
    public async Task Profile_edit_uses_real_cookie_and_csrf_without_changing_access()
    {
        var organization = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, organization.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);

        var response = await client.PutAsJsonAsync("/api/profile", new { displayName = "Updated Name", phoneNumber = "+84901234567" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Name", body.GetProperty("displayName").GetString());
        Assert.Equal(Roles.ExportStaff, body.GetProperty("role").GetString());
        Assert.DoesNotContain("identity.manage", body.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Logout_revokes_current_session_and_cookie_replay_fails()
    {
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Logout_all_revokes_every_session()
    {
        var (user, firstSession) = await factory.SeedUserAsync(Roles.ExportStaff);
        var secondSession = await factory.SeedSessionAsync(user);
        using var first = factory.CreateRealCookieClient(user, firstSession);
        using var second = factory.CreateRealCookieClient(user, secondSession);
        await AddCsrfAsync(first);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/auth/logout-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Admin_access_change_validates_scope_and_immediately_revokes_target_sessions()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (target, targetSession) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var targetClient = factory.CreateRealCookieClient(target, targetSession);
        await AddCsrfAsync(adminClient);

        var invalid = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{target.Id}/access") { Content = JsonContent.Create(new { role = Roles.CustomerManager, organizationId = mill.Id, isActive = true }) };
        invalid.Headers.TryAddWithoutValidation("If-Match", target.Version.ToString("N"));
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.SendAsync(invalid)).StatusCode);

        var valid = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{target.Id}/access") { Content = JsonContent.Create(new { role = Roles.PurchasingStaff, organizationId = customer.Id, isActive = true }) };
        valid.Headers.TryAddWithoutValidation("If-Match", target.Version.ToString("N"));
        Assert.Equal(HttpStatusCode.OK, (await adminClient.SendAsync(valid)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Tenant_scope_allows_only_the_users_assigned_organization()
    {
        var own = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var other = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, _) = await factory.SeedUserAsync(Roles.CustomerManager, own.Id);
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"))
        };
        try
        {
            var access = scope.ServiceProvider.GetRequiredService<ICurrentAccess>();
            Assert.True(await access.CanAccessOrganizationAsync(own.Id));
            Assert.False(await access.CanAccessOrganizationAsync(other.Id));
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task Concurrent_admin_demotions_leave_one_active_administrator()
    {
        using (var cleanup = factory.Services.CreateScope())
            await cleanup.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(x => x.Role == Roles.Administrator)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, Roles.MillManager));
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (firstAdmin, firstSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (secondAdmin, secondSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var firstClient = factory.CreateRealCookieClient(firstAdmin, firstSession);
        using var secondClient = factory.CreateRealCookieClient(secondAdmin, secondSession);
        await AddCsrfAsync(firstClient); await AddCsrfAsync(secondClient);

        var demoteSecond = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{secondAdmin.Id}/access")
        { Content = JsonContent.Create(new { role = Roles.MillManager, organizationId = mill.Id, isActive = true }) };
        demoteSecond.Headers.TryAddWithoutValidation("If-Match", secondAdmin.Version.ToString("N"));
        var demoteFirst = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{firstAdmin.Id}/access")
        { Content = JsonContent.Create(new { role = Roles.MillManager, organizationId = mill.Id, isActive = true }) };
        demoteFirst.Headers.TryAddWithoutValidation("If-Match", firstAdmin.Version.ToString("N"));

        var responses = await Task.WhenAll(firstClient.SendAsync(demoteSecond), secondClient.SendAsync(demoteFirst));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        // The losing request may reach the transaction guard (409), or its actor's
        // session/role may already be revoked before authentication/authorization (401/403).
        Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.CountAsync(x => x.Role == Roles.Administrator && x.IsActive));
    }

    [Fact]
    public async Task Google_authorization_code_flow_creates_pending_application_session()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        var challenge = await client.GetAsync("/api/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var authorizationUri = challenge.Headers.Location!;
        var state = QueryHelpers.ParseQuery(authorizationUri.Query)["state"].Single()!;
        var code = $"oauth-{Guid.NewGuid():N}";

        var callback = await client.GetAsync($"/signin-google?state={Uri.EscapeDataString(state)}&code={Uri.EscapeDataString(code)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.StartsWith("/api/auth/google/complete", callback.Headers.Location!.OriginalString);
        var complete = await client.GetAsync(callback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Equal("/", complete.Headers.Location!.OriginalString);

        var session = await client.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var body = await session.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"{code}@example.com", body.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("role").ValueKind);
        Assert.Empty(body.GetProperty("permissions").EnumerateArray());
    }

    [Fact]
    public async Task Google_callback_rejects_tampered_state()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        var challenge = await client.GetAsync("/api/auth/google");
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].Single()!;
        var callback = await client.GetAsync($"/signin-google?state={Uri.EscapeDataString(state + "tampered")}&code=test");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("invalid_state", QueryHelpers.ParseQuery(callback.Headers.Location!.OriginalString.Split('?')[1])["authError"]);
        Assert.False(string.IsNullOrWhiteSpace(QueryHelpers.ParseQuery(callback.Headers.Location.OriginalString.Split('?')[1])["authTrace"]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Theory]
    [InlineData("unverified")]
    [InlineData("missing-verification")]
    public async Task Google_callback_rejects_unverified_or_missing_email_verification(string prefix)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        var challenge = await client.GetAsync("/api/auth/google");
        var state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].Single()!;
        var code = $"{prefix}-{Guid.NewGuid():N}";
        var callback = await client.GetAsync($"/signin-google?state={Uri.EscapeDataString(state)}&code={Uri.EscapeDataString(code)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var complete = await client.GetAsync(callback.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        Assert.Equal("unverified_email", QueryHelpers.ParseQuery(complete.Headers.Location!.OriginalString.Split('?')[1])["authError"]);
        Assert.False(string.IsNullOrWhiteSpace(QueryHelpers.ParseQuery(complete.Headers.Location.OriginalString.Split('?')[1])["authTrace"]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }
}
