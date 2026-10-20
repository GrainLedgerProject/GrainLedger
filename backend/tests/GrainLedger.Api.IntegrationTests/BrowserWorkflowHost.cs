using System.Text.Json;
using System.Net.Http.Json;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Exports;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BrowserWorkflowHost
{
    [Fact]
    public async Task Serve_real_cookie_sessions_for_browser_workflow()
    {
        var sessionFile = Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_SESSION_FILE");
        if (string.IsNullOrWhiteSpace(sessionFile)) return;
        var port = Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_PORT") ?? "5080";
        var apiUrl = $"http://localhost:{port}";
        var webRoot = Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_UI_DIST");
        var uiUrl = string.IsNullOrWhiteSpace(webRoot) ? "http://localhost:5173" : apiUrl;
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
        await scenario.ReceiptAsync("10");
        var pendingReceipt = await LedgerScenario.SendAsync(scenario.Purchaser, HttpMethod.Post, "/api/procurements", scenario.ReceiptBody(Guid.NewGuid(), ["12", "8"]));
        var (exportUser, exportSession) = await factory.SeedUserAsync(Roles.ExportStaff, scenario.OwnerId);
        scenario.Accounts.Add("export", (exportUser, exportSession, factory.CreateRealCookieClient(exportUser, exportSession)));
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.AddRange(new Market { Id = Guid.NewGuid(), Code = "DEMO-EU", Name = "Browser test market" },
                new Commodity { Id = Guid.NewGuid(), Code = "RICE", Name = "Rice" },
                new QualityAttribute { Id = Guid.NewGuid(), Code = "MOISTURE", Name = "Moisture", Unit = "%" });
            await catalog.SaveChangesAsync();
        }
        async Task<string> StockSnapshotAsync()
        {
            await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT jsonb_build_object('positions', (SELECT count(*) FROM ledger.positions),
                    'massKg', (SELECT coalesce(sum(quantity_kg),0) FROM ledger.positions),
                    'postings', (SELECT count(*) FROM ledger.postings))::text
                """, connection);
            return (string)(await command.ExecuteScalarAsync())!;
        }
        var stockBefore = await StockSnapshotAsync();
        var corruptNextExportResponse = 0;
        var accounts = new Dictionary<string, object>();
        foreach (var (label, account) in scenario.Accounts)
        {
            var cookie = factory.ProtectSessionCookie(account.User, account.Session);
            accounts[label] = new
            {
                userId = account.User.Id, organizationId = account.User.OrganizationId, role = account.User.Role,
                loginUrl = $"{apiUrl}/__browser/session/{label}",
                cookie = new { name = cookie.Name, value = cookie.Value, domain = "localhost", path = "/", httpOnly = true, secure = false, sameSite = "Lax" }
            };
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", WebRootPath = webRoot });
        builder.WebHost.UseUrls(apiUrl);
        await using var bridge = builder.Build();
        if (!string.IsNullOrWhiteSpace(webRoot)) { bridge.UseDefaultFiles(); bridge.UseStaticFiles(); }
        using var upstream = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false
        });
        bridge.Run(async context =>
        {
            // Provisioning belongs only to this loopback test bridge, never production API.
            if (context.Request.Path == "/__browser/corrupt-next-export-response" && HttpMethods.IsPost(context.Request.Method))
            {
                Interlocked.Exchange(ref corruptNextExportResponse, 1);
                context.Response.StatusCode = 204;
                return;
            }
            if (context.Request.Path == "/__browser/export-check" && HttpMethods.IsGet(context.Request.Method))
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
                await context.Response.WriteAsJsonAsync(new
                {
                    stockUnchanged = stockBefore == await StockSnapshotAsync(),
                    stock = JsonSerializer.Deserialize<JsonElement>(stockBefore),
                    orders = await db.Orders.AsNoTracking().Where(x => x.CompanyId == scenario.OwnerId)
                        .Select(x => new { x.Code, x.Version, x.RequiredMassKg, x.CurrentSpecificationVersion }).ToArrayAsync(),
                    containerCount = await db.Containers.CountAsync(x => x.CompanyId == scenario.OwnerId),
                    eventCount = await db.Events.CountAsync(x => x.CompanyId == scenario.OwnerId)
                });
                return;
            }
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
                context.Response.Redirect(uiUrl);
                return;
            }
            if (!string.IsNullOrWhiteSpace(webRoot) && HttpMethods.IsGet(context.Request.Method) &&
                !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/signin-google"))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
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
            if (response.IsSuccessStatusCode && context.Request.Path.StartsWithSegments("/api/export-orders") &&
                !HttpMethods.IsGet(context.Request.Method) && Interlocked.Exchange(ref corruptNextExportResponse, 0) == 1)
            {
                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync("{\"id\":");
                return;
            }
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var file = new FileStream(sessionFile, options))
            await JsonSerializer.SerializeAsync(file, new
            {
                api = apiUrl, ui = uiUrl, isolatedTestData = true,
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
