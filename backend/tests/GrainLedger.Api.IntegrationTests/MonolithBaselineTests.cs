using GrainLedger.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MonolithBaselineTests
{
    [Fact]
    public async Task Fresh_baseline_is_idempotent_and_runtime_role_stays_restricted()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(factory.OwnerConnectionString, npgsql => npgsql.MigrationsAssembly("GrainLedger.Api")
                .MigrationsHistoryTable("__EFMigrationsHistory", "grainledger"))
            .Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        await using (var baseline = new NpgsqlCommand("""
            SELECT to_regclass('grainledger.identity_users') IS NOT NULL
               AND to_regclass('grainledger.catalog_commodities') IS NOT NULL
               AND to_regclass('grainledger.capture_procurements') IS NOT NULL
               AND to_regclass('grainledger.ledger_positions') IS NOT NULL
               AND to_regclass('grainledger.exports_export_orders') IS NOT NULL
               AND to_regclass('grainledger.ledger_receiving_threshold_default') IS NOT NULL
               AND to_regclass('grainledger.ledger_receiving_threshold_history') IS NOT NULL
               AND (SELECT count(*) FROM grainledger.ledger_receiving_threshold_default WHERE singleton AND percentage=2) = 1
               AND (SELECT count(*) FROM grainledger.ledger_receiving_threshold_history
                    WHERE scope='SYSTEM_DEFAULT' AND action='INITIALIZE' AND new_percentage=2
                      AND migration_id='20261019000000_ReceivingLossThreshold') = 1
               AND (SELECT count(*) FROM grainledger."__EFMigrationsHistory") = 1
               AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname IN ('identity','catalog','capture','ledger','exports'))
            """, owner))
            Assert.True((bool)(await baseline.ExecuteScalarAsync())!);

        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync();
        await using var security = new NpgsqlCommand("""
            SELECT has_schema_privilege(current_user, 'grainledger', 'USAGE')
               AND has_function_privilege(current_user, 'grainledger.ledger_command(uuid,text,jsonb)', 'EXECUTE')
               AND NOT has_schema_privilege(current_user, 'grainledger', 'CREATE')
               AND NOT has_table_privilege(current_user, 'grainledger.ledger_postings', 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
               AND NOT has_table_privilege(current_user, 'grainledger.ledger_evidence', 'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
            """, runtime);
        Assert.True((bool)(await security.ExecuteScalarAsync())!);
    }
}
