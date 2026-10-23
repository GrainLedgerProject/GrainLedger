using System.Text.Json;
using System.Net.Http.Json;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class BrowserWorkflowHost
{
    [Fact]
    public async Task Serve_real_cookie_sessions_for_browser_workflow()
    {
        var sessionFile = Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_SESSION_FILE");
        if (string.IsNullOrWhiteSpace(sessionFile)) return;
        var apiOrigin = new Uri(Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_API_URL") ?? "http://localhost:5080");
        var uiOrigin = new Uri(Environment.GetEnvironmentVariable("GRAINLEDGER_BROWSER_UI_URL") ?? "http://localhost:5173");
        if (new[] { apiOrigin, uiOrigin }.Any(uri => !uri.IsLoopback || uri.Scheme != "http" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != ""))
            throw new InvalidOperationException("Browser test origins must be loopback HTTP origins.");
        var apiUrl = apiOrigin.GetLeftPart(UriPartial.Authority);
        var uiUrl = uiOrigin.GetLeftPart(UriPartial.Authority);
        // Long-running test output can fill runner pipes; console overflow must not block HTTP requests.
        var factory = new ApiFactory
        {
            BrowserCookies = true,
            ConfigureTestServices = services => services.PostConfigure<ConsoleLoggerOptions>(
                options => options.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite)
        };
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
        var residue = await ResidueScenario.CreateAsync(scenario);
        var first = await LabReportTests.SampleAsync(scenario, await scenario.ReceiptAsync("60"));
        var second = await LabReportTests.SampleAsync(scenario, await scenario.ReceiptAsync("40"));
        var firstReport = await residue.ReportAsync(first);
        var lateReport = await residue.ReportAsync(second, [residue.Result("0.55", "±0.25")], confirm: false);
        await residue.PublishRuleAsync();
        await residue.SelectAsync(first, firstReport.Report);
        foreach (var (kind, input, output, factor) in new[]
        {
            ("DRYING", "RAW_PADDY", "DRIED_PADDY", "1"),
            ("MILLING", "DRIED_PADDY", "HEAD_RICE", "0.5"),
            ("BLENDING", "HEAD_RICE", "BLENDED_RICE", "1"),
            ("CUSTODY_INGRESS", "BLENDED_RICE", "BLENDED_RICE", "1"),
            ("CUSTODY_WITHDRAW", "BLENDED_RICE", "BLENDED_RICE", "1")
        }) await residue.ProcessRuleAsync(kind, input, output, factor, factor);
        await residue.BlendCompatibilityAsync("HEAD_RICE");
        var driedFirst = LedgerScenario.Output(await scenario.CommitAsync(await scenario.DraftAsync(
            "DRYING", [(first.LotId, "60")], [("DRIED_PADDY", "60")])));
        var driedSecond = LedgerScenario.Output(await scenario.CommitAsync(await scenario.DraftAsync(
            "DRYING", [(second.LotId, "40")], [("DRIED_PADDY", "40")])));
        var headFirst = LedgerScenario.Output(await scenario.CommitAsync(await scenario.DraftAsync(
            "MILLING", [(driedFirst, "60")], [("HEAD_RICE", "60")])));
        var headSecond = LedgerScenario.Output(await scenario.CommitAsync(await scenario.DraftAsync(
            "MILLING", [(driedSecond, "40")], [("HEAD_RICE", "40")])));
        var blended = LedgerScenario.Output(await scenario.CommitAsync(await scenario.DraftAsync(
            "BLENDING", [(headFirst, "60"), (headSecond, "40")], [("BLENDED_RICE", "100")])));
        var mixedLocation = await residue.MixedLocationAsync();
        await scenario.TransferAsync(blended, "100", to: mixedLocation);
        var (pool, revision) = await residue.PoolAsync(mixedLocation);
        var aliquot = await residue.WithdrawAsync(mixedLocation, pool, revision, "25");
        var (remainder, _) = await residue.PoolAsync(mixedLocation);
        var initialEvaluation = await residue.EvaluateAsync(aliquot);
        var companyPolicy = await ResidueScenario.CreateAsync(scenario);
        var policySample = await LabReportTests.SampleAsync(scenario, await scenario.ReceiptAsync("10"));
        var policyReport = await companyPolicy.ReportAsync(policySample, [companyPolicy.Result(uncertainty: null)]);
        var relativeRule = companyPolicy.RuleBody("NUMERIC_RELATIVE_UNCERTAINTY");
        relativeRule["relativeUncertaintyPercent"] = "10";
        relativeRule["relativeUncertaintyAtZero"] = false;
        var relativeRuleId = await companyPolicy.PublishRuleAsync(relativeRule, adopt: false);
        await companyPolicy.SelectAsync(policySample, policyReport.Report, uncertainty: null);
        var beforeAdoption = await companyPolicy.EvaluateAsync(policySample.LotId);
        var reviewSamples = new[]
        {
            await LabReportTests.SampleAsync(scenario, await scenario.ReceiptAsync("10")),
            await LabReportTests.SampleAsync(scenario, await scenario.ReceiptAsync("10"))
        };
        var reviewRequest = LabReportTests.ReportRequest(reviewSamples.Select(sample =>
            LabReportTests.ResultSet(sample, [companyPolicy.Result(uncertainty: null)])).ToArray());
        var reviewReportId = LabReportTests.EntityId(reviewRequest);
        await LedgerScenario.SendAsync(scenario.Customer, HttpMethod.Post, "/api/lab-reports", reviewRequest);
        var reviewReport = await LedgerScenario.GetAsync(scenario.Customer, $"/api/lab-reports/{reviewReportId}");
        await LedgerScenario.SendAsync(scenario.Customer, HttpMethod.Post, $"/api/lab-reports/{reviewReportId}/confirm",
            LabReportTests.ConfirmRequest(reviewReport, reviewSamples));
        var normalizedSample = await LabReportTests.SampleAsync(scenario, await scenario.ReceiptAsync("10"));
        var normalizedReport = await companyPolicy.ReportAsync(normalizedSample,
            [companyPolicy.Result("150", uncertainty: null, unit: "µg/kg", lod: "10", loq: "20"),
             companyPolicy.Result("0.2", uncertainty: null, substanceId: companyPolicy.OtherSubstanceId)], confirm: false);
        var latePublished = false;
        using var lateGate = new SemaphoreSlim(1, 1);
        var accounts = new Dictionary<string, object>();
        foreach (var (label, account) in scenario.Accounts)
        {
            var cookie = factory.ProtectSessionCookie(account.User, account.Session);
            accounts[label] = new
            {
                userId = account.User.Id, organizationId = account.User.OrganizationId, role = account.User.Role,
                loginUrl = $"{apiUrl}/__browser/session/{label}",
                cookie = new { name = cookie.Name, value = cookie.Value, domain = apiOrigin.Host, path = "/", httpOnly = true, secure = false, sameSite = "Lax" }
            };
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls(apiUrl);
        builder.Services.PostConfigure<ConsoleLoggerOptions>(
            options => options.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite);
        await using var bridge = builder.Build();
        using var upstream = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false
        });
        bridge.Run(async context =>
        {
            // Provisioning belongs only to this loopback test bridge, never production API.
            if (context.Request.Path == "/__browser/residue-bounds")
            {
                if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync($"""
                    <!doctype html><html lang="en"><meta name="viewport" content="width=device-width,initial-scale=1">
                    <title>Synthetic residue bounds browser scenario</title><main><h1>SYNTHETIC RESIDUE BOUNDS — isolated database only</h1>
                    <p>Not operational scientific approval or compliance. Source 60 kg [0.1,0.2] + source 40 kg [0.3,0.8],
                    drying factor 1, milling factor 0.5, blend/pool factors 1. Expected fresh interval [0.09,0.22] mg/kg.</p>
                    <p>Second report starts pending. Initial snapshot stays unresolved after late evidence.</p>
                    <ul><li><a href="{uiUrl}/ledger/lots/{aliquot}">Aliquot {aliquot}</a></li>
                    <li><a href="{uiUrl}/ledger/lots/{remainder}">Remainder {remainder}</a></li>
                    <li><a href="{uiUrl}/ledger/lots/{blended}">Exhausted blend {blended}</a></li>
                    <li><a href="{uiUrl}/ledger/lots/{first.LotId}">Exhausted source {first.LotId}</a></li>
                    <li>Initial evaluation: {initialEvaluation.GetProperty("id").GetGuid()}</li></ul>
                    <form method="post" action="/__browser/residue-bounds/late-evidence"><button>Confirm late evidence through real API</button></form>
                    <p><a href="/__browser/session/customer">Open customer workspace</a> ·
                    <a href="/__browser/session/operator">Open assigned operator</a> ·
                    <a href="/__browser/session/foreignCustomer">Open foreign customer (denied lot)</a></p></main></html>
                    """);
                return;
            }
            if (context.Request.Path == "/__browser/residue-bounds/late-evidence")
            {
                if (!HttpMethods.IsPost(context.Request.Method)) { context.Response.StatusCode = 405; return; }
                await lateGate.WaitAsync(context.RequestAborted);
                try
                {
                    if (!latePublished)
                    {
                        await LedgerScenario.SendAsync(scenario.Customer, HttpMethod.Post,
                            $"/api/lab-reports/{lateReport.ReportId}/confirm", LabReportTests.ConfirmRequest(lateReport.Report, second));
                        await residue.SelectAsync(second, lateReport.Report, "0.25");
                        latePublished = true;
                    }
                }
                finally { lateGate.Release(); }
                context.Response.Redirect($"{uiUrl}/ledger/lots/{aliquot}");
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
                api = apiUrl, ui = uiUrl, isolatedTestData = true,
                customerOrganizationId = scenario.OwnerId, foreignCustomerOrganizationId = scenario.ForeignOwnerId,
                facilityId = scenario.FacilityId, foreignFacilityId = scenario.ForeignFacilityId,
                locationId = scenario.LocationId, secondLocationId = scenario.SecondLocationId,
                isolationLocationId = scenario.IsolationLocationId, foreignLocationId = scenario.ForeignLocationId,
                equipmentId = scenario.EquipmentId, commodityId = scenario.CommodityId, catalogCommodityId = scenario.CatalogCommodityId,
                farmerId = scenario.FarmerId, seasonId = scenario.SeasonId, plotSeasonId = scenario.PlotSeasonId,
                residueBounds = new
                {
                    synthetic = true, substanceId = residue.SubstanceId, uncoveredSubstanceId = residue.OtherSubstanceId,
                    sourceLotIds = new[] { first.LotId, second.LotId }, sourceIds = new[] { first.SourceLotId, second.SourceLotId },
                    driedLotIds = new[] { driedFirst, driedSecond }, headLotIds = new[] { headFirst, headSecond },
                    blendedLotId = blended, poolLotId = pool, aliquotLotId = aliquot, remainderLotId = remainder,
                    mixedLocationId = mixedLocation, lateReportId = lateReport.ReportId,
                    initialEvaluationId = initialEvaluation.GetProperty("id").GetGuid(),
                    aliquotUrl = $"{uiUrl}/ledger/lots/{aliquot}",
                    remainderUrl = $"{uiUrl}/ledger/lots/{remainder}",
                    exhaustedSourceUrl = $"{uiUrl}/ledger/lots/{first.LotId}",
                    actionsUrl = $"{apiUrl}/__browser/residue-bounds",
                    lateEvidenceAction = $"POST {apiUrl}/__browser/residue-bounds/late-evidence",
                    expectedFreshLower = "0.09", expectedFreshUpper = "0.22", unit = "mg/kg", basis = ResidueScenario.Basis
                },
                companyPolicy = new
                {
                    synthetic = true, substanceId = companyPolicy.SubstanceId,
                    lotId = policySample.LotId, sourceId = policySample.SourceLotId,
                    reportId = policyReport.ReportId, ruleId = relativeRuleId,
                    ruleCode = relativeRule["code"]!.GetValue<string>(),
                    initialEvaluationId = beforeAdoption.GetProperty("id").GetGuid(),
                    lotUrl = $"{uiUrl}/ledger/lots/{policySample.LotId}",
                    expectedDefaultLower = "0.135", expectedDefaultUpper = "0.165",
                    expectedOverrideLower = "0.12", expectedOverrideUpper = "0.18",
                    reviewReportId,
                    reviewLotIds = reviewSamples.Select(sample => sample.LotId).ToArray(),
                    reviewSampleIds = reviewSamples.Select(sample => sample.Id).ToArray(),
                    normalizedReportId = normalizedReport.ReportId,
                    normalizedLotId = normalizedSample.LotId,
                    normalizedSampleId = normalizedSample.Id,
                    uncoveredSubstanceId = companyPolicy.OtherSubstanceId
                },
                evidenceId = scenario.EvidenceId, pendingProcurementId = pendingReceipt.GetProperty("id").GetGuid(), accounts
            }, new JsonSerializerOptions { WriteIndented = true });
        try { await bridge.StartAsync(); await bridge.WaitForShutdownAsync(); }
        finally { File.Delete(sessionFile); await factory.DisposeAsync(); }
    }
}
