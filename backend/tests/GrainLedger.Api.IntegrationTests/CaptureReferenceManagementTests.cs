using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class CaptureReferenceManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Reference_lists_page_filter_and_sort_with_creator_ownership_and_stale_versions()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var foreignCompany = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (owner, ownerSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        var (other, otherSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        var (foreign, foreignSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, foreignCompany.Id);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (otherAdmin, otherAdminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var ownerClient = factory.CreateRealCookieClient(owner, ownerSession);
        using var otherClient = factory.CreateRealCookieClient(other, otherSession);
        using var foreignClient = factory.CreateRealCookieClient(foreign, foreignSession);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var otherAdminClient = factory.CreateRealCookieClient(otherAdmin, otherAdminSession);
        await AddCsrfAsync(ownerClient);
        await AddCsrfAsync(otherClient);
        await AddCsrfAsync(foreignClient);
        await AddCsrfAsync(adminClient);
        await AddCsrfAsync(otherAdminClient);
        var ids = await SeedPagedReferencesAsync(company.Id, owner.Id, admin.Id);

        var farmers = await ownerClient.GetFromJsonAsync<FarmerListResponse>(
            "/api/farmers?search=Owned&page=1&pageSize=1&sortBy=name&sortDirection=desc");
        Assert.Equal(2, farmers!.TotalCount);
        Assert.Single(farmers.Items);
        Assert.True(farmers.HasMore);
        Assert.Equal(ids.FarmerZ, farmers.Items[0].Id);
        Assert.True(farmers.Items[0].CanEdit);
        Assert.True(farmers.Items[0].CanDelete);
        var otherFarmers = await otherClient.GetFromJsonAsync<FarmerListResponse>("/api/farmers?search=Owned");
        Assert.All(otherFarmers!.Items, x => Assert.False(x.CanEdit || x.CanDelete));
        var legacy = await ownerClient.GetFromJsonAsync<FarmerListResponse>("/api/farmers?search=Legacy");
        Assert.Null(legacy!.Items.Single().CreatedBy);
        Assert.False(legacy.Items.Single().CanEdit || legacy.Items.Single().CanDelete);

        var plots = await ownerClient.GetFromJsonAsync<PlotListResponse>(
            "/api/plots?search=PLOT&page=1&pageSize=1&sortBy=code&sortDirection=desc");
        Assert.Equal(2, plots!.TotalCount);
        Assert.True(plots.HasMore);
        Assert.Equal(ids.PlotZ, plots.Items.Single().Id);
        Assert.True(plots.Items.Single().CanEdit && plots.Items.Single().CanDelete);
        var otherPlots = await otherClient.GetFromJsonAsync<PlotListResponse>("/api/plots?search=PLOT");
        Assert.All(otherPlots!.Items, x => Assert.False(x.CanEdit || x.CanDelete));

        var seasonsUrl = $"/api/crop-seasons?companyId={company.Id}&search=Unused&page=1&pageSize=1&sortBy=name&sortDirection=asc";
        var seasons = await adminClient.GetFromJsonAsync<CropSeasonListResponse>(seasonsUrl);
        Assert.Equal(2, seasons!.TotalCount);
        Assert.True(seasons.HasMore);
        Assert.Equal(ids.SeasonA, seasons.Items.Single().Id);
        Assert.True(seasons.Items.Single().CanEdit && seasons.Items.Single().CanDelete);
        var otherSeasons = await otherAdminClient.GetFromJsonAsync<CropSeasonListResponse>(seasonsUrl);
        Assert.All(otherSeasons!.Items, x => Assert.False(x.CanEdit || x.CanDelete));

        var plotSeasons = await ownerClient.GetFromJsonAsync<PlotSeasonListResponse>(
            "/api/plot-seasons?search=ST25&page=1&pageSize=1&sortBy=id&sortDirection=desc");
        Assert.Equal(2, plotSeasons!.TotalCount);
        Assert.True(plotSeasons.HasMore);
        Assert.Equal(ids.PlotSeasonZ, plotSeasons.Items.Single().Id);
        Assert.Equal("SUPPORT-B", plotSeasons.Items.Single().PlotCode);
        Assert.Equal("Support Season", plotSeasons.Items.Single().SeasonName);
        var scopedPlotSeasons = await ownerClient.GetFromJsonAsync<PlotSeasonListResponse>(
            $"/api/plot-seasons?plotId={ids.SupportPlotA}&search=ST25");
        Assert.Equal(1, scopedPlotSeasons!.TotalCount);
        var seasonDetail = await adminClient.GetFromJsonAsync<CropSeasonReferenceItem>($"/api/crop-seasons/{ids.SeasonA}");
        Assert.Equal(ids.SeasonA, seasonDetail!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/crop-seasons/{ids.SeasonA}")).StatusCode);
        Assert.True(plotSeasons.Items.Single().CanEdit && plotSeasons.Items.Single().CanDelete);
        var otherPlotSeasons = await otherClient.GetFromJsonAsync<PlotSeasonListResponse>("/api/plot-seasons?search=ST25");
        Assert.All(otherPlotSeasons!.Items, x => Assert.False(x.CanEdit || x.CanDelete));

        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/farmers/{ids.FarmerA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/plots/{ids.PlotA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreignClient.GetAsync($"/api/plot-seasons/{ids.PlotSeasonA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.PutAsJsonAsync($"/api/farmers/{ids.LegacyFarmer}",
            new UpdateFarmerRequest("LEGACY-2", "Legacy", null, 1))).StatusCode);
        using (var legacyDelete = await DeleteAsync(ownerClient, $"/api/farmers/{ids.LegacyFarmer}", 1))
            Assert.Equal(HttpStatusCode.Forbidden, legacyDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PutAsJsonAsync($"/api/farmers/{ids.FarmerA}",
            new UpdateFarmerRequest("OWNED-A2", "Owned A", null, 1))).StatusCode);

        using (var stale = await ownerClient.PutAsJsonAsync($"/api/farmers/{ids.FarmerA}",
                   new UpdateFarmerRequest("OWNED-A2", "Changed A", null, 2)))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using (var updated = await ownerClient.PutAsJsonAsync($"/api/farmers/{ids.FarmerA}",
                   new UpdateFarmerRequest("OWNED-A2", "Changed A", null, 1)))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<FarmerReferenceItem>())!.Version);
        }
        using (var staleDelete = await DeleteAsync(ownerClient, $"/api/farmers/{ids.FarmerA}", 1))
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleDelete.StatusCode);
        using (var deleted = await DeleteAsync(ownerClient, $"/api/farmers/{ids.FarmerA}", 2))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var geometry = Point();
        using (var updated = await ownerClient.PutAsJsonAsync($"/api/plots/{ids.PlotA}",
                   new UpdatePlotRequest(ids.SupportFarmer, "PLOT-A2", geometry, 2m, 1)))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<PlotReferenceResponse>())!.Version);
        }
        using (var staleDelete = await DeleteAsync(ownerClient, $"/api/plots/{ids.PlotA}", 1))
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleDelete.StatusCode);
        using (var deleted = await DeleteAsync(ownerClient, $"/api/plots/{ids.PlotA}", 2))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using (var updated = await adminClient.PutAsJsonAsync($"/api/crop-seasons/{ids.SeasonA}",
                   new UpdateCropSeasonRequest(company.Id, "Updated Season", new DateOnly(2026, 11, 1), new DateOnly(2027, 3, 31), 1)))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<CropSeasonReferenceItem>())!.Version);
        }
        using (var staleDelete = await DeleteAsync(adminClient, $"/api/crop-seasons/{ids.SeasonA}", 1))
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleDelete.StatusCode);
        using (var deleted = await DeleteAsync(adminClient, $"/api/crop-seasons/{ids.SeasonA}", 2))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using (var updated = await ownerClient.PutAsJsonAsync($"/api/plot-seasons/{ids.PlotSeasonA}",
                   new UpdatePlotSeasonRequest(ids.SupportPlotA, ids.SupportSeason, "ST25 Updated", 1)))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<PlotSeasonReferenceResponse>())!.Version);
        }
        using (var staleDelete = await DeleteAsync(ownerClient, $"/api/plot-seasons/{ids.PlotSeasonA}", 1))
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleDelete.StatusCode);
        using (var deleted = await DeleteAsync(ownerClient, $"/api/plot-seasons/{ids.PlotSeasonA}", 2))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/farmers/{ids.FarmerA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/plots/{ids.PlotA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync($"/api/crop-seasons/{ids.SeasonA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/plot-seasons/{ids.PlotSeasonA}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
            Assert.True((await db.Farmers.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.FarmerA)).IsDeleted);
            Assert.Equal(3, (await db.Farmers.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.FarmerA)).Version);
            Assert.True((await db.Plots.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.PlotA)).IsDeleted);
            Assert.Equal(3, (await db.Plots.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.PlotA)).Version);
            Assert.True((await db.CropSeasons.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.SeasonA)).IsDeleted);
            Assert.Equal(3, (await db.CropSeasons.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.SeasonA)).Version);
            Assert.True((await db.PlotSeasons.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.PlotSeasonA)).IsDeleted);
            Assert.Equal(3, (await db.PlotSeasons.IgnoreQueryFilters().SingleAsync(x => x.Id == ids.PlotSeasonA)).Version);
            Assert.Equal(4, await db.BusinessEvents.CountAsync(x => x.CompanyId == company.Id && x.EventType.EndsWith("_UPDATED")));
            Assert.Equal(4, await db.BusinessEvents.CountAsync(x => x.CompanyId == company.Id && x.EventType.EndsWith("_DELETED")));
        }
    }

    [Fact]
    public async Task References_with_dependents_reject_edits_and_deletes()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (owner, ownerSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var ownerClient = factory.CreateRealCookieClient(owner, ownerSession);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        await AddCsrfAsync(ownerClient);
        await AddCsrfAsync(adminClient);

        var farmerId = Guid.NewGuid();
        var plotId = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var plotSeasonId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
            db.Farmers.Add(new Farmer { Id = farmerId, CompanyId = company.Id, Code = "IN-USE-FARMER", Name = "In Use Farmer",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = owner.Id });
            await db.SaveChangesAsync();
            db.Plots.Add(new Plot { Id = plotId, CompanyId = company.Id, FarmerId = farmerId, Code = "IN-USE-PLOT",
                BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m, CreatedBy = owner.Id });
            await db.SaveChangesAsync();
            db.CropSeasons.Add(new CropSeason { Id = seasonId, CompanyId = company.Id, Name = "In Use Season",
                StartsOn = new DateOnly(2026, 11, 1), EndsOn = new DateOnly(2027, 3, 31), CreatedBy = admin.Id });
            await db.SaveChangesAsync();
            db.PlotSeasons.Add(new PlotSeason { Id = plotSeasonId, CompanyId = company.Id, PlotId = plotId,
                SeasonId = seasonId, RiceVariety = "ST25", CreatedBy = owner.Id });
            await db.SaveChangesAsync();
            db.SprayDeclarations.Add(new SprayDeclaration { Id = Guid.NewGuid(), CompanyId = company.Id,
                PlotSeasonId = plotSeasonId, AppliedOn = new DateOnly(2027, 1, 10), RecordedBy = owner.Id,
                RecordedAt = DateTimeOffset.UtcNow, EvidenceRef = null });
            await db.SaveChangesAsync();
        }

        using (var response = await ownerClient.PutAsJsonAsync($"/api/farmers/{farmerId}",
                   new UpdateFarmerRequest("IN-USE-FARMER-2", "In Use Farmer", null, 1)))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await DeleteAsync(ownerClient, $"/api/farmers/{farmerId}", 1))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await ownerClient.PutAsJsonAsync($"/api/plots/{plotId}",
                   new UpdatePlotRequest(farmerId, "IN-USE-PLOT-2", Point(), 2m, 1)))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await DeleteAsync(ownerClient, $"/api/plots/{plotId}", 1))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await adminClient.PutAsJsonAsync($"/api/crop-seasons/{seasonId}",
                   new UpdateCropSeasonRequest(company.Id, "In Use Season 2", new DateOnly(2026, 11, 1), new DateOnly(2027, 3, 31), 1)))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await DeleteAsync(adminClient, $"/api/crop-seasons/{seasonId}", 1))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await ownerClient.PutAsJsonAsync($"/api/plot-seasons/{plotSeasonId}",
                   new UpdatePlotSeasonRequest(plotId, seasonId, "ST25 Updated", 1)))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await DeleteAsync(ownerClient, $"/api/plot-seasons/{plotSeasonId}", 1))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Soft_deleted_child_allows_parent_retirement_but_cannot_be_reused()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (owner, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var client = factory.CreateRealCookieClient(owner, session);
        await AddCsrfAsync(client);
        var farmerId = Guid.NewGuid();
        var plotId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
            db.Farmers.Add(new Farmer { Id = farmerId, CompanyId = company.Id, Code = "RETIRED-PARENT",
                Name = "Retired Parent", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = owner.Id });
            db.Plots.Add(new Plot { Id = plotId, CompanyId = company.Id, FarmerId = farmerId, Code = "RETIRED-CHILD",
                BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m, CreatedBy = owner.Id });
            await db.SaveChangesAsync();
        }

        using (var retiredChild = await DeleteAsync(client, $"/api/plots/{plotId}", 1))
            Assert.Equal(HttpStatusCode.NoContent, retiredChild.StatusCode);
        using (var retiredParent = await DeleteAsync(client, $"/api/farmers/{farmerId}", 1))
            Assert.Equal(HttpStatusCode.NoContent, retiredParent.StatusCode);
        using (var duplicateCode = await client.PostAsJsonAsync("/api/farmers", new
                   {
                       deviceId = "soft-delete-test", clientOperationId = Guid.NewGuid(), capturedAt = DateTimeOffset.UtcNow,
                       payloadSchemaVersion = 1, payload = new { entityId = Guid.NewGuid(), code = "RETIRED-PARENT",
                           name = "Duplicate", contact = (string?)null }
                   }))
            Assert.Equal(HttpStatusCode.Conflict, duplicateCode.StatusCode);
        using (var newPlot = await client.PostAsJsonAsync("/api/plots", new
                   {
                       deviceId = "soft-delete-test", clientOperationId = Guid.NewGuid(), capturedAt = DateTimeOffset.UtcNow,
                       payloadSchemaVersion = 1,
                       payload = new { entityId = Guid.NewGuid(), farmerId, code = "REUSE-RETIRED-FARMER",
                           geometry = Point(), areaHa = 1m }
                   }))
            Assert.Equal(HttpStatusCode.Conflict, newPlot.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        Assert.True((await verifyDb.Farmers.IgnoreQueryFilters().SingleAsync(x => x.Id == farmerId)).IsDeleted);
        Assert.True((await verifyDb.Plots.IgnoreQueryFilters().SingleAsync(x => x.Id == plotId)).IsDeleted);
        Assert.Empty(await verifyDb.Farmers.Where(x => x.Id == farmerId).ToListAsync());
        Assert.Empty(await verifyDb.Plots.Where(x => x.Id == plotId).ToListAsync());
    }

    private async Task<ReferenceIds> SeedPagedReferencesAsync(Guid companyId, Guid ownerId, Guid adminId)
    {
        var supportFarmer = Guid.NewGuid();
        var farmerA = Guid.NewGuid();
        var farmerZ = Guid.NewGuid();
        var legacyFarmer = Guid.NewGuid();
        var plotA = Guid.NewGuid();
        var plotZ = Guid.NewGuid();
        var supportPlotA = Guid.NewGuid();
        var supportPlotB = Guid.NewGuid();
        var supportSeason = Guid.NewGuid();
        var seasonA = Guid.NewGuid();
        var seasonZ = Guid.NewGuid();
        var plotSeasonA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var plotSeasonZ = Guid.Parse("00000000-0000-0000-0000-000000000002");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        db.Farmers.AddRange(
            new Farmer { Id = supportFarmer, CompanyId = companyId, Code = "SUPPORT-FARMER", Name = "Support Farmer", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = ownerId },
            new Farmer { Id = farmerA, CompanyId = companyId, Code = "OWNED-A", Name = "Owned A", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = ownerId },
            new Farmer { Id = farmerZ, CompanyId = companyId, Code = "OWNED-Z", Name = "Owned Z", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = ownerId },
            new Farmer { Id = legacyFarmer, CompanyId = companyId, Code = "LEGACY", Name = "Legacy", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = null });
        await db.SaveChangesAsync();
        db.Plots.AddRange(
            new Plot { Id = plotA, CompanyId = companyId, FarmerId = supportFarmer, Code = "PLOT-A", BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m, CreatedBy = ownerId },
            new Plot { Id = plotZ, CompanyId = companyId, FarmerId = supportFarmer, Code = "PLOT-Z", BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 2m, CreatedBy = ownerId },
            new Plot { Id = supportPlotA, CompanyId = companyId, FarmerId = supportFarmer, Code = "SUPPORT-A", BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m, CreatedBy = ownerId },
            new Plot { Id = supportPlotB, CompanyId = companyId, FarmerId = supportFarmer, Code = "SUPPORT-B", BoundaryGeojson = "{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}", AreaHa = 1m, CreatedBy = ownerId });
        await db.SaveChangesAsync();
        db.CropSeasons.AddRange(
            new CropSeason { Id = supportSeason, CompanyId = companyId, Name = "Support Season", StartsOn = new DateOnly(2026, 11, 1), EndsOn = new DateOnly(2027, 3, 31), CreatedBy = adminId },
            new CropSeason { Id = seasonA, CompanyId = companyId, Name = "Unused A", StartsOn = new DateOnly(2026, 11, 1), EndsOn = new DateOnly(2027, 3, 31), CreatedBy = adminId },
            new CropSeason { Id = seasonZ, CompanyId = companyId, Name = "Unused Z", StartsOn = new DateOnly(2026, 11, 1), EndsOn = new DateOnly(2027, 3, 31), CreatedBy = adminId });
        await db.SaveChangesAsync();
        db.PlotSeasons.AddRange(
            new PlotSeason { Id = plotSeasonA, CompanyId = companyId, PlotId = supportPlotA, SeasonId = supportSeason, RiceVariety = "ST25 A", CreatedBy = ownerId },
            new PlotSeason { Id = plotSeasonZ, CompanyId = companyId, PlotId = supportPlotB, SeasonId = supportSeason, RiceVariety = "ST25 Z", CreatedBy = ownerId });
        await db.SaveChangesAsync();
        return new ReferenceIds(supportFarmer, supportPlotA, supportSeason, farmerA, farmerZ, legacyFarmer,
            plotA, plotZ, seasonA, seasonZ, plotSeasonA, plotSeasonZ);
    }

    private static async Task AddCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new { version }) };
        return await client.SendAsync(request);
    }

    private static JsonElement Point() => JsonDocument.Parse("{\"type\":\"Point\",\"coordinates\":[106.35,10.4]}").RootElement.Clone();

    private sealed record ReferenceIds(Guid SupportFarmer, Guid SupportPlotA, Guid SupportSeason,
        Guid FarmerA, Guid FarmerZ, Guid LegacyFarmer, Guid PlotA, Guid PlotZ, Guid SeasonA, Guid SeasonZ,
        Guid PlotSeasonA, Guid PlotSeasonZ);
}
