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

public sealed class CaptureCutoverMigrationTests
{
    [Fact]
    public async Task Forward_cutover_preserves_historical_stock_sources_restrictions_and_retired_schema_data()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("cutover_tests")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options);
        await identity.Database.MigrateAsync();
        var company = new Organization { Name = "Historical company", Kind = OrganizationKind.Customer };
        var actor = new User { Email = "history@example.com", DisplayName = "Historical purchaser", Provider = "google",
            ProviderSubject = "historical", Role = Roles.PurchasingStaff, OrganizationId = company.Id, IsActive = true };
        identity.AddRange(company, actor); await identity.SaveChangesAsync();
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connectionString).Options);
        await catalog.Database.MigrateAsync();
        await using var capture = new CaptureDbContext(new DbContextOptionsBuilder<CaptureDbContext>().UseNpgsql(connectionString).Options);
        await capture.Database.MigrateAsync();
        await using var ledger = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(connectionString,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", "ledger")).Options);
        await ledger.GetService<IMigrator>().MigrateAsync("20261014000000_OptimizeLedgerReads");
        await using var owner = new NpgsqlConnection(connectionString); await owner.OpenAsync();
        var lot = Guid.NewGuid(); var receipt = Guid.NewGuid(); var facility = Guid.NewGuid(); var location = Guid.NewGuid();
        var commodity = Guid.NewGuid(); var ev = Guid.NewGuid(); var evidence = Guid.NewGuid();
        await using (var transaction = await owner.BeginTransactionAsync())
        {
            // Fixture represents already-committed pre-cutover data, not an executable synthetic workflow.
            await using var seed = new NpgsqlCommand("""
                CREATE SCHEMA capture_mock;
                CREATE TABLE capture_mock.retained_history(id integer PRIMARY KEY, payload text NOT NULL);
                INSERT INTO capture_mock.retained_history VALUES(1,'retain exactly');
                ALTER TABLE ledger.lots DISABLE TRIGGER receipt_package;
                ALTER TABLE ledger.receiving_blocks DISABLE TRIGGER receiving_resolution_package;
                INSERT INTO ledger.facilities VALUES(@facility,'Historical mill');
                INSERT INTO ledger.locations(id,facility_id,name,kind) VALUES(@location,@facility,'Historical receiving','STORE');
                INSERT INTO ledger.commodities VALUES(@commodity,'Historical commodity');
                INSERT INTO ledger.lots(id,code,owner_id,commodity_id,stage,product_identity,receipt_id,receipt_line_id,code_origin_id)
                VALUES(@lot,'HISTORICAL',@company,@commodity,'RAW_PADDY','RAW_PADDY',@receipt,gen_random_uuid(),ledger.register_code('HISTORICAL',@lot,NULL,NULL));
                INSERT INTO ledger.events(id,kind,actor_id,lot_id,details) VALUES(@ev,'RECEIPT',@actor,@lot,jsonb_build_object('receiptId',@receipt));
                SELECT ledger.post(@ev,@lot,@location,10);
                INSERT INTO ledger.contributions VALUES(@lot,@lot,10);
                INSERT INTO ledger.evidence(id,owner_id,facility_id,file_name,content_type,content,sha256,actor_id)
                VALUES(@evidence,@company,@facility,'history.txt','text/plain',decode('01','hex'),'preserved',@actor);
                INSERT INTO ledger.receiving_blocks VALUES(gen_random_uuid(),@lot,true,@evidence,'REAL_DIFFERENCE',true);
                INSERT INTO ledger.receipt_correction_blocks VALUES(gen_random_uuid(),@receipt,@company,false);
                ALTER TABLE ledger.lots ENABLE TRIGGER receipt_package;
                ALTER TABLE ledger.receiving_blocks ENABLE TRIGGER receiving_resolution_package;
                """, owner, transaction);
            foreach (var (name, value) in new[] { ("facility", facility), ("location", location), ("commodity", commodity),
                ("lot", lot), ("receipt", receipt), ("ev", ev), ("actor", actor.Id), ("company", company.Id), ("evidence", evidence) })
                seed.Parameters.AddWithValue(name, value);
            await seed.ExecuteNonQueryAsync(); await transaction.CommitAsync();
        }
        const string historySql = """
            SELECT jsonb_build_object(
                'lots',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.lots t),
                'events',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.events t),
                'postings',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.postings t),
                'sources',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.contributions t),
                'positions',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.positions t),
                'receiving',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.receiving_blocks t),
                'corrections',(SELECT jsonb_agg(to_jsonb(t)) FROM ledger.receipt_correction_blocks t))::text
            """;
        await using var history = new NpgsqlCommand(historySql, owner);
        var before = await history.ExecuteScalarAsync();
        await ledger.GetService<IMigrator>().MigrateAsync("20261014010000_UseOfficialCaptureBoundary");
        var sameNameCatalog = Guid.NewGuid(); var mappedCatalog = Guid.NewGuid(); var mappedLedger = Guid.NewGuid();
        catalog.Commodities.AddRange(
            new Commodity { Id = sameNameCatalog, Code = "HISTORY", Name = "Historical commodity" },
            new Commodity { Id = mappedCatalog, Code = "MAPPED", Name = "Canonical existing mapping" });
        await catalog.SaveChangesAsync();
        await using (var existingCommodity = new NpgsqlCommand("INSERT INTO ledger.commodities VALUES($1,'Old generated Catalog label')", owner))
        {
            existingCommodity.Parameters.AddWithValue(mappedLedger);
            await existingCommodity.ExecuteNonQueryAsync();
        }
        await using (var existingMapping = new NpgsqlCommand("INSERT INTO ledger.catalog_commodities VALUES($1,$2)", owner))
        {
            existingMapping.Parameters.AddWithValue(mappedCatalog);
            existingMapping.Parameters.AddWithValue(mappedLedger);
            await existingMapping.ExecuteNonQueryAsync();
        }
        await ledger.Database.MigrateAsync();
        Assert.Equal(before, await history.ExecuteScalarAsync());
        await using (var mappings = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM ledger.catalog_commodities WHERE catalog_commodity_id=$1 AND ledger_commodity_id<>$2)
                AND EXISTS(SELECT 1 FROM ledger.catalog_commodities WHERE catalog_commodity_id=$3 AND ledger_commodity_id=$4)
                AND NOT EXISTS(SELECT 1 FROM ledger.catalog_commodities WHERE ledger_commodity_id=$2)
                AND EXISTS(SELECT 1 FROM ledger.commodities WHERE id=$2 AND name='Historical commodity')
                AND EXISTS(SELECT 1 FROM jsonb_array_elements(ledger.read($5,'lookups','{}')->'commodities') c
                    WHERE (c->>'id')::uuid=$4 AND c->>'name'='Canonical existing mapping' AND c->>'code'='MAPPED')
            """, owner))
        {
            mappings.Parameters.AddWithValue(sameNameCatalog); mappings.Parameters.AddWithValue(commodity);
            mappings.Parameters.AddWithValue(mappedCatalog); mappings.Parameters.AddWithValue(mappedLedger);
            mappings.Parameters.AddWithValue(actor.Id);
            Assert.Equal(true, await mappings.ExecuteScalarAsync());
        }
        await using var retained = new NpgsqlCommand("SELECT payload FROM capture_mock.retained_history WHERE id=1", owner);
        Assert.Equal("retain exactly", await retained.ExecuteScalarAsync());
        await using var restrictions = new NpgsqlCommand("SELECT ledger.blocked(@lot),ledger.receiving_restricted(@lot)", owner);
        restrictions.Parameters.AddWithValue("lot", lot);
        await using (var reader = await restrictions.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync()); Assert.True(reader.GetBoolean(0)); Assert.True(reader.GetBoolean(1));
        }
        await using var role = new NpgsqlCommand("CREATE ROLE cutover_runtime LOGIN PASSWORD 'test-runtime' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT", owner);
        await role.ExecuteNonQueryAsync();
        await LedgerDatabase.ConfigureRuntimeRoleAsync(connectionString, "cutover_runtime");
        var runtimeConnection = new NpgsqlConnectionStringBuilder(connectionString) { Username = "cutover_runtime", Password = "test-runtime" }.ConnectionString;
        await LedgerDatabase.ValidateRuntimeAsync(runtimeConnection);
        await using var runtime = new NpgsqlConnection(runtimeConnection); await runtime.OpenAsync();
        foreach (var sql in new[] { "UPDATE ledger.receiving_blocks SET blocked=false", "DELETE FROM ledger.receipt_correction_blocks",
            "DELETE FROM ledger.contributions", "SELECT * FROM capture_mock.retained_history" })
        {
            await using var denied = new NpgsqlCommand(sql, runtime);
            var error = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        await using var removed = new NpgsqlCommand("SELECT to_regprocedure('ledger.receipt(uuid,jsonb)') IS NULL AND to_regprocedure('ledger.receiving_block(uuid,uuid,uuid,boolean,uuid,text)') IS NULL", owner);
        Assert.Equal(true, await removed.ExecuteScalarAsync());
    }
}
