using System.Text.Json;
using System.Net.Http.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BrowserWorkflowHost
{
    [Fact]
    public async Task Serve_real_cookie_sessions_for_browser_workflow()
    {
        var sessionFile = Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_SESSION_FILE");
        if (string.IsNullOrWhiteSpace(sessionFile)) return;
        var factory = new ApiFactory { BrowserCookies = true };
        await factory.InitializeAsync();
        // Synthetic password account only in the disposable browser-test database.
        using (var client = factory.CreateClient())
        {
            const string email = "browser-password@example.com";
            var registration = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "BrowserPass!123", displayName = "Browser Password User" });
            registration.EnsureSuccessStatusCode();
            var pending = (await registration.Content.ReadFromJsonAsync<RegistrationPendingDto>())!;
            var verification = await client.PostAsJsonAsync("/api/auth/verify-email", new { email, registrationId = pending.RegistrationId, otp = factory.Email.VerificationCode(email) });
            verification.EnsureSuccessStatusCode();
        }
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var (exportUser, exportSession) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        var exportClient = factory.CreateRealCookieClient(exportUser, exportSession);
        await LedgerScenario.CsrfAsync(exportClient);
        scenario.Accounts.Add("export", (exportUser, exportSession, exportClient));
        await scenario.ReceiptAsync("10");
        var pendingReceipt = await LedgerScenario.SendAsync(scenario.Purchaser, HttpMethod.Post, "/api/procurements", scenario.ReceiptBody(Guid.NewGuid(), ["12", "8"]));
        var accounts = new Dictionary<string, object>();
        foreach (var (label, account) in scenario.Accounts)
        {
            var cookie = factory.ProtectSessionCookie(account.User, account.Session);
            accounts[label] = new
            {
                userId = account.User.Id, organizationId = account.User.OrganizationId, role = account.User.Role,
                loginUrl = $"http://localhost:5080/__browser/session/{label}",
                cookie = new { name = cookie.Name, value = cookie.Value, domain = "localhost", path = "/", httpOnly = true, secure = false, sameSite = "Lax" }
            };
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://localhost:5080");
        await using var bridge = builder.Build();
        using var upstream = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false
        });
        bridge.Run(async context =>
        {
            // Provisioning belongs only to this loopback test bridge, never production API.
            if (context.Request.Path.StartsWithSegments("/__browser/session", out var remaining))
            {
                if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
                var label = remaining.Value?.Trim('/');
                context.Response.Cookies.Delete("GrainLedger.Session", new CookieOptions { Path = "/" });
                context.Response.Cookies.Delete("GrainLedger.Csrf", new CookieOptions { Path = "/" });
                if (label != "anonymous")
                {
                    if (label is null || !scenario.Accounts.TryGetValue(label, out var account)) { context.Response.StatusCode = 404; return; }
                    var session = await factory.SeedSessionAsync(account.User);
                    var cookie = factory.ProtectSessionCookie(account.User, session);
                    context.Response.Cookies.Append(cookie.Name, cookie.Value, new CookieOptions
                    {
                        HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = false, Path = "/",
                        Expires = session.ExpiresAt
                    });
                }
                context.Response.Redirect("http://localhost:5173");
                return;
            }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                request.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            using var response = await upstream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var file = new FileStream(sessionFile, options))
            await JsonSerializer.SerializeAsync(file, new
            {
                api = "http://localhost:5080", ui = "http://localhost:5173", isolatedTestData = true,
                customerOrganizationId = scenario.OwnerId, foreignCustomerOrganizationId = scenario.ForeignOwnerId,
                facilityId = scenario.FacilityId, foreignFacilityId = scenario.ForeignFacilityId,
                locationId = scenario.LocationId, secondLocationId = scenario.SecondLocationId,
                isolationLocationId = scenario.IsolationLocationId, foreignLocationId = scenario.ForeignLocationId,
                equipmentId = scenario.EquipmentId, commodityId = scenario.CommodityId, catalogCommodityId = scenario.CatalogCommodityId,
                farmerId = scenario.FarmerId, seasonId = scenario.SeasonId, plotSeasonId = scenario.PlotSeasonId,
                evidenceId = scenario.EvidenceId, pendingProcurementId = pendingReceipt.GetProperty("id").GetGuid(), accounts
            }, new JsonSerializerOptions { WriteIndented = true });
        try { await bridge.StartAsync(); await bridge.WaitForShutdownAsync(); }
        finally { File.Delete(sessionFile); await factory.DisposeAsync(); }
    }
}
