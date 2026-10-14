using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ProcurementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(Guid CompanyId, Guid FarmerId, Guid PlotSeasonId, Guid CommodityId)> SeedAsync()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var farmerId = Guid.NewGuid(); var plotId = Guid.NewGuid();
        var seasonId = Guid.NewGuid(); var plotSeasonId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
            db.Farmers.Add(new Farmer { Id = farmerId, CompanyId = company.Id,
                Code = Guid.NewGuid().ToString("N"), Name = "Farmer", CreatedAt = DateTimeOffset.UtcNow });
            db.Plots.Add(new Plot { Id = plotId, CompanyId = company.Id, FarmerId = farmerId,
                Code = Guid.NewGuid().ToString("N"), AreaHa = 1m,
                BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}" });
            db.CropSeasons.Add(new CropSeason { Id = seasonId, CompanyId = company.Id,
                Name = "Test season", StartsOn = new DateOnly(2026, 1, 1), EndsOn = new DateOnly(2026, 12, 31) });
            await db.SaveChangesAsync();
            db.PlotSeasons.Add(new PlotSeason { Id = plotSeasonId, CompanyId = company.Id,
                PlotId = plotId, SeasonId = seasonId, RiceVariety = "ST25" });
            await db.SaveChangesAsync();
        }
        Guid commodityId;
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var commodity = await catalog.Commodities.SingleOrDefaultAsync(x => x.Code == "PADDY");
            if (commodity is null)
            {
                commodity = new Commodity { Id = Guid.NewGuid(), Code = "PADDY", Name = "Paddy" };
                catalog.Commodities.Add(commodity);
            }
            if (!await catalog.MeasurementUnits.AnyAsync(x => x.Code == "KG"))
                catalog.MeasurementUnits.Add(new MeasurementUnit { Id = Guid.NewGuid(), Code = "KG",
                    Name = "Kilogram", QuantityKind = "MASS" });
            await catalog.SaveChangesAsync(); commodityId = commodity.Id;
        }
        return (company.Id, farmerId, plotSeasonId, commodityId);
    }

    private async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> app, Guid companyId)
    {
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, companyId);
        var client = app == factory ? factory.CreateRealCookieClient(user, session) :
            CreateCookieClient(app, user, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return client;
    }

    private static HttpClient CreateCookieClient(WebApplicationFactory<Program> app, User user, LoginSession session)
    {
        using var scope = app.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("GrainLedger.Application");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("session_id", session.Id.ToString())], "GrainLedger.Application");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = session.ExpiresAt }, "GrainLedger.Application");
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-GrainLedger.Session={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    private static object Request(Guid farmerId, Guid plotSeasonId, Guid commodityId,
        Guid siteId, Guid locationId, Guid? procurementId = null, Guid? operationId = null,
        string mass = "600", string unit = "KG", bool twoLines = false)
    {
        var first = new { itemId = Guid.NewGuid(), lineNo = 1, plotSeasonId,
            measuredValue = mass, measuredUnit = unit, moisturePercent = "22.5",
            lotId = Guid.NewGuid(), sourceLotId = Guid.NewGuid() };
        var second = new { itemId = Guid.NewGuid(), lineNo = 2, plotSeasonId,
            measuredValue = "400", measuredUnit = "KG", moisturePercent = "21",
            lotId = Guid.NewGuid(), sourceLotId = Guid.NewGuid() };
        return new { deviceId = "web-p3-test", clientOperationId = operationId ?? Guid.NewGuid(),
        capturedAt = new DateTimeOffset(2026, 10, 14, 3, 5, 0, TimeSpan.Zero), payloadSchemaVersion = 1,
        payload = new { entityId = procurementId ?? Guid.NewGuid(), farmerId,
            collectionSiteId = siteId, receivingLocationId = locationId, commodityId,
            receivedAt = new DateTimeOffset(2026, 10, 14, 3, 0, 0, TimeSpan.Zero),
            items = twoLines ? new[] { first, second } : new[] { first } } };
    }

    [Fact]
    public async Task Unconfigured_ledger_returns_503_without_capture_writes()
    {
        var source = await SeedAsync();
        using var client = await ClientAsync(factory, source.CompanyId);
        var request = Request(source.FarmerId, source.PlotSeasonId, source.CommodityId,
            Guid.NewGuid(), Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/api/procurements/commit", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.False(await db.Procurements.AnyAsync(x => x.CompanyId == source.CompanyId));
        Assert.False(await db.OfflineOperations.AnyAsync(x => x.DeviceId == "web-p3-test" && x.CompanyId == source.CompanyId));
    }

    [Fact]
    public async Task Bad_precision_is_rejected_before_writing()
    {
        var source = await SeedAsync();
        using var client = await ClientAsync(factory, source.CompanyId);
        var response = await client.PostAsJsonAsync("/api/procurements/commit",
            Request(source.FarmerId, source.PlotSeasonId, source.CommodityId,
                Guid.NewGuid(), Guid.NewGuid(), mass: "0.0001", unit: "G"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Fake_ledger_contract_commits_once_and_can_read_receipt()
    {
        var source = await SeedAsync();
        var port = new FakeLedgerPort();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProcurementLedgerPort>();
            services.AddSingleton<IProcurementLedgerPort>(port);
        }));
        using var client = await ClientAsync(app, source.CompanyId);
        var receiptId = Guid.NewGuid(); var operationId = Guid.NewGuid();
        var request = Request(source.FarmerId, source.PlotSeasonId, source.CommodityId,
            port.SiteId, port.LocationId, receiptId, operationId, twoLines: true);
        var created = await client.PostAsJsonAsync("/api/procurements/commit", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<ProcurementResponse>())!;
        Assert.Equal("1000", result.TotalNetMassKg);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, port.PostCalls);
        var replay = await client.PostAsJsonAsync("/api/procurements/commit", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(1, port.PostCalls);
        var detail = await client.GetFromJsonAsync<ProcurementResponse>($"/api/procurements/{receiptId}");
        Assert.Equal(result.EventId, detail!.EventId);
        var list = await client.GetFromJsonAsync<ProcurementListResponse>("/api/procurements");
        Assert.Contains(list!.Items, x => x.Id == receiptId && x.TotalNetMassKg == "1000");
        var lot = await client.GetFromJsonAsync<SourceLotResponse>($"/api/source-lots/{result.Items[0].SourceLotId}");
        Assert.Equal(result.Items[0].LotId, lot!.LotId);
        var otherCompany = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        using var foreignClient = await ClientAsync(app, otherCompany.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignClient.GetAsync($"/api/procurements/{receiptId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await foreignClient.GetAsync($"/api/source-lots/{result.Items[0].SourceLotId}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.Procurements.CountAsync(x => x.Id == receiptId));
        Assert.Equal(2, await db.ProcurementItems.CountAsync(x => x.ProcurementId == receiptId));
        Assert.Equal(2, await db.SourceLots.CountAsync(x => x.CompanyId == source.CompanyId &&
            result.Items.Select(y => y.SourceLotId).Contains(x.Id)));
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.ClientOperationId == operationId));
    }

    [Fact]
    public async Task Ledger_failure_rolls_back_receipt_event_and_operation()
    {
        var source = await SeedAsync();
        var port = new FakeLedgerPort { FailPost = true };
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProcurementLedgerPort>();
            services.AddSingleton<IProcurementLedgerPort>(port);
        }));
        using var client = await ClientAsync(app, source.CompanyId);
        var receiptId = Guid.NewGuid(); var operationId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/procurements/commit",
            Request(source.FarmerId, source.PlotSeasonId, source.CommodityId,
                port.SiteId, port.LocationId, receiptId, operationId));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.False(await db.Procurements.AnyAsync(x => x.Id == receiptId));
        Assert.False(await db.ProcurementItems.AnyAsync(x => x.ProcurementId == receiptId));
        Assert.False(await db.BusinessEvents.AnyAsync(x => x.CorrelationId == operationId));
        Assert.False(await db.OfflineOperations.AnyAsync(x => x.ClientOperationId == operationId));
    }

    private sealed class FakeLedgerPort : IProcurementLedgerPort
    {
        public Guid SiteId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public bool FailPost { get; set; }
        public int PostCalls { get; private set; }
        public Task<IReadOnlyList<ProcurementReceivingOption>> ListReceivingOptionsAsync(Guid companyId,
            Guid? collectionSiteId, int offset, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcurementReceivingOption>>(
            [new(SiteId, "SITE", "Test site", LocationId, "RECV")]);
        public Task<bool> IsValidReceivingOptionAsync(Guid companyId, Guid collectionSiteId,
            Guid receivingLocationId, CancellationToken ct) => Task.FromResult(collectionSiteId == SiteId && receivingLocationId == LocationId);
        public async Task PostReceiptAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid companyId, Guid actorId, Guid eventId, Guid procurementId, Guid commodityId,
            Guid collectionSiteId, Guid receivingLocationId, IReadOnlyList<ProcurementStockLine> lines,
            CancellationToken ct)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM capture.source_lots WHERE company_id = @company", connection, transaction);
            command.Parameters.AddWithValue("company", companyId);
            Assert.True((long)(await command.ExecuteScalarAsync(ct))! > 0);
            Assert.NotEmpty(lines);
            if (FailPost) throw new ProcurementLedgerUnavailableException();
            PostCalls++;
        }
        public Task<IReadOnlyList<ProcurementStockPosition>> GetCurrentStockAsync(Guid companyId, Guid lotId,
            CancellationToken ct) => Task.FromResult<IReadOnlyList<ProcurementStockPosition>>([new(LocationId, 600m)]);
    }
}
