using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Exports;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ExportOrderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Header_edits_preserve_allocation_and_replay_without_overwriting_newer_changes()
    {
        var (client, order, market, commodity) = await EditableOrderAsync();
        using var writer = client;
        var stockBefore = await StockSnapshotAsync();
        object Body(Guid operation, string mass) => new
        {
            operationId = operation, marketId = market, commodityId = commodity,
            requiredMass = mass, unit = "T", shippingFrom = "2026-11-11", shippingTo = "2026-11-21"
        };
        var route = $"/api/export-orders/{order.Id}";
        var tooSmall = await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "23"));
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
        Assert.True((await tooSmall.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("requiredMass", out _));
        var operation = Guid.NewGuid();
        var body = Body(operation, "50");
        var updated = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = (await updated.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        Assert.Equal("50000", saved.RequiredMassKg);
        Assert.Equal("50", saved.OriginalMass);
        Assert.Equal("T", saved.OriginalUnit);
        Assert.Equal("24000", saved.AllocatedMassKg);
        Assert.Equal("26000", saved.RemainingMassKg);
        Assert.Equal(new DateOnly(2026, 11, 11), saved.ShippingFrom);
        Assert.Equal("\"2\"", updated.Headers.ETag!.ToString());
        var newer = await PatchAsync(writer, route, 2, Body(Guid.NewGuid(), "51"));
        Assert.Equal(HttpStatusCode.OK, newer.StatusCode);
        var replay = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("50000", (await replay.Content.ReadFromJsonAsync<ExportOrderDto>())!.RequiredMassKg);
        Assert.Equal(updated.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync(writer, route, 1, Body(operation, "52"))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "53"))).StatusCode);
        var detail = (await writer.GetFromJsonAsync<ExportOrderDto>(route))!;
        Assert.Equal("51000", detail.RequiredMassKg);
        Assert.Equal(3, detail.Version);
        Assert.Equal(1, detail.SpecificationVersion);
        var history = (await writer.GetFromJsonAsync<ExportPage<ExportEventDto>>(route + "/history"))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(2, history.Items.Count(x => x.Action == "ORDER_UPDATED"));
        Assert.Equal(stockBefore, await StockSnapshotAsync());
    }

    [Fact]
    public async Task Target_edits_reject_overallocation_and_stale_versions_and_replay_exactly_once()
    {
        var (client, order, _, _) = await EditableOrderAsync();
        using var writer = client;
        var stockBefore = await StockSnapshotAsync();
        var containers = (await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers"))!;
        var container = Assert.Single(containers.Items);
        var route = $"/api/export-containers/{container.Id}";
        object Body(Guid operation, string mass) => new { operationId = operation, targetMass = mass, unit = "T" };
        var over = await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "49"));
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.True((await over.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("targetMass", out _));
        var operation = Guid.NewGuid();
        var body = Body(operation, "30");
        var updated = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = (await updated.Content.ReadFromJsonAsync<ExportContainerDto>())!;
        Assert.Equal("30000", saved.TargetMassKg);
        Assert.Equal(container.InternalCode, saved.InternalCode);
        Assert.Equal("30", saved.OriginalMass);
        Assert.Equal("T", saved.OriginalUnit);
        Assert.Equal("\"2\"", updated.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(writer, route, 2, Body(Guid.NewGuid(), "31"))).StatusCode);
        var replay = await PatchAsync(writer, route, 1, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("30000", (await replay.Content.ReadFromJsonAsync<ExportContainerDto>())!.TargetMassKg);
        Assert.Equal(updated.Headers.ETag, replay.Headers.ETag);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync(writer, route, 1, Body(operation, "32"))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await PatchAsync(writer, route, 1, Body(Guid.NewGuid(), "33"))).StatusCode);
        var detail = (await writer.GetFromJsonAsync<ExportContainerDto>(route))!;
        Assert.Equal("31000", detail.TargetMassKg);
        Assert.Equal(3, detail.Version);
        var currentOrder = (await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}"))!;
        Assert.Equal("31000", currentOrder.AllocatedMassKg);
        Assert.Equal("17000", currentOrder.RemainingMassKg);
        Assert.Equal(3, currentOrder.Version);
        var history = (await writer.GetFromJsonAsync<ExportPage<ExportEventDto>>($"/api/export-orders/{order.Id}/history"))!;
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(2, history.Items.Count(x => x.Action == "CONTAINER_UPDATED"));
        Assert.Equal(stockBefore, await StockSnapshotAsync());
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string route, int version, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, route) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(request);
    }

    private async Task<(HttpClient Client, ExportOrderDto Order, Guid Market, Guid Commodity)> EditableOrderAsync()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        var writer = factory.CreateRealCookieClient(staff, session);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "48", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (writer, (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!, market, commodity);
    }

    private async Task<(long Positions, decimal Mass, long Postings)> StockSnapshotAsync()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM ledger.positions),
                   (SELECT coalesce(sum(quantity_kg), 0) FROM ledger.positions),
                   (SELECT count(*) FROM ledger.postings)
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt64(0), reader.GetDecimal(1), reader.GetInt64(2));
    }

    [Fact]
    public async Task Specification_bounds_keep_twelve_decimal_places_in_reads()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        using var writer = factory.CreateRealCookieClient(staff, session);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "0.001", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = "0.000000000001", maxValue = "0.0000001" } },
            containers = new[] { new { targetMass = "0.001", unit = "T" } }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        Assert.Equal("0.000000000001", order.Limits.Single().MinValue);
        Assert.Equal("0.0000001", order.Limits.Single().MaxValue);
        var detail = await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}");
        Assert.Equal(order.Limits, detail!.Limits);
    }

    [Fact]
    public async Task Concurrent_target_additions_cannot_overallocate_an_order()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        using var writer = factory.CreateRealCookieClient(staff, session);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var (market, commodity, attribute) = await CatalogAsync();
        var created = await writer.PostAsJsonAsync("/api/export-orders", new
        {
            operationId = Guid.NewGuid(), marketId = market, commodityId = commodity,
            requiredMass = "48", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        HttpRequestMessage Addition()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/containers")
                { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), containers = new[] { new { targetMass = "24", unit = "T" } } }) };
            request.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
            return request;
        }
        using var first = Addition(); using var second = Addition();
        var results = await Task.WhenAll(writer.SendAsync(first), writer.SendAsync(second));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.PreconditionFailed));
        var detail = await writer.GetFromJsonAsync<ExportOrderDto>($"/api/export-orders/{order.Id}");
        Assert.Equal("48000", detail!.AllocatedMassKg);
        Assert.Equal("0", detail.RemainingMassKg);
        var targets = await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers?page=1&pageSize=1");
        Assert.Equal(2, targets!.TotalCount);
        Assert.Single(targets.Items);
        Assert.True(targets.HasMore);
    }

    [Fact]
    public async Task Create_replay_scope_and_allocation_guard_use_real_postgres()
    {
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var otherCompany = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (staff, session) = await factory.SeedUserAsync(Roles.ExportStaff, company.Id);
        var (manager, managerSession) = await factory.SeedUserAsync(Roles.CustomerManager, company.Id);
        var (other, otherSession) = await factory.SeedUserAsync(Roles.ExportStaff, otherCompany.Id);
        using var writer = factory.CreateRealCookieClient(staff, session);
        using var reader = factory.CreateRealCookieClient(manager, managerSession);
        using var outsider = factory.CreateRealCookieClient(other, otherSession);
        var csrf = await writer.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        writer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.True((await writer.GetFromJsonAsync<JsonElement>("/api/export-availability")).GetProperty("ready").GetBoolean());
        var (market, commodity, attribute) = await CatalogAsync();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        async Task<(long Positions, decimal Mass, long Postings)> StockAsync()
        {
            await using var check = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM ledger.positions),
                       (SELECT coalesce(sum(quantity_kg), 0) FROM ledger.positions),
                       (SELECT count(*) FROM ledger.postings)
                """, owner);
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetInt64(0), reader.GetDecimal(1), reader.GetInt64(2));
        }
        var stockBefore = await StockAsync();
        var operation = Guid.NewGuid();
        var payload = new
        {
            operationId = operation, marketId = market, commodityId = commodity,
            requiredMass = "48", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" }, new { targetMass = "24", unit = "T" } }
        };
        var created = await writer.PostAsJsonAsync("/api/export-orders", payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = (await created.Content.ReadFromJsonAsync<ExportOrderDto>())!;
        Assert.Matches(@"^EXP-\d{6}-\d{6,}$", order.Code);
        Assert.Equal("48000", order.RequiredMassKg);
        Assert.Equal("0", order.RemainingMassKg);
        var targets = await writer.GetFromJsonAsync<ExportPage<ExportContainerDto>>($"/api/export-orders/{order.Id}/containers");
        Assert.Equal(2, targets!.TotalCount);
        Assert.All(targets.Items, x => Assert.Matches(@"^CTN-\d{8}-\d{6,}$", x.InternalCode));
        var replay = await writer.PostAsJsonAsync("/api/export-orders", payload);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(order.Id, (await replay.Content.ReadFromJsonAsync<ExportOrderDto>())!.Id);
        var changed = new
        {
            operationId = operation, marketId = market, commodityId = commodity,
            requiredMass = "49", unit = "T", shippingFrom = "2026-11-10", shippingTo = "2026-11-20",
            limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "14" } },
            containers = new[] { new { targetMass = "24", unit = "T" } }
        };
        Assert.Equal(HttpStatusCode.Conflict, (await writer.PostAsJsonAsync("/api/export-orders", changed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/export-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/export-orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/export-orders", payload)).StatusCode);
        var add = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/containers")
            { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), containers = new[] { new { targetMass = "1", unit = "T" } } }) };
        add.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await writer.SendAsync(add)).StatusCode);
        var specOperation = Guid.NewGuid();
        var spec = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/specifications")
            { Content = JsonContent.Create(new { operationId = specOperation, limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "13" } } }) };
        spec.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        var updated = await writer.SendAsync(spec);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await updated.Content.ReadFromJsonAsync<ExportOrderDto>())!.SpecificationVersion);
        var specPage = await writer.GetFromJsonAsync<ExportPage<ExportSpecificationDto>>(
            $"/api/export-orders/{order.Id}/specifications?page=1&pageSize=1");
        Assert.Equal(2, specPage!.TotalCount);
        Assert.True(specPage.HasMore);
        Assert.Equal(2, Assert.Single(specPage.Items).VersionNo);
        var historyPage = await reader.GetFromJsonAsync<ExportPage<ExportEventDto>>(
            $"/api/export-orders/{order.Id}/history?page=1&pageSize=1");
        Assert.NotNull(historyPage);
        Assert.Equal(staff.DisplayName, Assert.Single(historyPage.Items).ActorName);
        var oldReplay = await writer.PostAsJsonAsync("/api/export-orders", payload);
        Assert.Equal(HttpStatusCode.Created, oldReplay.StatusCode);
        Assert.Equal(1, (await oldReplay.Content.ReadFromJsonAsync<ExportOrderDto>())!.SpecificationVersion);
        var stale = new HttpRequestMessage(HttpMethod.Post, $"/api/export-orders/{order.Id}/specifications")
            { Content = JsonContent.Create(new { operationId = Guid.NewGuid(), limits = new[] { new { attributeId = attribute, minValue = (string?)null, maxValue = "12" } } }) };
        stale.Headers.TryAddWithoutValidation("If-Match", created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await writer.SendAsync(stale)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<ExportDbContext>();
        Assert.Equal(1, await exports.Orders.CountAsync(x => x.CompanyId == company.Id));
        Assert.Equal(2, await exports.Containers.CountAsync(x => x.OrderId == order.Id));
        Assert.Equal(2, await exports.Specifications.CountAsync(x => x.OrderId == order.Id));
        Assert.Equal(stockBefore, await StockAsync());
        await using var rewrite = new NpgsqlCommand("""
            UPDATE exports.specification_limits SET max_value=99
            WHERE specification_id IN (SELECT id FROM exports.order_specifications WHERE order_id=$1)
            """, owner);
        rewrite.Parameters.AddWithValue(order.Id);
        var violation = await Assert.ThrowsAsync<PostgresException>(() => rewrite.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
    }

    private async Task<(Guid Market, Guid Commodity, Guid Attribute)> CatalogAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var market = new Market { Id = Guid.NewGuid(), Code = "TEST-MKT-" + suffix, Name = "Test market" };
        var commodity = new Commodity { Id = Guid.NewGuid(), Code = "TEST-RICE-" + suffix, Name = "Test rice" };
        var attribute = new QualityAttribute { Id = Guid.NewGuid(), Code = "TEST-MOIST-" + suffix, Name = "Moisture", Unit = "%" };
        db.AddRange(market, commodity, attribute);
        if (!await db.MeasurementUnits.AnyAsync(x => x.Code == "T"))
            db.MeasurementUnits.Add(new MeasurementUnit { Id = Guid.NewGuid(), Code = "T", Name = "Tonne", QuantityKind = "MASS" });
        await db.SaveChangesAsync();
        return (market.Id, commodity.Id, attribute.Id);
    }
}
