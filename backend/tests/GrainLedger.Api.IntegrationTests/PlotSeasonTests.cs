using GrainLedger.Api.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using GrainLedger.Api.Features.Capture;
using GrainLedger.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class PlotSeasonTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task AddCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private async Task<Plot> SeedPlotAsync(Guid companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var farmer = new Farmer { Id = Guid.NewGuid(), CompanyId = companyId, Code = Guid.NewGuid().ToString("N"),
            Name = "Test Farmer", CreatedAt = DateTimeOffset.UtcNow };
        var plot = new Plot { Id = Guid.NewGuid(), CompanyId = companyId, FarmerId = farmer.Id,
            Code = Guid.NewGuid().ToString("N"), BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1.25m };
        db.Farmers.Add(farmer);
        db.Plots.Add(plot);
        await db.SaveChangesAsync();
        return plot;
    }

    [Fact]
    public async Task Admin_creates_season_customer_registers_plot_season_and_retries()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var other = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var plot = await SeedPlotAsync(company.Id);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        var (outsider, outsiderSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, other.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var customerClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        using var outsiderClient = factory.CreateRealCookieClient(outsider, outsiderSession);
        await AddCsrfAsync(adminClient);
        await AddCsrfAsync(customerClient);
        var seasonCreated = await adminClient.PostAsJsonAsync("/api/crop-seasons",
            new { companyId = company.Id, name = "Dong Xuan", startsOn = "2026-11-01", endsOn = "2027-03-31" });
        Assert.Equal(HttpStatusCode.Created, seasonCreated.StatusCode);
        var season = (await seasonCreated.Content.ReadFromJsonAsync<CropSeasonResponse>())!;
        Assert.Equal(company.Id, season.CompanyId);
        var adminSeasons = await adminClient.GetFromJsonAsync<CropSeasonListResponse>($"/api/crop-seasons?companyId={company.Id}");
        Assert.Contains(adminSeasons!.Items, x => x.Id == season.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.GetAsync("/api/crop-seasons")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.GetAsync("/api/crop-seasons?companyId=")).StatusCode);
        var seasons = await customerClient.GetFromJsonAsync<CropSeasonListResponse>("/api/crop-seasons");
        Assert.Contains(seasons!.Items, x => x.Id == season.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await customerClient.GetAsync("/api/crop-seasons?companyId=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customerClient.GetAsync("/api/crop-seasons?companyId=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await customerClient.GetAsync($"/api/crop-seasons?companyId={other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await customerClient.PostAsJsonAsync("/api/crop-seasons",
                new { companyId = company.Id, name = "Unauthorized", startsOn = "2026-11-01", endsOn = "2027-03-31" })).StatusCode);
        var foreignSeasons = await outsiderClient.GetFromJsonAsync<CropSeasonListResponse>("/api/crop-seasons");
        Assert.DoesNotContain(foreignSeasons!.Items, x => x.Id == season.Id);
        var varietyResponse = await adminClient.PostAsJsonAsync("/api/rice-varieties", new { code = $"TEST-{Guid.NewGuid():N}", name = "ST25" });
        Assert.Equal(HttpStatusCode.Created, varietyResponse.StatusCode);
        var varietyId = (await varietyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        object InvalidRequest(object payload) => new { deviceId = "test-browser", clientOperationId = Guid.NewGuid(),
            capturedAt = DateTimeOffset.UtcNow, payloadSchemaVersion = 1, payload };
        Assert.Equal(HttpStatusCode.BadRequest, (await customerClient.PostAsJsonAsync("/api/plot-seasons",
            InvalidRequest(new { entityId = Guid.NewGuid(), plotId = plot.Id, seasonId = season.Id }))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customerClient.PostAsJsonAsync("/api/plot-seasons",
            InvalidRequest(new { entityId = Guid.NewGuid(), plotId = plot.Id, seasonId = season.Id, riceVarietyId = Guid.NewGuid() }))).StatusCode);
        var legacyResponse = await customerClient.PostAsJsonAsync("/api/plot-seasons",
            InvalidRequest(new { entityId = Guid.NewGuid(), plotId = plot.Id, seasonId = season.Id, riceVariety = "ST25" }));
        Assert.Equal(HttpStatusCode.Conflict, legacyResponse.StatusCode);
        Assert.Equal("LEGACY_VARIETY_RESELECT_REQUIRED",
            (await legacyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var legacyId = Guid.NewGuid();
        var legacyOperationId = Guid.NewGuid();
        var legacyTime = DateTimeOffset.UtcNow;
        var legacyCanonical = JsonSerializer.Serialize(new { operationType = "PLOT_SEASON_REGISTERED", PayloadSchemaVersion = 1,
            capturedAt = legacyTime, EntityId = legacyId, PlotId = plot.Id, SeasonId = season.Id, riceVariety = "ST25" });
        using (var legacyScope = factory.Services.CreateScope())
        {
            var legacyDb = legacyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var legacyEvent = new CaptureBusinessEvent { CompanyId = company.Id, ActorId = purchaser.Id,
                EventType = "PLOT_SEASON_REGISTERED", OccurredAt = legacyTime, RecordedAt = legacyTime,
                CorrelationId = legacyOperationId, Payload = "{}" };
            legacyDb.BusinessEvents.Add(legacyEvent);
            await legacyDb.SaveChangesAsync();
            legacyDb.OfflineOperations.Add(new CaptureOperation { CompanyId = company.Id, SubmittedBy = purchaser.Id,
                DeviceId = "legacy-browser", ClientOperationId = legacyOperationId,
                PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyCanonical))),
                Payload = legacyCanonical, CapturedAt = legacyTime, ReceivedAt = legacyTime, Status = "APPLIED",
                AppliedEventId = legacyEvent.Id, ResultPayload = JsonSerializer.Serialize(new { Id = legacyId, PlotId = plot.Id,
                    SeasonId = season.Id, RiceVariety = "ST25" }) });
            await legacyDb.SaveChangesAsync();
        }
        var legacyRequest = new { deviceId = "legacy-browser", clientOperationId = legacyOperationId,
            capturedAt = legacyTime, payloadSchemaVersion = 1,
            payload = new { entityId = legacyId, plotId = plot.Id, seasonId = season.Id, riceVariety = "ST25" } };
        var replay = await customerClient.PostAsJsonAsync("/api/plot-seasons", legacyRequest);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayResult = await replay.Content.ReadFromJsonAsync<PlotSeasonResponse>();
        Assert.Equal(legacyId, replayResult!.Id);
        Assert.Null(replayResult.RiceVarietyId);


        var id = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;
        var request = new { deviceId = "test-browser", clientOperationId = operationId,
            capturedAt, payloadSchemaVersion = 1,
            payload = new { entityId = id, plotId = plot.Id, seasonId = season.Id, riceVarietyId = varietyId } };
        var created = await customerClient.PostAsJsonAsync("/api/plot-seasons", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/plot-seasons/{id}", created.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await customerClient.PostAsJsonAsync("/api/plot-seasons", request)).StatusCode);
        var detail = await customerClient.GetFromJsonAsync<PlotSeasonReferenceResponse>($"/api/plot-seasons/{id}");
        Assert.Equal("ST25", detail!.RiceVariety);
        Assert.Equal(varietyId, detail.RiceVarietyId);
        Assert.False(string.IsNullOrWhiteSpace(detail.PlotCode));
        Assert.Equal(season.Name, detail.SeasonName);
        var list = await customerClient.GetFromJsonAsync<PlotSeasonListResponse>($"/api/plot-seasons?plotId={plot.Id}");
        Assert.Contains(list!.Items, x => x.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/api/plot-seasons/{id}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PlotSeasons.CountAsync(x => x.Id == id));
        Assert.Equal(purchaser.Id, (await db.PlotSeasons.SingleAsync(x => x.Id == id)).CreatedBy);
        Assert.Equal(admin.Id, (await db.CropSeasons.SingleAsync(x => x.Id == season.Id)).CreatedBy);
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.ClientOperationId == operationId));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == operationId));
    }

    [Fact]
    public async Task Cross_company_parent_and_duplicate_pair_are_rejected()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var other = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var plot = await SeedPlotAsync(company.Id);
        var foreignPlot = await SeedPlotAsync(other.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CropSeasons.Add(new CropSeason { Id = Guid.NewGuid(), CompanyId = company.Id,
                Name = "Season A", StartsOn = new DateOnly(2026, 11, 1), EndsOn = new DateOnly(2027, 3, 31) });
            await db.SaveChangesAsync();
        }
        Guid seasonId;
        using (var scope = factory.Services.CreateScope())
            seasonId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().CropSeasons
                .Where(x => x.CompanyId == company.Id).Select(x => x.Id).FirstAsync();
        var (user, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await AddCsrfAsync(client);
        Guid varietyId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            varietyId = Guid.NewGuid();
            db.RiceVarieties.Add(new GrainLedger.Api.Features.Catalogs.RiceVariety { Id = varietyId, Code = $"TEST-{varietyId:N}", Name = "ST25" });
            await db.SaveChangesAsync();
        }
        object Request(Guid plotId) => new { deviceId = "test-browser", clientOperationId = Guid.NewGuid(),
            capturedAt = DateTimeOffset.UtcNow, payloadSchemaVersion = 1,
            payload = new { entityId = Guid.NewGuid(), plotId, seasonId, riceVarietyId = varietyId } };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/plot-seasons", Request(foreignPlot.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/plot-seasons", Request(plot.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/plot-seasons", Request(plot.Id))).StatusCode);
    }
}
