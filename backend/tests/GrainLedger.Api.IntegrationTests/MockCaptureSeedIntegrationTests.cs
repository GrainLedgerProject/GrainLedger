using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.MockCapture;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MockCaptureSeedIntegrationTests
{
    private const string Password = "SeedOnly!Password123";
    private const string ArtifactRowsSql = """
        SELECT (SELECT count(*) FROM identity.organizations) + (SELECT count(*) FROM identity.users)
          + (SELECT count(*) FROM ledger.facilities) + (SELECT count(*) FROM ledger.locations)
          + (SELECT count(*) FROM ledger.equipment) + (SELECT count(*) FROM ledger.commodities)
          + (SELECT count(*) FROM ledger.assignments) + (SELECT count(*) FROM ledger.evidence)
          + (SELECT count(*) FROM ledger.policies) + (SELECT count(*) FROM ledger.compatibilities)
          + (SELECT count(*) FROM ledger.lots) + (SELECT count(*) FROM ledger.positions)
          + (SELECT count(*) FROM ledger.operations) + (SELECT count(*) FROM ledger.events)
          + (SELECT count(*) FROM ledger.postings) + (SELECT count(*) FROM ledger.contributions)
          + (SELECT count(*) FROM ledger.holds) + (SELECT count(*) FROM ledger.corrections)
          + (SELECT count(*) FROM ledger.receiving_blocks) + (SELECT count(*) FROM ledger.receipt_reversals)
          + (SELECT count(*) FROM ledger.notifications) + (SELECT count(*) FROM ledger.receipt_correction_blocks)
          + (SELECT count(*) FROM capture_mock.farmers) + (SELECT count(*) FROM capture_mock.plots)
          + (SELECT count(*) FROM capture_mock.seasons) + (SELECT count(*) FROM capture_mock.plot_seasons)
          + (SELECT count(*) FROM capture_mock.receipts) + (SELECT count(*) FROM capture_mock.receipt_lines)
          + (SELECT count(*) FROM capture_mock.receipt_lots) + (SELECT count(*) FROM capture_mock.arrivals)
          + (SELECT count(*) FROM capture_mock.arrival_corrections)
          + (SELECT count(*) FROM capture_mock.arrival_investigations)
          + (SELECT count(*) FROM capture_mock.receipt_corrections)
          + (SELECT count(*) FROM capture_mock.commands)
        """;
    private const string DomainRowsExceptFarmerSql = """
        SELECT (SELECT count(*) FROM ledger.facilities) + (SELECT count(*) FROM ledger.locations)
          + (SELECT count(*) FROM ledger.equipment) + (SELECT count(*) FROM ledger.commodities)
          + (SELECT count(*) FROM ledger.assignments) + (SELECT count(*) FROM ledger.evidence)
          + (SELECT count(*) FROM ledger.policies) + (SELECT count(*) FROM ledger.compatibilities)
          + (SELECT count(*) FROM ledger.lots) + (SELECT count(*) FROM ledger.positions)
          + (SELECT count(*) FROM ledger.operations) + (SELECT count(*) FROM ledger.events)
          + (SELECT count(*) FROM ledger.postings) + (SELECT count(*) FROM ledger.contributions)
          + (SELECT count(*) FROM ledger.holds) + (SELECT count(*) FROM ledger.corrections)
          + (SELECT count(*) FROM ledger.receiving_blocks) + (SELECT count(*) FROM ledger.receipt_reversals)
          + (SELECT count(*) FROM ledger.notifications) + (SELECT count(*) FROM ledger.receipt_correction_blocks)
          + (SELECT count(*) FROM capture_mock.plots) + (SELECT count(*) FROM capture_mock.seasons)
          + (SELECT count(*) FROM capture_mock.plot_seasons) + (SELECT count(*) FROM capture_mock.receipts)
          + (SELECT count(*) FROM capture_mock.receipt_lines) + (SELECT count(*) FROM capture_mock.receipt_lots)
          + (SELECT count(*) FROM capture_mock.arrivals) + (SELECT count(*) FROM capture_mock.arrival_corrections)
          + (SELECT count(*) FROM capture_mock.arrival_investigations)
          + (SELECT count(*) FROM capture_mock.receipt_corrections) + (SELECT count(*) FROM capture_mock.commands)
        """;
    private const string SourceLineageSql = """
        SELECT count(*) FROM capture_mock.receipts r
        JOIN capture_mock.receipt_lines rl ON rl.receipt_id=r.id AND rl.owner_id=r.owner_id
            AND rl.farmer_id=r.farmer_id AND rl.season_id=r.season_id
        JOIN capture_mock.plot_seasons ps ON ps.id=rl.plot_season_id AND ps.owner_id=r.owner_id
            AND ps.farmer_id=r.farmer_id AND ps.season_id=r.season_id
        JOIN capture_mock.plots p ON p.id=ps.plot_id AND p.owner_id=r.owner_id AND p.farmer_id=r.farmer_id
        JOIN capture_mock.farmers f ON f.id=p.farmer_id AND f.owner_id=r.owner_id
        JOIN capture_mock.seasons s ON s.id=ps.season_id AND s.owner_id=r.owner_id
        """;
    private const string CommittedLotLineageSql = """
        SELECT count(*) FROM capture_mock.receipts r
        JOIN capture_mock.receipt_lines rl ON rl.receipt_id=r.id
        JOIN capture_mock.receipt_lots link ON link.receipt_line_id=rl.id
        JOIN ledger.lots lot ON lot.id=link.lot_id AND lot.receipt_id=r.id
            AND lot.receipt_line_id=rl.id AND lot.owner_id=r.owner_id AND lot.commodity_id=rl.commodity_id
        WHERE r.status='COMMITTED'
        """;

    [Fact]
    public async Task Development_seed_is_atomic_repeatable_and_readable_by_assigned_operator()
    {
        var factory = new ApiFactory();
        await factory.InitializeAsync();
        try
        {
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MockCaptureSeed.RunAsync(factory.OwnerConnectionString, Environments.Production));
            Assert.Equal(0L, await ScalarAsync<long>(owner, ArtifactRowsSql));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MockCaptureSeed.RunAsync(factory.OwnerConnectionString, Environments.Development));
            Assert.Equal(0L, await ScalarAsync<long>(owner, ArtifactRowsSql));

            Guid operatorId;
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<IdentityDbContext>();
                var hasher = services.GetRequiredService<IPasswordHasher<User>>();
                Assert.Equal(6, await DevelopmentSeed.RunAsync(db, hasher, Environments.Development, Password));
                var operatorUser = await db.Users.SingleAsync(user => user.Role == Roles.MillOperator);
                operatorId = operatorUser.Id;
            }


            await MockCaptureSeed.RunAsync(factory.OwnerConnectionString, Environments.Development);
            await AssertSeedDatasetAsync(owner);

            using var client = factory.CreateClient();
            var operatorEmail = await GetOperatorEmailAsync(factory, operatorId);
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = operatorEmail, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var tokens = (await login.Content.ReadFromJsonAsync<IssuedTokens>())!;
            Assert.Equal(Roles.MillOperator, tokens.User.Role);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var setup = await GetJsonAsync(client, "/api/ledger/setup");
            var facility = Assert.Single(setup.GetProperty("facilities").EnumerateArray().ToArray());
            var assignments = setup.GetProperty("assignments").EnumerateArray().ToArray();
            var assignment = Assert.Single(assignments);
            Assert.Equal(operatorId, assignment.GetProperty("userId").GetGuid());
            Assert.Equal(facility.GetProperty("id").GetGuid(), assignment.GetProperty("facilityId").GetGuid());

            var lots = (await GetJsonAsync(client, "/api/ledger/lots")).EnumerateArray().ToArray();
            var lot = Assert.Single(lots);
            var lotId = lot.GetProperty("id").GetGuid();
            var beforeMove = Positions(lot);
            Assert.Single(beforeMove);
            Assert.Equal(1000m, beforeMove.Values.Sum());
            Assert.NotEmpty((await GetJsonAsync(client, $"/api/ledger/lots/{lotId}"))
                .GetProperty("history").EnumerateArray());

            var fromLocationId = beforeMove.Keys.Single();
            var targetLocationId = setup.GetProperty("locations").EnumerateArray()
                .Select(location => location.GetProperty("id").GetGuid())
                .Single(locationId => locationId != fromLocationId);
            using (var transfer = await client.PostAsJsonAsync("/api/ledger/transfers", new
            {
                id = Guid.NewGuid(),
                lotId,
                fromLocationId,
                toLocationId = targetLocationId,
                quantityKg = "100",
                occurredAt = DateTimeOffset.UtcNow
            }))
                Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

            var movedPositions = Positions(Assert.Single((await GetJsonAsync(client, "/api/ledger/lots")).EnumerateArray().ToArray()));
            Assert.Equal(1000m, movedPositions.Values.Sum());
            Assert.Equal(900m, movedPositions[fromLocationId]);
            Assert.Equal(100m, movedPositions[targetLocationId]);

            await MockCaptureSeed.RunAsync(factory.OwnerConnectionString, Environments.Development);
            await AssertSeedDatasetAsync(owner);
            var rerunPositions = Positions(Assert.Single((await GetJsonAsync(client, "/api/ledger/lots")).EnumerateArray().ToArray()));
            Assert.Equal(movedPositions.OrderBy(position => position.Key), rerunPositions.OrderBy(position => position.Key));
            Assert.Equal(1000m, rerunPositions.Values.Sum());
            await AssertCollisionRollbackAsync();
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static async Task AssertCollisionRollbackAsync()
    {
        var factory = new ApiFactory();
        await factory.InitializeAsync();
        try
        {
            Guid ownerId;
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<IdentityDbContext>();
                var hasher = services.GetRequiredService<IPasswordHasher<User>>();
                Assert.Equal(6, await DevelopmentSeed.RunAsync(db, hasher, Environments.Development, Password));
                var purchaser = await db.Users.SingleAsync(user => user.Role == Roles.PurchasingStaff);
                ownerId = purchaser.OrganizationId!.Value;
            }

            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            var conflictingFarmerId = Guid.Parse("10000000-0000-0000-0000-000000000006");
            var conflictingBody = JsonSerializer.Serialize(new
            {
                id = conflictingFarmerId,
                ownerOrganizationId = ownerId,
                name = "conflicting source",
                phoneNumber = (string?)null
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await using (var command = new NpgsqlCommand(
                "INSERT INTO capture_mock.farmers(id,owner_id,body) VALUES(@id,@owner,@body::jsonb)", owner))
            {
                command.Parameters.AddWithValue("id", conflictingFarmerId);
                command.Parameters.AddWithValue("owner", ownerId);
                command.Parameters.AddWithValue("body", conflictingBody);
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MockCaptureSeed.RunAsync(factory.OwnerConnectionString, Environments.Development));
            Assert.Equal(9L, await ScalarAsync<long>(owner, ArtifactRowsSql));
            Assert.Equal(0L, await ScalarAsync<long>(owner, DomainRowsExceptFarmerSql));
            Assert.Equal(1L, await ScalarAsync<long>(owner, "SELECT count(*) FROM capture_mock.farmers"));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static async Task AssertSeedDatasetAsync(NpgsqlConnection connection)
    {
        foreach (var (sql, count) in new (string, long)[]
        {
            ("SELECT count(*) FROM ledger.facilities", 1),
            ("SELECT count(*) FROM ledger.locations", 2),
            ("SELECT count(*) FROM ledger.equipment", 1),
            ("SELECT count(*) FROM ledger.commodities", 1),
            ("SELECT count(*) FROM ledger.assignments", 2),
            ("SELECT count(*) FROM ledger.policies", 0),
            ("SELECT count(*) FROM ledger.operations", 0),
            ("SELECT count(*) FROM ledger.lots", 1),
            ("SELECT count(*) FROM capture_mock.farmers", 1),
            ("SELECT count(*) FROM capture_mock.plots", 1),
            ("SELECT count(*) FROM capture_mock.seasons", 1),
            ("SELECT count(*) FROM capture_mock.plot_seasons", 1),
            ("SELECT count(*) FROM capture_mock.receipts", 2),
            ("SELECT count(*) FROM capture_mock.receipts WHERE status='COMMITTED'", 1),
            ("SELECT count(*) FROM capture_mock.receipts WHERE status='DRAFT'", 1),
            ("SELECT count(*) FROM capture_mock.receipt_lines", 2),
            ("SELECT count(*) FROM capture_mock.receipt_lots", 1),
            ("SELECT count(*) FROM capture_mock.commands WHERE kind='seed-mock'", 1),
            (SourceLineageSql, 2),
            (CommittedLotLineageSql, 1)
        })
            Assert.Equal(count, await ScalarAsync<long>(connection, sql));

        Assert.Equal(1000m, await ScalarAsync<decimal>(connection, """
            SELECT sum(line.quantity_kg) FROM capture_mock.receipt_lines line
            JOIN capture_mock.receipts receipt ON receipt.id=line.receipt_id WHERE receipt.status='COMMITTED'
            """));
        Assert.Equal(200m, await ScalarAsync<decimal>(connection, """
            SELECT sum(line.quantity_kg) FROM capture_mock.receipt_lines line
            JOIN capture_mock.receipts receipt ON receipt.id=line.receipt_id WHERE receipt.status='DRAFT'
            """));
        Assert.Equal(1000m, await ScalarAsync<decimal>(connection, "SELECT sum(quantity_kg) FROM ledger.positions"));
    }

    private static async Task<string> GetOperatorEmailAsync(ApiFactory factory, Guid operatorId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await db.Users.SingleAsync(user => user.Id == operatorId)).Email!;
    }

    private static Dictionary<Guid, decimal> Positions(JsonElement lot) => lot.GetProperty("positions").EnumerateArray()
        .ToDictionary(position => position.GetProperty("locationId").GetGuid(), position =>
            decimal.Parse(position.GetProperty("quantityKg").GetString()!, CultureInfo.InvariantCulture));

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
