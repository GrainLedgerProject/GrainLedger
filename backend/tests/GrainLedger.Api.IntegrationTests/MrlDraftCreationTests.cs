using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MrlDraftCreationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, Guid MarketId, Guid CommodityId)> CreateAdminAndCatalogsAsync()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var client = factory.CreateRealCookieClient(admin, session);
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var code = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var market = await client.PostAsJsonAsync("/api/markets", new { code = $"M-{code}", name = "Test market" });
        var commodity = await client.PostAsJsonAsync("/api/commodities", new { code = $"C-{code}", name = "Test commodity" });
        Assert.Equal(HttpStatusCode.Created, market.StatusCode);
        Assert.Equal(HttpStatusCode.Created, commodity.StatusCode);
        return (client,
            (await market.Content.ReadFromJsonAsync<MarketResponse>())!.Id,
            (await commodity.Content.ReadFromJsonAsync<CommodityResponse>())!.Id);
    }

    [Fact]
    public async Task Admin_creates_versioned_draft_with_utc_dates_and_audit()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var request = new
            {
                marketId, commodityId,
                effectiveFrom = "2027-01-01T07:00:00+07:00",
                effectiveTo = "2028-01-01T00:00:00Z",
                sourceRef = " Official regulation, section 1 "
            };
            var first = await client.PostAsJsonAsync("/api/mrl-versions", request);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var draft = (await first.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            Assert.Equal(1, draft.VersionNo);
            Assert.Equal("DRAFT", draft.Status);
            Assert.Null(draft.PublishedBy);
            Assert.Null(draft.PublishedAt);
            Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00Z"), draft.EffectiveFrom);
            Assert.Equal("Official regulation, section 1", draft.SourceRef);
            Assert.Equal($"/api/mrl-versions/{draft.Id}", first.Headers.Location?.OriginalString);
            Assert.NotNull(first.Headers.ETag);
            var detail = await client.GetAsync($"/api/mrl-versions/{draft.Id}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(first.Headers.ETag, detail.Headers.ETag);
            Assert.Equal(draft, await detail.Content.ReadFromJsonAsync<MrlVersionResponse>());

            var second = await client.PostAsJsonAsync("/api/mrl-versions", request);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            Assert.Equal(2, (await second.Content.ReadFromJsonAsync<MrlVersionResponse>())!.VersionNo);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(2, await db.MrlVersions.CountAsync(x => x.MarketId == marketId && x.CommodityId == commodityId));
            Assert.Equal("MRL_VERSION_CREATED", (await db.CatalogEvents.SingleAsync(x => x.ResourceId == draft.Id)).EventType);
        }
    }

    [Fact]
    public async Task Draft_is_admin_only_and_published_version_is_readable_with_mrl_permission()
    {
        var (adminClient, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (adminClient)
        {
            var created = await adminClient.PostAsJsonAsync("/api/mrl-versions", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation"
            });
            var draft = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{draft.Id}";
            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, exporterSession) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
            using var exporterClient = factory.CreateRealCookieClient(exporter, exporterSession);
            using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);

            Assert.Equal(HttpStatusCode.NotFound, (await exporterClient.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await purchaserClient.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await adminClient.GetAsync($"/api/mrl-versions/{Guid.NewGuid()}")).StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var version = await db.MrlVersions.SingleAsync(x => x.Id == draft.Id);
                version.Status = "PUBLISHED";
                version.PublishedBy = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == draft.Id)).ActorId;
                version.PublishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var published = await exporterClient.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            Assert.Equal("PUBLISHED", (await published.Content.ReadFromJsonAsync<MrlVersionResponse>())!.Status);
            Assert.NotNull(published.Headers.ETag);
        }
    }

    [Fact]
    public async Task List_filters_and_pages_versions_without_disclosing_drafts_to_readers()
    {
        var (adminClient, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (adminClient)
        {
            var request = new { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" };
            var first = (await (await adminClient.PostAsJsonAsync("/api/mrl-versions", request))
                .Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var second = (await (await adminClient.PostAsJsonAsync("/api/mrl-versions", request))
                .Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var filter = $"?marketId={marketId}&commodityId={commodityId}&status=DRAFT";
            var page1 = await adminClient.GetFromJsonAsync<MrlVersionListResponse>(
                $"/api/mrl-versions{filter}&page=1&pageSize=1");
            Assert.NotNull(page1);
            Assert.Single(page1.Items);
            Assert.Equal(second.Id, page1.Items[0].Id);
            Assert.True(page1.HasMore);
            var page2 = await adminClient.GetFromJsonAsync<MrlVersionListResponse>(
                $"/api/mrl-versions{filter}&page=2&pageSize=1");
            Assert.NotNull(page2);
            Assert.Equal(first.Id, Assert.Single(page2.Items).Id);
            Assert.False(page2.HasMore);

            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, session) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            using var reader = factory.CreateRealCookieClient(exporter, session);
            var hidden = await reader.GetFromJsonAsync<MrlVersionListResponse>("/api/mrl-versions?status=DRAFT");
            Assert.Empty(hidden!.Items);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var published = await db.MrlVersions.SingleAsync(x => x.Id == first.Id);
                published.Status = "PUBLISHED";
                published.PublishedBy = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == first.Id)).ActorId;
                published.PublishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var visible = await reader.GetFromJsonAsync<MrlVersionListResponse>(
                $"/api/mrl-versions?marketId={marketId}&commodityId={commodityId}");
            Assert.Equal(first.Id, Assert.Single(visible!.Items).Id);
            Assert.False(visible.HasMore);
            var adminDrafts = await adminClient.GetFromJsonAsync<MrlVersionListResponse>(
                $"/api/mrl-versions?marketId={marketId}&status=DRAFT");
            Assert.Equal(second.Id, Assert.Single(adminDrafts!.Items).Id);

            var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
            using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await purchaserClient.GetAsync("/api/mrl-versions")).StatusCode);
        }
    }

    [Theory]
    [InlineData("marketId=")]
    [InlineData("commodityId=invalid")]
    [InlineData("status=UNKNOWN")]
    [InlineData("status=DRAFT&status=PUBLISHED")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task List_rejects_invalid_query_values(string query)
    {
        var (client, _, _) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var response = await client.GetAsync($"/api/mrl-versions?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Admin_updates_only_draft_fields_with_etag_and_audit()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z",
                effectiveTo = "2028-01-01T00:00:00Z", sourceRef = "Old regulation"
            });
            var draft = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{draft.Id}";
            var oldEtag = created.Headers.ETag!;

            using var missing = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { sourceRef = "New regulation" }) };
            Assert.Equal((HttpStatusCode)428, (await client.SendAsync(missing)).StatusCode);

            using var invalid = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { marketId = Guid.NewGuid() }) };
            invalid.Headers.IfMatch.Add(oldEtag);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);

            using var reversed = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { effectiveFrom = "2029-01-01T00:00:00Z" }) };
            reversed.Headers.IfMatch.Add(oldEtag);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(reversed)).StatusCode);

            using var update = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { effectiveTo = (string?)null, sourceRef = " New regulation " }) };
            update.Headers.IfMatch.Add(oldEtag);
            var updated = await client.SendAsync(update);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var body = (await updated.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            Assert.Equal(draft.Id, body.Id);
            Assert.Equal(draft.MarketId, body.MarketId);
            Assert.Equal(draft.CommodityId, body.CommodityId);
            Assert.Equal(draft.VersionNo, body.VersionNo);
            Assert.Null(body.EffectiveTo);
            Assert.Equal("New regulation", body.SourceRef);
            Assert.NotEqual(oldEtag, updated.Headers.ETag);

            using var stale = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { sourceRef = "Stale update" }) };
            stale.Headers.IfMatch.Add(oldEtag);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
            Assert.Equal(body, await client.GetFromJsonAsync<MrlVersionResponse>(url));
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(1, await db.CatalogEvents.CountAsync(x =>
                x.ResourceId == draft.Id && x.EventType == "MRL_VERSION_UPDATED"));
        }
    }

    [Fact]
    public async Task Published_version_and_non_admin_cannot_be_updated()
    {
        var (adminClient, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (adminClient)
        {
            var created = await adminClient.PostAsJsonAsync("/api/mrl-versions", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation"
            });
            var draft = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{draft.Id}";
            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, session) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            using var reader = factory.CreateRealCookieClient(exporter, session);
            using var forbidden = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { sourceRef = "Wrong actor" }) };
            forbidden.Headers.IfMatch.Add(created.Headers.ETag!);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(forbidden)).StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var version = await db.MrlVersions.SingleAsync(x => x.Id == draft.Id);
                version.Status = "PUBLISHED";
                version.PublishedBy = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == draft.Id)).ActorId;
                version.PublishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var published = await adminClient.GetAsync(url);
            using var rejected = new HttpRequestMessage(HttpMethod.Patch, url)
            { Content = JsonContent.Create(new { sourceRef = "Cannot edit" }) };
            rejected.Headers.IfMatch.Add(published.Headers.ETag!);
            Assert.Equal(HttpStatusCode.Conflict, (await adminClient.SendAsync(rejected)).StatusCode);
        }
    }

    [Fact]
    public async Task Limits_list_is_filtered_paged_and_hides_drafts_from_readers()
    {
        var (adminClient, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (adminClient)
        {
            var created = await adminClient.PostAsJsonAsync("/api/mrl-versions", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation"
            });
            var draft = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{draft.Id}/limits";
            var empty = await adminClient.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Empty((await empty.Content.ReadFromJsonAsync<MrlLimitListResponse>())!.Items);
            Assert.Equal(created.Headers.ETag, empty.Headers.ETag);

            var code = Guid.NewGuid().ToString("N")[..12];
            var first = await adminClient.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}-1", name = "First substance" });
            var second = await adminClient.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}-2", name = "Second substance" });
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var firstId = (await first.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            var secondId = (await second.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                db.MrlLimits.AddRange(
                    new MrlLimit { Id = Guid.NewGuid(), MrlVersionId = draft.Id,
                        SubstanceId = firstId, LimitValue = 0.01m, Unit = "mg/kg" },
                    new MrlLimit { Id = Guid.NewGuid(), MrlVersionId = draft.Id,
                        SubstanceId = secondId, LimitValue = 0.02m, Unit = "mg/kg" });
                await db.SaveChangesAsync();
            }
            var filtered = await adminClient.GetFromJsonAsync<MrlLimitListResponse>($"{url}?substanceId={firstId}");
            var row = Assert.Single(filtered!.Items);
            Assert.Equal(firstId, row.SubstanceId);
            Assert.Equal(draft.Id, row.MrlVersionId);
            Assert.Equal(0.01m, row.LimitValue);
            var page1 = await adminClient.GetFromJsonAsync<MrlLimitListResponse>($"{url}?page=1&pageSize=1");
            var page2 = await adminClient.GetFromJsonAsync<MrlLimitListResponse>($"{url}?page=2&pageSize=1");
            Assert.True(page1!.HasMore);
            Assert.False(page2!.HasMore);
            Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);

            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, exporterSession) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
            using var reader = factory.CreateRealCookieClient(exporter, exporterSession);
            using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
            Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await purchaserClient.GetAsync(url)).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var version = await db.MrlVersions.SingleAsync(x => x.Id == draft.Id);
                version.Status = "PUBLISHED";
                version.PublishedBy = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == draft.Id)).ActorId;
                version.PublishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var published = await reader.GetFromJsonAsync<MrlLimitListResponse>(url);
            Assert.Equal(2, published!.Items.Count);
            Assert.All(published.Items, x => Assert.Equal("mg/kg", x.Unit));
        }
    }

    [Theory]
    [InlineData("substanceId=")]
    [InlineData("substanceId=invalid")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    public async Task Limits_list_rejects_invalid_queries(string query)
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            {
                marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation"
            });
            var draft = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.GetAsync($"/api/mrl-versions/{draft.Id}/limits?{query}")).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_upserts_draft_limit_and_parent_etag_changes_atomically()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var code = Guid.NewGuid().ToString("N")[..12];
            var unit = await client.PostAsJsonAsync("/api/measurement-units", new
            { code = "MG/KG", name = "Milligram per kilogram", quantityKind = "RESIDUE_CONCENTRATION" });
            Assert.True(unit.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
            var substance = await client.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}", name = "Test substance" });
            var substanceId = (await substance.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" });
            var version = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{version.Id}/limits/{substanceId}";
            var originalEtag = created.Headers.ETag!;

            using var missing = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.01m, unit = "mg/kg" }) };
            Assert.Equal((HttpStatusCode)428, (await client.SendAsync(missing)).StatusCode);

            using var add = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.01m, unit = "mg/kg" }) };
            add.Headers.IfMatch.Add(originalEtag);
            var added = await client.SendAsync(add);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var row = (await added.Content.ReadFromJsonAsync<MrlLimitResponse>())!;
            Assert.Equal(0.01m, row.LimitValue);
            Assert.Equal("mg/kg", row.Unit);
            Assert.NotEqual(originalEtag, added.Headers.ETag);
            var list = await client.GetFromJsonAsync<MrlLimitListResponse>($"/api/mrl-versions/{version.Id}/limits");
            Assert.Equal(row, Assert.Single(list!.Items));

            using var stale = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.02m, unit = "mg/kg" }) };
            stale.Headers.IfMatch.Add(originalEtag);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);

            using var replace = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.02m, unit = "MG/KG" }) };
            replace.Headers.IfMatch.Add(added.Headers.ETag!);
            var replaced = await client.SendAsync(replace);
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var updatedRow = (await replaced.Content.ReadFromJsonAsync<MrlLimitResponse>())!;
            Assert.Equal(row.Id, updatedRow.Id);
            Assert.Equal(0.02m, updatedRow.LimitValue);
            Assert.NotEqual(added.Headers.ETag, replaced.Headers.ETag);
            using var replay = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.02m, unit = "mg/kg" }) };
            replay.Headers.IfMatch.Add(replaced.Headers.ETag!);
            var unchanged = await client.SendAsync(replay);
            Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
            Assert.Equal(replaced.Headers.ETag, unchanged.Headers.ETag);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(1, await db.MrlLimits.CountAsync(x => x.MrlVersionId == version.Id));
            Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == row.Id &&
                (x.EventType == "MRL_LIMIT_CREATED" || x.EventType == "MRL_LIMIT_UPDATED")));
        }
    }

    [Fact]
    public async Task Limit_upsert_rejects_invalid_values_missing_substance_and_published_version()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" });
            var version = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{version.Id}/limits/{Guid.NewGuid()}";
            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, exporterSession) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            using var reader = factory.CreateRealCookieClient(exporter, exporterSession);
            using var forbidden = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.01m, unit = "mg/kg" }) };
            forbidden.Headers.IfMatch.Add(created.Headers.ETag!);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(forbidden)).StatusCode);

            async Task<HttpResponseMessage> PutAsync(object body, EntityTagHeaderValue? etag = null)
            {
                using var message = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
                message.Headers.IfMatch.Add(etag ?? created.Headers.ETag!);
                return await client.SendAsync(message);
            }

            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutAsync(new { limitValue = -1m, unit = "mg/kg" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutAsync(new { limitValue = 0.0000000000001m, unit = "mg/kg" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutAsync(new { limitValue = 0.01m, unit = "ppm" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await PutAsync(new { limitValue = 0.01m, unit = "mg/kg" })).StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var row = await db.MrlVersions.SingleAsync(x => x.Id == version.Id);
                row.Status = "PUBLISHED";
                row.PublishedBy = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == version.Id)).ActorId;
                row.PublishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var published = await client.GetAsync($"/api/mrl-versions/{version.Id}");
            Assert.Equal(HttpStatusCode.Conflict,
                (await PutAsync(new { limitValue = 0.01m, unit = "mg/kg" }, published.Headers.ETag)).StatusCode);
            using var scopeAfter = factory.Services.CreateScope();
            var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.False(await dbAfter.MrlLimits.AnyAsync(x => x.MrlVersionId == version.Id));
        }
    }

    [Fact]
    public async Task Concurrent_limit_writes_with_the_same_etag_accept_only_one()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var unit = await client.PostAsJsonAsync("/api/measurement-units", new
            { code = "MG/KG", name = "Milligram per kilogram", quantityKind = "RESIDUE_CONCENTRATION" });
            Assert.True(unit.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
            var code = Guid.NewGuid().ToString("N")[..12];
            var first = await client.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}-A", name = "First" });
            var second = await client.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}-B", name = "Second" });
            var firstId = (await first.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            var secondId = (await second.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" });
            var version = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;

            async Task<HttpResponseMessage> WriteAsync(Guid substanceId)
            {
                using var request = new HttpRequestMessage(HttpMethod.Put,
                    $"/api/mrl-versions/{version.Id}/limits/{substanceId}")
                { Content = JsonContent.Create(new { limitValue = 0.01m, unit = "mg/kg" }) };
                request.Headers.IfMatch.Add(created.Headers.ETag!);
                return await client.SendAsync(request);
            }

            var results = await Task.WhenAll(WriteAsync(firstId), WriteAsync(secondId));
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(1, await db.MrlLimits.CountAsync(x => x.MrlVersionId == version.Id));
        }
    }

    [Fact]
    public async Task Admin_deletes_draft_limit_with_fresh_etag_and_audit()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var unit = await client.PostAsJsonAsync("/api/measurement-units", new
            { code = "MG/KG", name = "Milligram per kilogram", quantityKind = "RESIDUE_CONCENTRATION" });
            Assert.True(unit.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
            var code = Guid.NewGuid().ToString("N")[..12];
            var substance = await client.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}", name = "Test substance" });
            var substanceId = (await substance.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" });
            var version = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var url = $"/api/mrl-versions/{version.Id}/limits/{substanceId}";
            using var add = new HttpRequestMessage(HttpMethod.Put, url)
            { Content = JsonContent.Create(new { limitValue = 0.01m, unit = "mg/kg" }) };
            add.Headers.IfMatch.Add(created.Headers.ETag!);
            var added = await client.SendAsync(add);
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var limit = (await added.Content.ReadFromJsonAsync<MrlLimitResponse>())!;

            using var missing = new HttpRequestMessage(HttpMethod.Delete, url);
            Assert.Equal((HttpStatusCode)428, (await client.SendAsync(missing)).StatusCode);
            using var stale = new HttpRequestMessage(HttpMethod.Delete, url);
            stale.Headers.IfMatch.Add(created.Headers.ETag!);
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(stale)).StatusCode);
            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, session) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            using var reader = factory.CreateRealCookieClient(exporter, session);
            using var forbidden = new HttpRequestMessage(HttpMethod.Delete, url);
            forbidden.Headers.IfMatch.Add(added.Headers.ETag!);
            Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(forbidden)).StatusCode);

            using var remove = new HttpRequestMessage(HttpMethod.Delete, url);
            remove.Headers.IfMatch.Add(added.Headers.ETag!);
            var deleted = await client.SendAsync(remove);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.NotEqual(added.Headers.ETag, deleted.Headers.ETag);
            var rows = await client.GetFromJsonAsync<MrlLimitListResponse>($"/api/mrl-versions/{version.Id}/limits");
            Assert.Empty(rows!.Items);
            using var repeat = new HttpRequestMessage(HttpMethod.Delete, url);
            repeat.Headers.IfMatch.Add(deleted.Headers.ETag!);
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(repeat)).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.False(await db.MrlLimits.AnyAsync(x => x.Id == limit.Id));
            var audit = await db.CatalogEvents.SingleAsync(x =>
                x.ResourceId == limit.Id && x.EventType == "MRL_LIMIT_DELETED");
            Assert.NotNull(audit.BeforeState);
            Assert.Null(audit.AfterState);
        }
    }

    [Fact]
    public async Task Published_limit_cannot_be_deleted()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            var code = Guid.NewGuid().ToString("N")[..12];
            var substance = await client.PostAsJsonAsync("/api/active-substances",
                new { code = $"S-{code}", name = "Test substance" });
            var substanceId = (await substance.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
            var created = await client.PostAsJsonAsync("/api/mrl-versions", new
            { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" });
            var version = (await created.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            var limitId = Guid.NewGuid();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                db.MrlLimits.Add(new MrlLimit { Id = limitId, MrlVersionId = version.Id,
                    SubstanceId = substanceId, LimitValue = 0.01m, Unit = "mg/kg" });
                var entity = await db.MrlVersions.SingleAsync(x => x.Id == version.Id);
                entity.Status = "PUBLISHED";
                entity.PublishedBy = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == version.Id)).ActorId;
                entity.PublishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            var published = await client.GetAsync($"/api/mrl-versions/{version.Id}");
            using var remove = new HttpRequestMessage(HttpMethod.Delete,
                $"/api/mrl-versions/{version.Id}/limits/{substanceId}");
            remove.Headers.IfMatch.Add(published.Headers.ETag!);
            Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(remove)).StatusCode);
            using var scopeAfter = factory.Services.CreateScope();
            var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.True(await dbAfter.MrlLimits.AnyAsync(x => x.Id == limitId));
        }
    }

    [Fact]
    public async Task Invalid_dates_source_and_missing_catalogs_do_not_create_draft()
    {
        var (client, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (client)
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.PostAsJsonAsync("/api/mrl-versions", new
                {
                    marketId, commodityId, effectiveFrom = "2028-01-01T00:00:00Z",
                    effectiveTo = "2027-01-01T00:00:00Z", sourceRef = " "
                })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync("/api/mrl-versions", new
                {
                    marketId = Guid.NewGuid(), commodityId, effectiveFrom = "2027-01-01T00:00:00Z",
                    sourceRef = "Regulation"
                })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsJsonAsync("/api/mrl-versions", new
                {
                    marketId, commodityId = Guid.NewGuid(), effectiveFrom = "2027-01-01T00:00:00Z",
                    sourceRef = "Regulation"
                })).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(0, await db.MrlVersions.CountAsync(x => x.MarketId == marketId && x.CommodityId == commodityId));
        }
    }

    [Fact]
    public async Task Customer_cannot_create_draft_and_parallel_admin_creates_get_distinct_numbers()
    {
        var (adminClient, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (adminClient)
        {
            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (purchaser, session) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
            using var customerClient = factory.CreateRealCookieClient(purchaser, session);
            var request = new { marketId, commodityId, effectiveFrom = "2027-01-01T00:00:00Z", sourceRef = "Regulation" };
            Assert.Equal(HttpStatusCode.Forbidden,
                (await customerClient.PostAsJsonAsync("/api/mrl-versions", request)).StatusCode);

            var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => adminClient.PostAsJsonAsync("/api/mrl-versions", request)));
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var drafts = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<MrlVersionResponse>()));
            Assert.Equal([1, 2, 3, 4], drafts.Select(x => x!.VersionNo).OrderBy(x => x).ToArray());
        }
    }

    [Fact]
    public async Task Publish_requires_etag_and_limits_then_freezes_draft_for_business_readers()
    {
        var (admin, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (admin)
        {
            var (draft, etag) = await CreateDraftAsync(admin, marketId, commodityId,
                DateTimeOffset.UtcNow.AddMinutes(20));
            Assert.Equal((HttpStatusCode)428,
                (await admin.PostAsync($"/api/mrl-versions/{draft.Id}/publish", null)).StatusCode);
            var missingLimits = await PublishAsync(admin, draft.Id, etag);
            Assert.Equal(HttpStatusCode.Conflict, missingLimits.StatusCode);
            Assert.Equal("DRAFT", (await admin.GetFromJsonAsync<MrlVersionResponse>(
                $"/api/mrl-versions/{draft.Id}"))!.Status);

            await SeedValidLimitAsync(draft.Id);
            var published = await PublishAsync(admin, draft.Id, etag);
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            var result = (await published.Content.ReadFromJsonAsync<MrlVersionResponse>())!;
            Assert.Equal("PUBLISHED", result.Status);
            Assert.NotNull(result.PublishedBy);
            Assert.NotNull(result.PublishedAt);
            Assert.NotEqual(etag, published.Headers.ETag);
            Assert.Equal(HttpStatusCode.PreconditionFailed,
                (await PublishAsync(admin, draft.Id, etag)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await PublishAsync(admin, draft.Id, published.Headers.ETag!)).StatusCode);

            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, exportSession) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            var (purchaser, purchaseSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
            using var exportClient = factory.CreateRealCookieClient(exporter, exportSession);
            using var purchaseClient = factory.CreateRealCookieClient(purchaser, purchaseSession);
            Assert.Equal(HttpStatusCode.OK,
                (await exportClient.GetAsync($"/api/mrl-versions/{draft.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await PublishAsync(purchaseClient, draft.Id, published.Headers.ETag!)).StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(1, await db.CatalogEvents.CountAsync(x =>
                x.ResourceId == draft.Id && x.EventType == "MRL_VERSION_PUBLISHED"));
        }
    }

    [Fact]
    public async Task Publishing_successor_closes_open_predecessor_and_resolve_uses_half_open_boundaries()
    {
        var (admin, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (admin)
        {
            var (first, firstEtag) = await CreateDraftAsync(admin, marketId, commodityId,
                DateTimeOffset.UtcNow.AddMinutes(20));
            var (second, secondEtag) = await CreateDraftAsync(admin, marketId, commodityId,
                DateTimeOffset.UtcNow.AddDays(2));
            await SeedValidLimitAsync(first.Id);
            await SeedValidLimitAsync(second.Id);
            Assert.Equal(HttpStatusCode.OK, (await PublishAsync(admin, first.Id, firstEtag)).StatusCode);

            var beforeFirst = await admin.GetAsync(ResolveUrl(marketId, commodityId,
                first.EffectiveFrom.AddTicks(-10)));
            Assert.Equal(HttpStatusCode.NotFound, beforeFirst.StatusCode);
            var beforeSecond = await admin.GetFromJsonAsync<MrlVersionResponse>(
                ResolveUrl(marketId, commodityId, second.EffectiveFrom.AddTicks(-10)));
            Assert.Equal(first.Id, beforeSecond!.Id);

            var publishedSecond = await PublishAsync(admin, second.Id, secondEtag);
            Assert.Equal(HttpStatusCode.OK, publishedSecond.StatusCode);
            var closedFirst = await admin.GetFromJsonAsync<MrlVersionResponse>(
                $"/api/mrl-versions/{first.Id}");
            Assert.Equal(second.EffectiveFrom, closedFirst!.EffectiveTo);
            Assert.Equal(first.Id, (await admin.GetFromJsonAsync<MrlVersionResponse>(
                ResolveUrl(marketId, commodityId, second.EffectiveFrom.AddTicks(-10))))!.Id);
            Assert.Equal(second.Id, (await admin.GetFromJsonAsync<MrlVersionResponse>(
                ResolveUrl(marketId, commodityId, second.EffectiveFrom)))!.Id);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var closeEvent = await db.CatalogEvents.SingleAsync(x =>
                x.ResourceId == first.Id && x.EventType == "MRL_VERSION_CLOSED");
            var publishEvent = await db.CatalogEvents.SingleAsync(x =>
                x.ResourceId == second.Id && x.EventType == "MRL_VERSION_PUBLISHED");
            Assert.Equal(closeEvent.CorrelationId, publishEvent.CorrelationId);
            Assert.Contains(second.Id.ToString(), closeEvent.AfterState, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(closeEvent.BeforeState);
        }
    }

    [Fact]
    public async Task Publish_rejects_overlap_backdating_and_resolve_rejects_invalid_or_unauthorized_queries()
    {
        var (admin, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (admin)
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(20);
            var (first, firstEtag) = await CreateDraftAsync(admin, marketId, commodityId,
                start, start.AddDays(2));
            var (overlap, overlapEtag) = await CreateDraftAsync(admin, marketId, commodityId,
                start.AddDays(1), start.AddDays(3));
            var (backdated, backdatedEtag) = await CreateDraftAsync(admin, marketId, commodityId,
                DateTimeOffset.UtcNow.AddDays(-1));
            await SeedValidLimitAsync(first.Id);
            await SeedValidLimitAsync(overlap.Id);
            await SeedValidLimitAsync(backdated.Id);
            Assert.Equal(HttpStatusCode.OK, (await PublishAsync(admin, first.Id, firstEtag)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await PublishAsync(admin, overlap.Id, overlapEtag)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await PublishAsync(admin, backdated.Id, backdatedEtag)).StatusCode);
            Assert.Equal("DRAFT", (await admin.GetFromJsonAsync<MrlVersionResponse>(
                $"/api/mrl-versions/{overlap.Id}"))!.Status);

            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.GetAsync($"/api/mrl-versions/resolve?marketId=&commodityId={commodityId}&decisionAt=bad")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await admin.GetAsync($"/api/mrl-versions/resolve?marketId={marketId}&commodityId={commodityId}&decisionAt=2027-01-01T00:00:00")).StatusCode);
            var customer = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
            var (exporter, exportSession) = await factory.SeedUserAsync(Roles.ExportStaff, customer.Id);
            var (purchaser, purchaseSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, customer.Id);
            using var exportClient = factory.CreateRealCookieClient(exporter, exportSession);
            using var purchaseClient = factory.CreateRealCookieClient(purchaser, purchaseSession);
            Assert.Equal(first.Id, (await exportClient.GetFromJsonAsync<MrlVersionResponse>(
                ResolveUrl(marketId, commodityId, first.EffectiveFrom)))!.Id);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await purchaseClient.GetAsync(ResolveUrl(marketId, commodityId, first.EffectiveFrom))).StatusCode);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var row = await db.MrlVersions.SingleAsync(x => x.Id == first.Id);
                row.PublishedAt = first.EffectiveFrom.AddHours(1);
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.NotFound,
                (await admin.GetAsync(ResolveUrl(marketId, commodityId, first.EffectiveFrom))).StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_publications_for_same_market_do_not_overlap()
    {
        var (admin, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (admin)
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(20);
            var (first, firstEtag) = await CreateDraftAsync(admin, marketId, commodityId, start);
            var (second, secondEtag) = await CreateDraftAsync(admin, marketId, commodityId, start);
            await SeedValidLimitAsync(first.Id);
            await SeedValidLimitAsync(second.Id);
            var results = await Task.WhenAll(
                PublishAsync(admin, first.Id, firstEtag),
                PublishAsync(admin, second.Id, secondEtag));
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.Equal(1, await db.MrlVersions.CountAsync(x => x.MarketId == marketId &&
                x.CommodityId == commodityId && x.Status == "PUBLISHED"));
        }
    }

    [Fact]
    public async Task Resolve_reports_ambiguity_instead_of_guessing_from_overlapping_published_rows()
    {
        var (admin, marketId, commodityId) = await CreateAdminAndCatalogsAsync();
        using (admin)
        {
            var start = DateTimeOffset.UtcNow.AddMinutes(20);
            var (first, _) = await CreateDraftAsync(admin, marketId, commodityId, start);
            var (second, _) = await CreateDraftAsync(admin, marketId, commodityId, start);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var rows = await db.MrlVersions.Where(x => x.Id == first.Id || x.Id == second.Id).ToListAsync();
                var actorId = (await db.CatalogEvents.SingleAsync(x => x.ResourceId == first.Id)).ActorId;
                foreach (var row in rows)
                {
                    row.Status = "PUBLISHED";
                    row.PublishedBy = actorId;
                    row.PublishedAt = DateTimeOffset.UtcNow;
                }
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Conflict,
                (await admin.GetAsync(ResolveUrl(marketId, commodityId, first.EffectiveFrom))).StatusCode);
        }
    }

    private async Task<(MrlVersionResponse Version, EntityTagHeaderValue Etag)> CreateDraftAsync(
        HttpClient client, Guid marketId, Guid commodityId, DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo = null)
    {
        var response = await client.PostAsJsonAsync("/api/mrl-versions", new
        { marketId, commodityId, effectiveFrom, effectiveTo, sourceRef = "Verified test fixture" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<MrlVersionResponse>())!, response.Headers.ETag!);
    }

    private async Task SeedValidLimitAsync(Guid versionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (!await db.MeasurementUnits.AnyAsync(x => x.Code == "MG/KG"))
            db.MeasurementUnits.Add(new MeasurementUnit
            { Id = Guid.NewGuid(), Code = "MG/KG", Name = "Milligram per kilogram", QuantityKind = "RESIDUE_CONCENTRATION" });
        var substanceId = Guid.NewGuid();
        db.ActiveSubstances.Add(new ActiveSubstance
        { Id = substanceId, Code = $"S-{substanceId:N}", Name = "Test substance" });
        db.MrlLimits.Add(new MrlLimit
        { Id = Guid.NewGuid(), MrlVersionId = versionId, SubstanceId = substanceId,
            LimitValue = 0.01m, Unit = "mg/kg" });
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> PublishAsync(HttpClient client, Guid versionId,
        EntityTagHeaderValue etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/mrl-versions/{versionId}/publish");
        request.Headers.IfMatch.Add(etag);
        return await client.SendAsync(request);
    }

    private static string ResolveUrl(Guid marketId, Guid commodityId, DateTimeOffset decisionAt) =>
        $"/api/mrl-versions/resolve?marketId={marketId}&commodityId={commodityId}" +
        $"&decisionAt={Uri.EscapeDataString(decisionAt.ToString("O"))}";
}
