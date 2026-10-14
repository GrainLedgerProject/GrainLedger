using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ActiveSubstanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task AddCsrfAsync(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Admin_creates_and_updates_with_etag_and_audit_customer_reads_only()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        await AddCsrfAsync(adminClient);
        await AddCsrfAsync(purchaserClient);

        var created = await adminClient.PostAsJsonAsync("/api/active-substances", new { code = " abc-01 ", name = " Original name " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!;
        Assert.Equal("ABC-01", body.Code);
        Assert.Equal("Original name", body.Name);
        var etag = created.Headers.ETag!.ToString();
        Assert.Equal($"/api/active-substances/{body.Id}", created.Headers.Location?.OriginalString);

        var read = await purchaserClient.GetAsync($"/api/active-substances/{body.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(etag, read.Headers.ETag?.ToString());
        var list = await purchaserClient.GetFromJsonAsync<ActiveSubstanceListResponse>("/api/active-substances?q=ABC-01");
        Assert.Contains(list!.Items, x => x.Id == body.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await purchaserClient.PostAsJsonAsync("/api/active-substances", new { code = "OTHER", name = "Other" })).StatusCode);

        using var missingMatch = new HttpRequestMessage(HttpMethod.Patch, $"/api/active-substances/{body.Id}")
            { Content = JsonContent.Create(new { name = "Corrected name" }) };
        Assert.Equal((HttpStatusCode)428, (await adminClient.SendAsync(missingMatch)).StatusCode);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/active-substances/{body.Id}")
            { Content = JsonContent.Create(new { name = "Corrected name" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", etag);
        var updated = await adminClient.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.NotEqual(etag, updated.Headers.ETag?.ToString());
        Assert.Equal("Corrected name", (await updated.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Name);

        using var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/active-substances/{body.Id}")
            { Content = JsonContent.Create(new { name = "Stale name" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == body.Id));
        Assert.Equal("Corrected name", (await db.ActiveSubstances.SingleAsync(x => x.Id == body.Id)).Name);
    }

    [Fact]
    public async Task Invalid_or_duplicate_code_and_invalid_page_are_rejected_without_audit()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        await AddCsrfAsync(client);
        var code = $"A-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/active-substances", new { code = " ", name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/active-substances", new { code, name = "Test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/active-substances", new { code = code.ToLowerInvariant(), name = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/active-substances?page=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/active-substances?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Admin_deletes_unused_substance_with_etag_and_keeps_audit()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (admin, adminSession) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        var (purchaser, purchaserSession) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        using var adminClient = factory.CreateRealCookieClient(admin, adminSession);
        using var purchaserClient = factory.CreateRealCookieClient(purchaser, purchaserSession);
        await AddCsrfAsync(adminClient);
        await AddCsrfAsync(purchaserClient);
        var created = await adminClient.PostAsJsonAsync("/api/active-substances",
            new { code = $"DEL-{Guid.NewGuid():N}", name = "Temporary" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
        var url = $"/api/active-substances/{id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await purchaserClient.DeleteAsync(url)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await adminClient.DeleteAsync(url)).StatusCode);
        using var stale = new HttpRequestMessage(HttpMethod.Delete, url);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await adminClient.SendAsync(stale)).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, url);
        delete.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.SendAsync(delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync(url)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(0, await db.ActiveSubstances.CountAsync(x => x.Id == id));
        Assert.Equal(2, await db.CatalogEvents.CountAsync(x => x.ResourceId == id));
        Assert.Equal("ACTIVE_SUBSTANCE_DELETED",
            (await db.CatalogEvents.SingleAsync(x => x.ResourceId == id && x.EventType == "ACTIVE_SUBSTANCE_DELETED")).EventType);
    }

    [Fact]
    public async Task Referenced_substance_cannot_be_deleted_and_has_no_delete_event()
    {
        var mill = await factory.SeedOrganizationAsync(OrganizationKind.Mill);
        var (admin, session) = await factory.SeedUserAsync(Roles.Administrator, mill.Id);
        using var client = factory.CreateRealCookieClient(admin, session);
        await AddCsrfAsync(client);
        var created = await client.PostAsJsonAsync("/api/active-substances",
            new { code = $"USED-{Guid.NewGuid():N}", name = "Used substance" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<ActiveSubstanceResponse>())!.Id;
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var setup = new NpgsqlCommand("""
                CREATE TABLE IF NOT EXISTS catalog.test_active_substance_refs (
                    id uuid PRIMARY KEY,
                    substance_id uuid NOT NULL REFERENCES catalog.active_substances(id)
                );
                """, owner);
            await setup.ExecuteNonQueryAsync();
            await using var reference = new NpgsqlCommand("INSERT INTO catalog.test_active_substance_refs(id,substance_id) VALUES($1,$2)", owner);
            reference.Parameters.AddWithValue(Guid.NewGuid());
            reference.Parameters.AddWithValue(id);
            await reference.ExecuteNonQueryAsync();
        }
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/active-substances/{id}");
        delete.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(delete)).StatusCode);
        using var verify = factory.Services.CreateScope();
        var catalog = verify.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Equal(1, await catalog.ActiveSubstances.CountAsync(x => x.Id == id));
        Assert.Equal(0, await catalog.CatalogEvents.CountAsync(x => x.ResourceId == id && x.EventType == "ACTIVE_SUBSTANCE_DELETED"));
    }
}
