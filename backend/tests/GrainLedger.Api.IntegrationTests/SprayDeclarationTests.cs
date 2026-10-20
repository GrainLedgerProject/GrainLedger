using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class SprayDeclarationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(Guid CompanyId, Guid PlotSeasonId, Guid SubstanceId)> SeedSourceAsync()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var farmerId = Guid.NewGuid();
        var plotId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var plotSeasonId = Guid.NewGuid();
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
        var substanceId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.ActiveSubstances.Add(new ActiveSubstance { Id = substanceId,
                Code = $"T{substanceId:N}", Name = "Test substance" });
            await catalog.SaveChangesAsync();
        }
        return (company.Id, plotSeasonId, substanceId);
    }

    private async Task<HttpClient> CustomerClientAsync(Guid companyId, string role = Roles.PurchasingStaff)
    {
        var (user, session) = await factory.SeedUserAsync(role, companyId);
        var client = factory.CreateRealCookieClient(user, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return client;
    }

    private static object Request(Guid plotSeasonId, Guid substanceId, Guid? entityId = null,
        Guid? operationId = null, object[]? items = null, string? informationSource = "Farmer notebook",
        DateOnly? appliedOn = null) => new
    {
        deviceId = "spray-test", clientOperationId = operationId ?? Guid.NewGuid(),
        capturedAt = new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero), payloadSchemaVersion = 2,
        payload = new { entityId = entityId ?? Guid.NewGuid(), plotSeasonId,
            appliedOn = appliedOn ?? new DateOnly(2026, 10, 12),
            informationSource, evidenceRef = (string?)null,
            items = items ?? [new { productName = (string?)null, substanceId = (Guid?)substanceId }] }
    };

    [Fact]
    public async Task Creates_multiple_items_reads_history_and_replays_once()
    {
        var source = await SeedSourceAsync();
        using var client = await CustomerClientAsync(source.CompanyId);
        var operationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        object[] items = [new { productName = "Test product", substanceId = (Guid?)null },
            new { productName = (string?)null, substanceId = (Guid?)source.SubstanceId }];
        var request = Request(source.PlotSeasonId, source.SubstanceId, entityId, operationId, items);
        var created = await client.PostAsJsonAsync("/api/spray-declarations", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<SprayDeclarationResponse>())!;
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new DateOnly(2026, 10, 12), result.AppliedOn);
        var body = await created.Content.ReadAsStringAsync();
        Assert.Contains("\"appliedOn\":\"2026-10-12\"", body);
        Assert.DoesNotContain("appliedAt", body);
        Assert.DoesNotContain("rate", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Farmer notebook", result.InformationSource);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/spray-declarations", request)).StatusCode);
        var detail = await client.GetFromJsonAsync<SprayDeclarationResponse>($"/api/spray-declarations/{entityId}");
        Assert.Equal(result.Id, detail!.Id);
        Assert.Equal(result.AppliedOn, detail.AppliedOn);
        Assert.Equal(result.InformationSource, detail.InformationSource);
        Assert.Equal(result.CapturedAt, detail.CapturedAt);
        Assert.Equal(result.Items.Select(x => x.Id), detail.Items.Select(x => x.Id));
        var list = await client.GetFromJsonAsync<SprayDeclarationListResponse>(
            $"/api/spray-declarations?plotSeasonId={source.PlotSeasonId}");
        Assert.Contains(list!.Items, x => x.Id == entityId && x.ItemCount == 2);
        var conflict = await client.PostAsJsonAsync("/api/spray-declarations",
            Request(source.PlotSeasonId, source.SubstanceId, entityId, operationId,
                items, "Different notebook"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.SprayDeclarations.CountAsync(x => x.Id == entityId));
        Assert.Equal(2, await db.SprayItems.CountAsync(x => x.DeclarationId == entityId && x.Rate == null && x.RateUnit == null));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == operationId));
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.ClientOperationId == operationId));
    }

    [Fact]
    public async Task Rejects_foreign_source_invalid_line_and_legacy_rate_without_writing()
    {
        var source = await SeedSourceAsync();
        var foreign = await SeedSourceAsync();
        using var client = await CustomerClientAsync(source.CompanyId);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/spray-declarations",
                Request(foreign.PlotSeasonId, source.SubstanceId))).StatusCode);
        object[] invalidItems = [new { productName = (string?)null, substanceId = (Guid?)null }];
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/spray-declarations",
                Request(source.PlotSeasonId, source.SubstanceId, items: invalidItems))).StatusCode);
        object[] legacyRate = [new { productName = "Product", substanceId = (Guid?)null,
            rate = 1m, rateUnit = "L/HA" }];
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/spray-declarations",
                Request(source.PlotSeasonId, source.SubstanceId, items: legacyRate))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/spray-declarations",
                Request(source.PlotSeasonId, source.SubstanceId,
                    appliedOn: new DateOnly(2026, 10, 14)))).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CaptureDbContext>()
            .SprayDeclarations.CountAsync(x => x.CompanyId == source.CompanyId));
    }

    [Fact]
    public async Task Read_is_tenant_scoped_and_manager_cannot_create()
    {
        var source = await SeedSourceAsync();
        var foreign = await SeedSourceAsync();
        using var writer = await CustomerClientAsync(source.CompanyId);
        using var manager = await CustomerClientAsync(source.CompanyId, Roles.CustomerManager);
        using var outsider = await CustomerClientAsync(foreign.CompanyId);
        var created = await writer.PostAsJsonAsync("/api/spray-declarations",
            Request(source.PlotSeasonId, source.SubstanceId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = (await created.Content.ReadFromJsonAsync<SprayDeclarationResponse>())!;
        Assert.Equal(HttpStatusCode.OK,
            (await manager.GetAsync($"/api/spray-declarations/{detail.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("/api/spray-declarations",
                Request(source.PlotSeasonId, source.SubstanceId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/spray-declarations/{detail.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await writer.GetAsync("/api/spray-declarations?plotSeasonId=")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_retry_creates_one_declaration_and_one_event()
    {
        var source = await SeedSourceAsync();
        using var client = await CustomerClientAsync(source.CompanyId);
        var entityId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var request = Request(source.PlotSeasonId, source.SubstanceId, entityId, operationId);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/spray-declarations", request),
            client.PostAsJsonAsync("/api/spray-declarations", request));
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.Equal(1, await db.SprayDeclarations.CountAsync(x => x.Id == entityId));
        Assert.Equal(1, await db.BusinessEvents.CountAsync(x => x.CorrelationId == operationId));
        Assert.Equal(1, await db.OfflineOperations.CountAsync(x => x.ClientOperationId == operationId));
    }
}
