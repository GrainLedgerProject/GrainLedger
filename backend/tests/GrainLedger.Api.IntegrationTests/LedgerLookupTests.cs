using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerLookupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Catalog_and_mapping_write_roll_back_together_when_provisioning_is_denied()
    {
        using var s = await CreateAsync(factory);
        var id = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var ledger = scope.ServiceProvider.GetRequiredService<ICommodityLedgerPort>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Commodities.Add(new Commodity { Id = id, Code = $"RB-{Guid.NewGuid():N}", Name = "Must roll back" });
            await db.SaveChangesAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => ledger.ProvisionAsync(
                (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction(),
                s.Accounts["operator"].User.Id, default));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
            await transaction.RollbackAsync();
        }
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Commodities.AnyAsync(x => x.Id == id));
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var mappings = new NpgsqlCommand("SELECT count(*) FROM ledger.catalog_commodities WHERE catalog_commodity_id=$1", runtime);
        mappings.Parameters.AddWithValue(id);
        Assert.Equal(0L, await mappings.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Owners_follow_persisted_facility_lot_and_operation_scope_not_global_customer_list()
    {
        using var s = await CreateAsync(factory);
        var unrelated = await factory.SeedOrganizationAsync(OrganizationKind.Customer, "Unrelated company");
        var operationOwner = await factory.SeedOrganizationAsync(OrganizationKind.Customer, "Operation-only company");
        Assert.Empty((await GetAsync(s.Operator, "/api/ledger/lookups")).GetProperty("owners").EnumerateArray());
        Assert.Equal(s.OwnerId, Assert.Single((await GetAsync(s.Purchaser, "/api/ledger/lookups"))
            .GetProperty("owners").EnumerateArray()).GetProperty("id").GetGuid());
        await s.ReceiptAsync("10");
        await s.ForeignReceiptAsync("8", s.ForeignLocationId);
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                INSERT INTO ledger.operations(id,owner_id,facility_id,kind,occurred_at,equipment_id,content,version)
                VALUES(gen_random_uuid(),$1,$2,'DRYING',now(),$3,'{}'::jsonb,gen_random_uuid())
                """, owner);
            seed.Parameters.AddWithValue(operationOwner.Id);
            seed.Parameters.AddWithValue(s.FacilityId);
            seed.Parameters.AddWithValue(s.EquipmentId);
            await seed.ExecuteNonQueryAsync();
        }
        foreach (var client in new[] { s.Operator, s.Manager })
        {
            foreach (var route in new[] { "/api/ledger/lookups", "/api/ledger/setup" })
            {
                var owners = (await GetAsync(client, route)).GetProperty("owners").EnumerateArray().ToArray();
                Assert.Equal(2, owners.Length);
                Assert.Contains(owners, x => x.GetProperty("id").GetGuid() == s.OwnerId);
                Assert.Contains(owners, x => x.GetProperty("id").GetGuid() == operationOwner.Id &&
                    x.GetProperty("name").GetString() == operationOwner.Name);
                Assert.DoesNotContain(owners, x => x.GetProperty("id").GetGuid() == s.ForeignOwnerId ||
                    x.GetProperty("id").GetGuid() == unrelated.Id);
            }
        }
        foreach (var client in new[] { s.Purchaser, s.Customer })
            Assert.Equal(s.OwnerId, Assert.Single((await GetAsync(client, "/api/ledger/lookups"))
                .GetProperty("owners").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(s.ForeignOwnerId, Assert.Single((await GetAsync(s.ForeignCustomer, "/api/ledger/lookups"))
            .GetProperty("owners").EnumerateArray()).GetProperty("id").GetGuid());
        var (unassigned, session) = await factory.SeedUserAsync(Roles.MillOperator, s.Accounts["operator"].User.OrganizationId);
        using var unassignedClient = factory.CreateRealCookieClient(unassigned, session);
        Assert.Empty((await GetAsync(unassignedClient, "/api/ledger/lookups")).GetProperty("owners").EnumerateArray());
    }

    [Fact]
    public async Task Catalog_write_provisions_all_rows_processing_uses_mapping_and_rename_preserves_stock_history()
    {
        using var s = await CreateAsync(factory);
        var beforeWriteId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Commodities.Add(new Commodity { Id = beforeWriteId, Code = $"PRE-{Guid.NewGuid():N}", Name = "Canonical blend" });
            await db.SaveChangesAsync();
        }
        var lookups = await GetAsync(s.Operator, "/api/ledger/lookups");
        Assert.DoesNotContain(lookups.GetProperty("commodities").EnumerateArray(), x =>
            x.GetProperty("catalogId").ValueKind == JsonValueKind.String && x.GetProperty("catalogId").GetGuid() == beforeWriteId);
        var catalogCommodity = await SendAsync(s.Admin, HttpMethod.Post, "/api/commodities",
            new { code = $"BL-{Guid.NewGuid():N}", name = "Canonical blend" });
        var catalogId = catalogCommodity.GetProperty("id").GetGuid();
        lookups = await GetAsync(s.Operator, "/api/ledger/lookups");
        var mapped = lookups.GetProperty("commodities").EnumerateArray().Single(x =>
            x.GetProperty("catalogId").ValueKind == JsonValueKind.String && x.GetProperty("catalogId").GetGuid() == catalogId);
        var ledgerId = mapped.GetProperty("id").GetGuid();
        Assert.NotEqual(catalogId, ledgerId);
        Assert.Equal("Canonical blend", mapped.GetProperty("name").GetString());
        Assert.Equal(catalogCommodity.GetProperty("code").GetString(), mapped.GetProperty("code").GetString());
        Assert.Contains(lookups.GetProperty("commodities").EnumerateArray(), x =>
            x.GetProperty("catalogId").ValueKind == JsonValueKind.String && x.GetProperty("catalogId").GetGuid() == beforeWriteId);
        var receipt = await s.ReceiptAsync("20");
        var source = Lots(receipt)[0];
        Assert.Equal(s.CommodityId, (await s.LotAsync(source)).GetProperty("commodityId").GetGuid());
        var paddy = (await GetAsync(s.Operator, "/api/ledger/lookups")).GetProperty("commodities").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == s.CommodityId);
        Assert.Equal("PADDY", paddy.GetProperty("code").GetString());
        Assert.Equal("Paddy", paddy.GetProperty("name").GetString());
        var dried = await s.CommitAsync(await s.DraftAsync("DRYING", [(source, "20")], [("DRIED_PADDY", "20")]));
        var milled = await s.CommitAsync(await s.DraftAsync("MILLING", [(Output(dried), "20")], [("HEAD_RICE", "12"), ("BROKEN_RICE", "8")]));
        await SendAsync(s.Admin, HttpMethod.Post, "/api/ledger/compatibilities", new
        {
            id = Guid.NewGuid(), facilityId = s.FacilityId,
            inputProducts = new[] { $"{s.CommodityId}:HEAD_RICE", $"{s.CommodityId}:BROKEN_RICE" },
            outputCommodityId = ledgerId, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), evidenceId = s.EvidenceId
        });
        var operationId = Guid.NewGuid();
        var body = JsonSerializer.SerializeToNode(s.OperationBody(operationId, "BLENDING",
            [(Output(milled), "12"), (Output(milled, 1), "8")], [("BLENDED_RICE", "20")]))!;
        body["outputs"]![0]!["commodityId"] = ledgerId;
        var draft = await SendAsync(s.Operator, HttpMethod.Post, "/api/ledger/operations", body);
        var blend = await s.CommitAsync(draft);
        var output = Output(blend);
        var historyBefore = (await GetAsync(s.Customer, $"/api/ledger/history?lotId={output}")).GetRawText();
        var lotBefore = (await s.LotAsync(output)).GetRawText();
        using var detail = await s.Admin.GetAsync($"/api/commodities/{catalogId}");
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/commodities/{catalogId}")
        {
            Content = JsonContent.Create(new { name = "Renamed canonical blend" })
        };
        patch.Headers.TryAddWithoutValidation("If-Match", detail.Headers.ETag!.ToString());
        using var updated = await s.Admin.SendAsync(patch);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var renamed = (await GetAsync(s.Operator, "/api/ledger/lookups")).GetProperty("commodities").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == ledgerId);
        Assert.Equal(catalogId, renamed.GetProperty("catalogId").GetGuid());
        Assert.Equal("Renamed canonical blend", renamed.GetProperty("name").GetString());
        Assert.Equal(lotBefore, (await s.LotAsync(output)).GetRawText());
        Assert.Equal(historyBefore, (await GetAsync(s.Customer, $"/api/ledger/history?lotId={output}")).GetRawText());
        await RejectedAsync(s.Admin, HttpMethod.Post, "/api/ledger/commodities", new { id = Guid.NewGuid(), name = "Duplicate" }, HttpStatusCode.NotFound);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        foreach (var actor in new[] { s.Accounts["operator"].User.Id, s.Accounts["purchaser"].User.Id })
        {
            await using var denied = new NpgsqlCommand("SELECT ledger.provision_catalog_commodities($1)", runtime);
            denied.Parameters.AddWithValue(actor);
            var error = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        await using var removed = new NpgsqlCommand("SELECT ledger.command($1,'commodities',$2::jsonb)", runtime);
        removed.Parameters.AddWithValue(s.Accounts["admin"].User.Id);
        removed.Parameters.AddWithValue(JsonSerializer.Serialize(new { id = Guid.NewGuid(), name = "Duplicate" }));
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => removed.ExecuteNonQueryAsync())).SqlState);
        await using var internalDenied = new NpgsqlCommand("SELECT ledger.provision_catalog_commodity($1)", runtime);
        internalDenied.Parameters.AddWithValue(catalogId);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => internalDenied.ExecuteNonQueryAsync())).SqlState);
    }
}
