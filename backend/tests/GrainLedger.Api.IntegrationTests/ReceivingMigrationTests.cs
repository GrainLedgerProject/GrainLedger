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
}
