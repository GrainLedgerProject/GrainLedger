using System.Text.Json;
using GrainLedger.Modules.Capture;
using GrainLedger.Modules.Catalogs;
using GrainLedger.Modules.Identity;
using GrainLedger.Modules.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ReceivingMigrationTests
{
    [Fact]
    public async Task Physical_receiving_forward_upgrade_grandfathers_official_source_snapshot_and_all_stock_history()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("arrival_upgrade")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync(); var cs = postgres.GetConnectionString();
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(cs).Options);
        await identity.Database.MigrateAsync();
        var company = new Organization { Name = "Legacy official customer", Kind = OrganizationKind.Customer };
        var actor = new User { Email = "legacy-arrival@example.com", DisplayName = "Legacy purchaser", Provider = "google", ProviderSubject = "legacy-arrival", Role = Roles.PurchasingStaff, OrganizationId = company.Id, IsActive = true };
        identity.AddRange(company, actor); await identity.SaveChangesAsync();
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(cs).Options);
        await catalog.Database.MigrateAsync();
        var commodity = new Commodity { Id = Guid.NewGuid(), Code = "PADDY", Name = "Legacy paddy" };
        catalog.AddRange(commodity, new MeasurementUnit { Id = Guid.NewGuid(), Code = "KG", Name = "Kilogram", QuantityKind = "MASS" });
        await catalog.SaveChangesAsync();
        await using var capture = new CaptureDbContext(new DbContextOptionsBuilder<CaptureDbContext>().UseNpgsql(cs).Options);
        await capture.Database.MigrateAsync();
        await using var ledger = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(cs,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", "ledger")).Options);
        await ledger.GetService<IMigrator>().MigrateAsync("20261014020000_CanonicalCatalogCommodities");
        await using var owner = new NpgsqlConnection(cs); await owner.OpenAsync();
        var procurement = Guid.NewGuid(); var item = Guid.NewGuid(); var lot = Guid.NewGuid(); var source = Guid.NewGuid();
        var facility = Guid.NewGuid(); var location = Guid.NewGuid(); var farmer = Guid.NewGuid(); var plot = Guid.NewGuid();
        var season = Guid.NewGuid(); var plotSeason = Guid.NewGuid(); var ev = Guid.NewGuid(); var op = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow; now = new DateTimeOffset(now.UtcTicks / 10 * 10, TimeSpan.Zero);
        var code = $"TM-{procurement:N}".ToUpperInvariant(); var lotCode = $"LL-{lot:N}".ToUpperInvariant();
        var snapshot = new ProcurementResponse(procurement, code, farmer, facility, location, commodity.Id, "COMMITTED", actor.Id,
            now, now, now, ev, "10", [new ProcurementLineResponse(item, 1, plotSeason, plot, season, "ST25", "10", "KG", "10", "22.5", source, lot, lotCode, "RAW_PADDY", new ProcurementInitialStock(location, "10"))]);
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await using var seed = new NpgsqlCommand("""
                INSERT INTO ledger.facilities VALUES(@facility,'Legacy mill');
                INSERT INTO ledger.locations(id,facility_id,name,kind) VALUES(@location,@facility,'Legacy receiving','STORE');
                INSERT INTO capture.farmers(id,company_id,code,name,created_at) VALUES(@farmer,@company,'LEGACY-FARMER','Legacy source',@now);
                INSERT INTO capture.plots(id,company_id,farmer_id,code,boundary_geojson,area_ha) VALUES(@plot,@company,@farmer,'LEGACY-PLOT','{}',1);
                INSERT INTO capture.crop_seasons(id,company_id,name,starts_on,ends_on) VALUES(@season,@company,'Legacy season','2026-01-01','2026-12-31');
                INSERT INTO capture.plot_seasons(id,company_id,plot_id,season_id,rice_variety) VALUES(@plotSeason,@company,@plot,@season,'ST25');
                INSERT INTO capture.business_events(id,company_id,actor_id,event_type,occurred_at,recorded_at,correlation_id,payload)
                VALUES(@ev,@company,@actor,'PROCUREMENT_COMMITTED',@now,@now,@op,jsonb_build_object('schemaVersion',1,'procurementId',@procurement,'result',@snapshot));
                INSERT INTO capture.procurements(id,company_id,code,farmer_id,collection_site_id,received_by,received_at,status,event_id)
                VALUES(@procurement,@company,@code,@farmer,@facility,@actor,@now,'COMMITTED',@ev);
                INSERT INTO capture.procurement_items(id,company_id,procurement_id,line_no,plot_season_id,net_mass_kg,measured_value,measured_unit,moisture_percent)
                VALUES(@item,@company,@procurement,1,@plotSeason,10,10,'KG',22.5);
                INSERT INTO capture.source_lots(id,company_id,lot_id,procurement_item_id) VALUES(@source,@company,@lot,@item);
                INSERT INTO capture.offline_operations(id,company_id,submitted_by,device_id,client_operation_id,payload_hash,payload,captured_at,received_at,status,applied_event_id,result_payload)
                VALUES(gen_random_uuid(),@company,@actor,'legacy-device',@op,'preserve-legacy-hash','{"operationType":"PROCUREMENT_COMMITTED"}',@now,@now,'APPLIED',@ev,@snapshot);
                SELECT ledger.procurement_receipt(@actor,@company,@ev,@procurement,@commodity,@facility,@location,
                    jsonb_build_array(jsonb_build_object('procurementItemId',@item,'sourceLotId',@source,'lotId',@lot,'lotCode',@lotCode,'netMassKg','10')));
                """, owner, tx);
            foreach (var (name, value) in new[] { ("facility", facility), ("location", location), ("farmer", farmer), ("plot", plot), ("season", season),
                ("plotSeason", plotSeason), ("ev", ev), ("op", op), ("procurement", procurement), ("company", company.Id), ("actor", actor.Id), ("item", item), ("source", source), ("lot", lot), ("commodity", commodity.Id) })
                seed.Parameters.AddWithValue(name, value);
            seed.Parameters.AddWithValue("code", code); seed.Parameters.AddWithValue("lotCode", lotCode); seed.Parameters.AddWithValue("now", now);
            seed.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(snapshot));
            await seed.ExecuteNonQueryAsync(); await tx.CommitAsync();
        }
        const string historySql = """
            SELECT jsonb_build_object(
                'procurements',(SELECT jsonb_agg(to_jsonb(h)-'receiving_workflow' ORDER BY id) FROM capture.procurements h),
                'items',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.procurement_items t),
                'sources',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.source_lots t),
                'captureEvents',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.business_events t),
                'operations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.offline_operations t),
                'lots',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.lots t),
                'events',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.events t),
                'postings',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.postings t),
                'positions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id,location_id) FROM ledger.positions t),
                'contributions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id,source_lot_id) FROM ledger.contributions t))::text
            """;
        await using var history = new NpgsqlCommand(historySql, owner); var before = await history.ExecuteScalarAsync();
        await ledger.Database.MigrateAsync(); Assert.Equal(before, await history.ExecuteScalarAsync());
        await using var check = new NpgsqlCommand("""
            SELECT receiving_workflow IS NULL AND ledger.procurement_package_complete(id)
                AND NOT EXISTS(SELECT 1 FROM ledger.receiving)
                AND to_regprocedure('ledger.procurement_receipt(uuid,uuid,uuid,uuid,uuid,uuid,uuid,jsonb)') IS NULL
            FROM capture.procurements WHERE id=@id
            """, owner);
        check.Parameters.AddWithValue("id", procurement); Assert.Equal(true, await check.ExecuteScalarAsync());
        await using var stock = new NpgsqlCommand("SELECT quantity_kg FROM ledger.positions WHERE lot_id=@lot", owner);
        stock.Parameters.AddWithValue("lot", lot); Assert.Equal(10m, await stock.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Threshold_upgrade_preserves_accepted_history_and_pending_reviews_without_backfilling_snapshots(bool repairInstalledBoundary)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").WithDatabase("threshold_upgrade")
            .WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync(); var cs = postgres.GetConnectionString();
        await using var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(cs).Options);
        await identity.Database.MigrateAsync();
        var company = new Organization { Name = "Pre-threshold customer", Kind = OrganizationKind.Customer };
        var mill = new Organization { Name = "Pre-threshold mill", Kind = OrganizationKind.Mill };
        User Actor(string role, Guid organization) => new() { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = role,
            Provider = "google", ProviderSubject = Guid.NewGuid().ToString(), Role = role, OrganizationId = organization, IsActive = true };
        var purchaser = Actor(Roles.PurchasingStaff, company.Id); var op = Actor(Roles.MillOperator, mill.Id);
        var manager = Actor(Roles.MillManager, mill.Id); var admin = Actor(Roles.Administrator, mill.Id);
        identity.AddRange(company, mill, purchaser, op, manager, admin); await identity.SaveChangesAsync();
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(cs).Options);
        await catalog.Database.MigrateAsync();
        var commodity = new Commodity { Id = Guid.NewGuid(), Code = "PADDY", Name = "Upgrade paddy" };
        catalog.AddRange(commodity, new MeasurementUnit { Id = Guid.NewGuid(), Code = "KG", Name = "Kilogram", QuantityKind = "MASS" });
        await catalog.SaveChangesAsync();
        await using var capture = new CaptureDbContext(new DbContextOptionsBuilder<CaptureDbContext>().UseNpgsql(cs).Options);
        await capture.Database.MigrateAsync();
        await using var ledger = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(cs,
            n => n.MigrationsHistoryTable("__EFMigrationsHistory", "ledger")).Options);
        await ledger.GetService<IMigrator>().MigrateAsync("20261014210000_PaginateLedgerLists");
        await using var owner = new NpgsqlConnection(cs); await owner.OpenAsync();
        var facility = Guid.NewGuid(); var location = Guid.NewGuid(); var farmer = Guid.NewGuid(); var plot = Guid.NewGuid();
        var season = Guid.NewGuid(); var plotSeason = Guid.NewGuid(); var evidence = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO ledger.facilities(id,name) VALUES(@facility,'Upgrade mill');
            INSERT INTO ledger.locations(id,facility_id,name,kind) VALUES(@location,@facility,'Upgrade store','STORE');
            INSERT INTO ledger.assignments(user_id,facility_id) VALUES(@operator,@facility),(@manager,@facility);
            INSERT INTO ledger.evidence(id,owner_id,facility_id,file_name,content_type,content,sha256,actor_id)
                VALUES(@evidence,@company,@facility,'upgrade.txt','text/plain',decode('78','hex'),'upgrade',@operator);
            INSERT INTO capture.farmers(id,company_id,code,name,created_at) VALUES(@farmer,@company,'UPGRADE-FARMER','Upgrade source',clock_timestamp());
            INSERT INTO capture.plots(id,company_id,farmer_id,code,boundary_geojson,area_ha) VALUES(@plot,@company,@farmer,'UPGRADE-PLOT','{}',1);
            INSERT INTO capture.crop_seasons(id,company_id,name,starts_on,ends_on) VALUES(@season,@company,'Upgrade season','2026-01-01','2026-12-31');
            INSERT INTO capture.plot_seasons(id,company_id,plot_id,season_id,rice_variety) VALUES(@plotSeason,@company,@plot,@season,'ST25');
            """, owner))
        {
            foreach (var (key, value) in new[] { ("facility", facility), ("location", location), ("operator", op.Id), ("manager", manager.Id),
                ("evidence", evidence), ("company", company.Id), ("farmer", farmer), ("plot", plot), ("season", season), ("plotSeason", plotSeason) })
                seed.Parameters.AddWithValue(key, value);
            await seed.ExecuteNonQueryAsync();
        }

        async Task<JsonElement> Register(bool unifiedCode = false)
        {
            var procurement = Guid.NewGuid(); var item = Guid.NewGuid(); var lot = Guid.NewGuid(); var source = Guid.NewGuid();
            var ev = Guid.NewGuid(); var operation = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow; now = new DateTimeOffset(now.UtcTicks / 10 * 10, TimeSpan.Zero);
            var code = $"TM-{procurement:N}".ToUpperInvariant(); var lotCode = $"LL-{lot:N}".ToUpperInvariant();
            await using var tx = await owner.BeginTransactionAsync();
            if (unifiedCode)
            {
                await using var reserve = new NpgsqlCommand("SELECT ledger.reserve_source_lot_code(@actor,@company,@procurement,@lot)", owner, tx);
                reserve.Parameters.AddWithValue("actor", purchaser.Id); reserve.Parameters.AddWithValue("company", company.Id);
                reserve.Parameters.AddWithValue("procurement", procurement); reserve.Parameters.AddWithValue("lot", lot);
                lotCode = (string)(await reserve.ExecuteScalarAsync())!;
            }
            var snapshot = new ProcurementResponse(procurement, code, farmer, facility, location, commodity.Id, "PENDING_ARRIVAL", purchaser.Id,
                now, now, now, ev, "100", [new ProcurementLineResponse(item, 1, plotSeason, plot, season, "ST25", "100", "KG", "100", "22.5", source, lot, lotCode, "RAW_PADDY", new ProcurementInitialStock(location, "0"))]);
            await using var seed = new NpgsqlCommand("""
                INSERT INTO capture.business_events(id,company_id,actor_id,event_type,occurred_at,recorded_at,correlation_id,payload)
                    VALUES(@ev,@company,@actor,'PROCUREMENT_COMMITTED',@now,@now,@operation,jsonb_build_object('schemaVersion',@schemaVersion,'procurementId',@procurement,'result',@snapshot));
                INSERT INTO capture.procurements(id,company_id,code,farmer_id,collection_site_id,received_by,received_at,status,event_id)
                    VALUES(@procurement,@company,@code,@farmer,@facility,@actor,@now,'COMMITTED',@ev);
                INSERT INTO capture.procurement_items(id,company_id,procurement_id,line_no,plot_season_id,net_mass_kg,measured_value,measured_unit,moisture_percent)
                    VALUES(@item,@company,@procurement,1,@plotSeason,100,100,'KG',22.5);
                INSERT INTO capture.source_lots(id,company_id,lot_id,procurement_item_id) VALUES(@source,@company,@lot,@item);
                INSERT INTO capture.offline_operations(id,company_id,submitted_by,device_id,client_operation_id,payload_hash,payload,captured_at,received_at,status,applied_event_id,result_payload)
                    VALUES(gen_random_uuid(),@company,@actor,'upgrade-device',@operation,'immutable-upgrade-hash','{"operationType":"PROCUREMENT_COMMITTED"}',@now,@now,'APPLIED',@ev,@snapshot);
                SELECT ledger.register_pending_procurement(@actor,@procurement);
                """, owner, tx);
            foreach (var (key, value) in new[] { ("ev", ev), ("company", company.Id), ("actor", purchaser.Id), ("operation", operation),
                ("procurement", procurement), ("farmer", farmer), ("facility", facility), ("item", item), ("plotSeason", plotSeason), ("source", source), ("lot", lot) })
                seed.Parameters.AddWithValue(key, value);
            seed.Parameters.AddWithValue("now", now); seed.Parameters.AddWithValue("code", code);
            seed.Parameters.AddWithValue("schemaVersion", unifiedCode ? 2 : 1);
            seed.Parameters.AddWithValue("snapshot", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(snapshot));
            await seed.ExecuteNonQueryAsync(); await tx.CommitAsync();
            return JsonSerializer.SerializeToElement(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        async Task<JsonElement> Call(Guid actor, Guid id, string kind, object body)
        {
            await using var cmd = new NpgsqlCommand("SELECT ledger.arrival_command(@actor,@id,@kind,@body)", owner);
            cmd.Parameters.AddWithValue("actor", actor); cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("kind", kind);
            cmd.Parameters.AddWithValue("body", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(body));
            return JsonSerializer.Deserialize<JsonElement>((string)(await cmd.ExecuteScalarAsync())!);
        }
        object Review(JsonElement state, string action) => new
        {
            operationId = Guid.NewGuid(), expectedReceivingVersion = state.GetProperty("receivingVersion").GetInt32(),
            measurementSetId = state.GetProperty("latestMeasurementSetId").GetGuid(), action, evidenceId = evidence, reason = "Upgrade scoped physical review"
        };
        var exact = await Register(); var approved = await Register(); var pending = await Register(); var unmeasured = await Register();
        var exactId = exact.GetProperty("id").GetGuid(); var approvedId = approved.GetProperty("id").GetGuid(); var pendingId = pending.GetProperty("id").GetGuid();
        await Call(op.Id, exactId, "measurements", LedgerScenario.MeasurementBody(exact));
        var approvedReview = await Call(op.Id, approvedId, "measurements", LedgerScenario.MeasurementBody(approved, actual: ["97.5"]));
        await Call(manager.Id, approvedId, "reviews", Review(approvedReview, "APPROVE_SHORTAGE"));
        var pendingBody = LedgerScenario.MeasurementBody(pending, actual: ["99"]);
        var beforePending = await Call(op.Id, pendingId, "measurements", pendingBody);
        Assert.Equal("AWAITING_MANAGER_REVIEW", beforePending.GetProperty("status").GetString());
        const string historySql = """
            SELECT jsonb_build_object(
                'captureEvents',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.business_events t),
                'captureOperations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.offline_operations t),
                'receiving',(SELECT jsonb_agg(to_jsonb(t) ORDER BY procurement_id) FROM ledger.receiving t),
                'sets',(SELECT jsonb_agg(to_jsonb(t)-'threshold_company_id'-'threshold_percentage'-'threshold_source'-'threshold_revision' ORDER BY id) FROM ledger.arrival_measurement_sets t),
                'measurements',(SELECT jsonb_agg(to_jsonb(t) ORDER BY measurement_set_id,procurement_item_id) FROM ledger.arrival_measurements t),
                'reviews',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.arrival_reviews t),
                'arrivalOperations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY operation_id) FROM ledger.arrival_operations t),
                'lots',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.lots t),
                'events',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.events t),
                'postings',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM ledger.postings t),
                'positions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id,location_id) FROM ledger.positions t),
                'contributions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id,source_lot_id) FROM ledger.contributions t))::text
            """;
        await using var history = new NpgsqlCommand(historySql, owner); var before = await history.ExecuteScalarAsync();
        await ledger.GetService<IMigrator>().MigrateAsync(repairInstalledBoundary ? "20261019000000_ReceivingLossThreshold" : null);
        Assert.Equal(before, await history.ExecuteScalarAsync());
        await using (var check = new NpgsqlCommand("""
            SELECT (SELECT count(*)=3 FROM ledger.arrival_measurement_sets
                WHERE threshold_company_id IS NULL AND threshold_percentage IS NULL AND threshold_source IS NULL AND threshold_revision IS NULL)
                AND NOT EXISTS(SELECT 1 FROM ledger.receiving_threshold_overrides)
                AND (SELECT count(*)=1 FROM ledger.receiving_threshold_history WHERE action='INITIALIZE' AND actor_id IS NULL AND migration_id='20261019000000_ReceivingLossThreshold')
                AND NOT EXISTS(SELECT 1 FROM capture.procurements WHERE NOT ledger.procurement_package_complete(id))
            """, owner))
            Assert.Equal(true, await check.ExecuteScalarAsync());
        Assert.Equal(beforePending.GetRawText(), (await Call(op.Id, pendingId, "measurements", pendingBody)).GetRawText());
        await using (var detail = new NpgsqlCommand("SELECT ledger.receiving_detail(@actor,@id)", owner))
        {
            detail.Parameters.AddWithValue("actor", op.Id); detail.Parameters.AddWithValue("id", pendingId);
            var state = JsonSerializer.Deserialize<JsonElement>((string)(await detail.ExecuteScalarAsync())!);
            Assert.Equal("AWAITING_MANAGER_REVIEW", state.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, state.GetProperty("measurementSets")[0].GetProperty("threshold").ValueKind);
            Assert.Equal(0m, LedgerScenario.Decimal(state.GetProperty("usableStockKg")));
        }
        var arrival = await Call(op.Id, unmeasured.GetProperty("id").GetGuid(), "measurements", LedgerScenario.MeasurementBody(unmeasured, actual: ["98"]));
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", arrival.GetProperty("status").GetString());
        Assert.Equal(2m, LedgerScenario.Decimal(arrival.GetProperty("measurementSets")[0].GetProperty("threshold").GetProperty("percentage")));
        var requested = await Call(manager.Id, pendingId, "reviews", Review(beforePending, "REQUEST_REMEASUREMENT"));
        var remeasured = await Call(op.Id, pendingId, "measurements", LedgerScenario.MeasurementBody(pending,
            version: requested.GetProperty("receivingVersion").GetInt32(), actual: ["99"]));
        Assert.Equal("RECEIVED_WITHIN_TOLERANCE", remeasured.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, remeasured.GetProperty("measurementSets")[0].GetProperty("threshold").ValueKind);
        Assert.Equal(2m, LedgerScenario.Decimal(remeasured.GetProperty("measurementSets")[1].GetProperty("threshold").GetProperty("percentage")));
        Assert.Equal(99m, LedgerScenario.Decimal(remeasured.GetProperty("usableStockKg")));
        await using var valid = new NpgsqlCommand("SELECT bool_and(ledger.procurement_package_complete(id)) FROM capture.procurements", owner);
        Assert.Equal(true, await valid.ExecuteScalarAsync());

        if (repairInstalledBoundary)
        {
            // Reproduce functions already installed by the pre-fix threshold migration, not a fresh corrected database.
            using var stream = typeof(RepairReceivingLotCodeBoundary).Assembly.GetManifestResourceStream(
                "GrainLedger.Modules.Ledger.Sql.RepairReceivingLotCodeBoundary.sql")!;
            using var reader = new StreamReader(stream);
            var repairedFunctions = await reader.ReadToEndAsync();
            Assert.Contains("ledger.source_lot_code(r,row.lot_id)", repairedFunctions);
            Assert.Contains("ledger.source_lot_code(r,sl.lot_id)", repairedFunctions);
            var installedFunctions = repairedFunctions
                .Replace("ledger.source_lot_code(r,row.lot_id)", "upper('LL-'||replace(row.lot_id::text,'-',''))", StringComparison.Ordinal)
                .Replace("ledger.source_lot_code(r,sl.lot_id)", "upper('LL-'||replace(sl.lot_id::text,'-',''))", StringComparison.Ordinal);
            await using (var install = new NpgsqlCommand(installedFunctions, owner))
                await install.ExecuteNonQueryAsync();
            await using (var installed = new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM ledger."__EFMigrationsHistory" WHERE "MigrationId"='20261019000000_ReceivingLossThreshold')
                    AND NOT EXISTS(SELECT 1 FROM ledger."__EFMigrationsHistory" WHERE "MigrationId"='20261019010000_RepairReceivingLotCodeBoundary')
                """, owner))
                Assert.Equal(true, await installed.ExecuteScalarAsync());

            // Include applied threshold snapshots, policy history, provenance and code reservations in preservation check.
            var repairHistorySql = historySql.Replace(
                "to_jsonb(t)-'threshold_company_id'-'threshold_percentage'-'threshold_source'-'threshold_revision'",
                "to_jsonb(t)", StringComparison.Ordinal).Replace(
                "SELECT jsonb_build_object(", """
                SELECT jsonb_build_object(
                    'procurements',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.procurements t),
                    'items',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.procurement_items t),
                    'sources',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM capture.source_lots t),
                    'reservations',(SELECT jsonb_agg(to_jsonb(t) ORDER BY lot_id) FROM ledger.lot_code_reservations t),
                    'codes',(SELECT jsonb_agg(to_jsonb(t) ORDER BY code) FROM ledger.lot_codes t),
                    'thresholdDefault',(SELECT to_jsonb(t) FROM ledger.receiving_threshold_default t),
                    'thresholdOverrides',(SELECT jsonb_agg(to_jsonb(t) ORDER BY company_id) FROM ledger.receiving_threshold_overrides t),
                    'thresholdHistory',(SELECT jsonb_agg(to_jsonb(t) ORDER BY revision) FROM ledger.receiving_threshold_history t),
                """, StringComparison.Ordinal);
            await using var repairHistory = new NpgsqlCommand(repairHistorySql, owner);
            var beforeRepair = await repairHistory.ExecuteScalarAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => Register(unifiedCode: true));
            Assert.Equal("23514", rejected.SqlState);
            Assert.Equal("Pending source package incomplete", rejected.MessageText);
            Assert.Equal(beforeRepair, await repairHistory.ExecuteScalarAsync());

            await ledger.GetService<IMigrator>().MigrateAsync("20261019010000_RepairReceivingLotCodeBoundary");
            Assert.Equal(beforeRepair, await repairHistory.ExecuteScalarAsync());
            await using (var applied = new NpgsqlCommand("""
                SELECT count(*)=1 FROM ledger."__EFMigrationsHistory"
                WHERE "MigrationId"='20261019010000_RepairReceivingLotCodeBoundary'
                """, owner))
                Assert.Equal(true, await applied.ExecuteScalarAsync());

            var unified = await Register(unifiedCode: true);
            var unifiedId = unified.GetProperty("id").GetGuid();
            var unifiedLot = unified.GetProperty("items")[0].GetProperty("lotId").GetGuid();
            var unifiedCode = unified.GetProperty("items")[0].GetProperty("lotCode").GetString();
            Assert.Matches("^RAW-[0-9]{8}-[0-9]{6,}$", unifiedCode!);
            await using (var detail = new NpgsqlCommand("SELECT ledger.receiving_detail(@actor,@id)", owner))
            {
                detail.Parameters.AddWithValue("actor", op.Id); detail.Parameters.AddWithValue("id", unifiedId);
                var state = JsonSerializer.Deserialize<JsonElement>((string)(await detail.ExecuteScalarAsync())!);
                Assert.Equal("PENDING_ARRIVAL", state.GetProperty("status").GetString());
                Assert.Equal(unifiedCode, state.GetProperty("items")[0].GetProperty("lotCode").GetString());
                Assert.Equal(0m, LedgerScenario.Decimal(state.GetProperty("usableStockKg")));
                Assert.Equal(2m, LedgerScenario.Decimal(state.GetProperty("effectiveThreshold").GetProperty("percentage")));
            }
            var accepted = await Call(op.Id, unifiedId, "measurements", LedgerScenario.MeasurementBody(unified, actual: ["98"]));
            Assert.Equal("RECEIVED_WITHIN_TOLERANCE", accepted.GetProperty("status").GetString());
            Assert.Equal(unifiedCode, accepted.GetProperty("items")[0].GetProperty("lotCode").GetString());
            Assert.Equal(98m, LedgerScenario.Decimal(accepted.GetProperty("usableStockKg")));
            Assert.Equal(2m, LedgerScenario.Decimal(accepted.GetProperty("measurementSets")[0].GetProperty("threshold").GetProperty("percentage")));
            Assert.Empty(accepted.GetProperty("reviews").EnumerateArray());
            await using var stock = new NpgsqlCommand("""
                SELECT ledger.procurement_package_complete(h.id)
                    AND ev.payload->>'schemaVersion'='2'
                    AND ev.payload->'result'->'Items'->0->>'LotCode'=@code
                    AND o.result_payload=ev.payload->'result'
                    AND l.code=@code AND reservation.code=@code AND p.quantity_kg=98
                FROM capture.procurements h JOIN capture.business_events ev ON ev.id=h.event_id
                JOIN capture.offline_operations o ON o.applied_event_id=ev.id
                JOIN ledger.lots l ON l.receipt_id=h.id
                JOIN ledger.lot_code_reservations reservation ON reservation.lot_id=l.id
                JOIN ledger.positions p ON p.lot_id=l.id
                WHERE h.id=@id AND l.id=@lot
                """, owner);
            stock.Parameters.AddWithValue("id", unifiedId); stock.Parameters.AddWithValue("lot", unifiedLot);
            stock.Parameters.AddWithValue("code", unifiedCode!);
            Assert.Equal(true, await stock.ExecuteScalarAsync());
        }
    }
}
