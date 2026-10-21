using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MixedBulkMigrationTests
{
    [Fact]
    public async Task Forward_migration_preserves_preexisting_history_and_legacy_rows()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("mixed_bulk_upgrade")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        var cs = postgres.GetConnectionString();
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(cs).Options);
        await identity.Database.MigrateAsync();
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(cs).Options);
        await catalog.Database.MigrateAsync();
        await using var capture = new CaptureDbContext(new DbContextOptionsBuilder<CaptureDbContext>().UseNpgsql(cs).Options);
        await capture.Database.MigrateAsync();
        await using var ledger = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(cs,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", "ledger")).Options);
        await ledger.GetService<IMigrator>().MigrateAsync("20261019010000_RepairReceivingLotCodeBoundary");

        var marker = Guid.NewGuid();
        await using var owner = new NpgsqlConnection(cs); await owner.OpenAsync();
        await using (var seed = new NpgsqlCommand("INSERT INTO ledger.facilities(id,name) VALUES($1,'Pre-mixed legacy facility')", owner))
        {
            seed.Parameters.AddWithValue(marker);
            await seed.ExecuteNonQueryAsync();
        }
        var priorHistory = await HistoryAsync(owner);
        await ledger.GetService<IMigrator>().MigrateAsync("20261020000000_MixedBulkStorage");
        var migratedHistory = await HistoryAsync(owner);
        Assert.Contains("20261020000000_MixedBulkStorage", migratedHistory);
        Assert.Equal(priorHistory, migratedHistory.Where(id => id != "20261020000000_MixedBulkStorage").ToArray());
        await using var check = new NpgsqlCommand("SELECT name FROM ledger.facilities WHERE id=$1", owner);
        check.Parameters.AddWithValue(marker);
        Assert.Equal("Pre-mixed legacy facility", await check.ExecuteScalarAsync());
        await using var columns = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema='ledger' AND table_name='locations' AND column_name='mixed_bulk'", owner);
        Assert.Equal(1L, (long)(await columns.ExecuteScalarAsync())!);
    }

    private static async Task<string[]> HistoryAsync(NpgsqlConnection owner)
    {
        await using var command = new NpgsqlCommand("SELECT \"MigrationId\" FROM ledger.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", owner);
        var ids = new List<string>();
        await using var rows = await command.ExecuteReaderAsync();
        while (await rows.ReadAsync()) ids.Add(rows.GetString(0));
        return ids.ToArray();
    }
}
