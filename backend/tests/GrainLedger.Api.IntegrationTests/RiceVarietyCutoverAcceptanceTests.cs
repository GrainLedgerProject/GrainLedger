using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Identity;
using GrainLedger.Api.Features.Ledger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class RiceVarietyCutoverAcceptanceTests
{
    [Fact]
    public async Task Populated_cutover_preserves_legacy_stock_and_replays_then_assigns_referenced_plot_season()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        var name = "variety_cutover_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", server);
            await create.ExecuteNonQueryAsync();
        }
        var ownerString = new NpgsqlConnectionStringBuilder(factory.OwnerConnectionString) { Database = name }.ConnectionString;
        var runtimeString = new NpgsqlConnectionStringBuilder(factory.RuntimeConnectionString) { Database = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ownerString, x => x.MigrationsAssembly("GrainLedger.Api")
                .MigrationsHistoryTable("__EFMigrationsHistory", "grainledger")).Options;
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("20261123010000_ContainerTraceCorrections");

        var company = Guid.NewGuid(); var actor = Guid.NewGuid(); var farmer = Guid.NewGuid();
        var plot = Guid.NewGuid(); var season = Guid.NewGuid(); var plotSeason = Guid.NewGuid();
        var catalogCommodity = Guid.NewGuid(); var facility = Guid.NewGuid();
        var silo = Guid.NewGuid(); var store = Guid.NewGuid();
        var mill = Guid.NewGuid(); var operatorId = Guid.NewGuid();
        var captureEvent = Guid.NewGuid(); var operation = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow;
        var canonical = JsonSerializer.Serialize(new { operationType = "PLOT_SEASON_REGISTERED", PayloadSchemaVersion = 1,
            capturedAt, EntityId = plotSeason, PlotId = plot, SeasonId = season, riceVariety = "Historic ST25" });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var result = JsonSerializer.Serialize(new { Id = plotSeason, PlotId = plot, SeasonId = season, RiceVariety = "Historic ST25" });
        await using var owner = new NpgsqlConnection(ownerString);
        await owner.OpenAsync();
        Assert.Equal("true", await Scalar(owner, "SELECT (to_regprocedure('grainledger.ledger_byproduct_variety_policy(uuid,text,jsonb)') IS NULL)::text"));
        await LedgerDatabase.ConfigureRuntimeRoleAsync(ownerString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(runtimeString);
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await Exec(owner, "INSERT INTO grainledger.identity_organizations (\"Id\",\"Name\",\"Kind\",\"CreatedAt\") VALUES ($1,'Cutover customer','Customer',now())", company);
            await Exec(owner, "INSERT INTO grainledger.identity_users (\"Id\",\"Provider\",\"ProviderSubject\",\"Email\",\"DisplayName\",\"Role\",\"OrganizationId\",\"IsActive\",\"Version\",\"CreatedAt\",\"UpdatedAt\") VALUES ($1,'test',$1::text,'variety-cutover@example.invalid','Purchaser','PurchasingStaff',$2,true,gen_random_uuid(),now(),now())", actor, company);
            await Exec(owner, "INSERT INTO grainledger.capture_farmers(id,company_id,code,name,created_at) VALUES ($1,$2,'CUTOVER','Farmer',now())", farmer, company);
            await Exec(owner, "INSERT INTO grainledger.capture_plots(id,company_id,farmer_id,code,boundary_geojson,area_ha) VALUES ($1,$2,$3,'CUTOVER','{}',1)", plot, company, farmer);
            await Exec(owner, "INSERT INTO grainledger.capture_crop_seasons(id,company_id,name,starts_on,ends_on) VALUES ($1,$2,'Cutover',current_date,current_date)", season, company);
            await Exec(owner, "INSERT INTO grainledger.capture_plot_seasons(id,company_id,plot_id,season_id,rice_variety,created_by) VALUES ($1,$2,$3,$4,'Historic ST25',$5)", plotSeason, company, plot, season, actor);
            await Exec(owner, "INSERT INTO grainledger.capture_business_events(id,company_id,actor_id,event_type,occurred_at,recorded_at,correlation_id,payload) VALUES ($1,$2,$3,'PLOT_SEASON_REGISTERED',$4,$4,$5,$6::jsonb)", captureEvent, company, actor, capturedAt, operation, canonical);
            await Exec(owner, "INSERT INTO grainledger.capture_offline_operations(company_id,submitted_by,device_id,client_operation_id,payload_hash,payload,captured_at,received_at,status,applied_event_id,result_payload) VALUES ($1,$2,'cutover',$3,$4,$5::jsonb,$6,$6,'APPLIED',$7,$8::jsonb)", company, actor, operation, hash, canonical, capturedAt, captureEvent, result);
            await Exec(owner, "INSERT INTO grainledger.identity_organizations (\"Id\",\"Name\",\"Kind\",\"CreatedAt\") VALUES ($1,'Cutover mill','Mill',now())", mill);
            await Exec(owner, "INSERT INTO grainledger.identity_users (\"Id\",\"Provider\",\"ProviderSubject\",\"Email\",\"DisplayName\",\"Role\",\"OrganizationId\",\"IsActive\",\"Version\",\"CreatedAt\",\"UpdatedAt\") VALUES ($1,'test',$1::text,'cutover-operator@example.invalid','Operator','MillOperator',$2,true,gen_random_uuid(),now(),now())", operatorId, mill);
            await Exec(owner, "INSERT INTO grainledger.ledger_facilities(id,name) VALUES ($1,'Cutover facility')", facility);
            await Exec(owner, "INSERT INTO grainledger.ledger_locations(id,facility_id,name,kind) VALUES ($1,$2,'Cutover silo','SILO'),($3,$2,'Separated store','STORE')", silo, facility, store);
            await Exec(owner, "INSERT INTO grainledger.ledger_assignments(user_id,facility_id) VALUES ($1,$2)", operatorId, facility);
            await Exec(owner, "INSERT INTO grainledger.catalog_commodities(id,code,name) VALUES ($1,'PADDY','Paddy')", catalogCommodity);
            await Exec(owner, "INSERT INTO grainledger.catalog_measurement_units(id,code,name,quantity_kind) VALUES ($1,'KG','Kilogram','MASS')", Guid.NewGuid());
            await tx.CommitAsync();
        }
        var accepted = await CreateLegacyReceipt(owner, company, actor, farmer, plot, season, plotSeason, facility, silo, catalogCommodity, "ACCEPTED");
        var pending = await CreateLegacyReceipt(owner, company, actor, farmer, plot, season, plotSeason, facility, store, catalogCommodity, "PENDING");
        var arrival = JsonSerializer.Serialize(new { operationId = Guid.NewGuid(), expectedReceivingVersion = 0,
            capturedAt = DateTimeOffset.UtcNow, observations = "Legacy matched arrival",
            items = new[] { new { procurementItemId = accepted.Item, measuredValue = "10", measuredUnit = "KG" } } });
        await Scalar(owner, "SELECT grainledger.ledger_arrival_command($1,$2,'measurements',$3::jsonb)::text", operatorId, accepted.Procurement, arrival);
        Assert.Equal("true", await Scalar(owner, "SELECT grainledger.ledger_procurement_package_complete($1)::text", accepted.Procurement));
        Assert.Equal("true", await Scalar(owner, "SELECT grainledger.ledger_procurement_package_complete($1)::text", pending.Procurement));
        var lot = accepted.Lot; var source = accepted.Source;
        var ledgerEvent = Guid.Parse((await Scalar(owner, "SELECT id::text FROM grainledger.ledger_events WHERE lot_id=$1 AND kind='RECEIPT'", lot))!);
        var posting = Guid.Parse((await Scalar(owner, "SELECT id::text FROM grainledger.ledger_postings WHERE event_id=$1", ledgerEvent))!);
        var beforeProcurement = await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.capture_business_events e WHERE id=$1", accepted.Event);
        var beforePending = await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.capture_business_events e WHERE id=$1", pending.Event);
        var beforeCode = await Scalar(owner, "SELECT code FROM grainledger.ledger_lots WHERE id=$1", lot);
        var beforeEvent = await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.ledger_events e WHERE id=$1", ledgerEvent);
        var beforePosting = await Scalar(owner, "SELECT to_jsonb(p)::text FROM grainledger.ledger_postings p WHERE id=$1", posting);
        var beforeSource = await Scalar(owner, "SELECT to_jsonb(s)::text FROM grainledger.capture_source_lots s WHERE id=$1", source);
        var beforeCapture = await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.capture_business_events e WHERE id=$1", captureEvent);
        await using (var db = new AppDbContext(options)) await db.Database.MigrateAsync();
        await LedgerDatabase.ConfigureRuntimeRoleAsync(ownerString, "grainledger_test_runtime");
        Assert.Equal("true", await Scalar(owner, "SELECT has_function_privilege('grainledger_test_runtime','grainledger.ledger_byproduct_variety_policy(uuid,text,jsonb)','EXECUTE')::text"));
        Assert.Equal("false", await Scalar(owner, """
            SELECT EXISTS (
                SELECT 1 FROM pg_proc p, LATERAL aclexplode(coalesce(p.proacl, acldefault('f',p.proowner))) acl
                WHERE p.oid = 'grainledger.ledger_byproduct_variety_policy(uuid,text,jsonb)'::regprocedure
                  AND acl.grantee = 0 AND acl.privilege_type = 'EXECUTE'
            )::text
            """));
        Assert.Equal(beforeEvent, await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.ledger_events e WHERE id=$1", ledgerEvent));
        Assert.Equal(beforePosting, await Scalar(owner, "SELECT to_jsonb(p)::text FROM grainledger.ledger_postings p WHERE id=$1", posting));
        Assert.Equal(beforeSource, await Scalar(owner, "SELECT to_jsonb(s)::text FROM grainledger.capture_source_lots s WHERE id=$1", source));
        Assert.Equal(beforeCapture, await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.capture_business_events e WHERE id=$1", captureEvent));
        Assert.Equal(beforeProcurement, await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.capture_business_events e WHERE id=$1", accepted.Event));
        Assert.Equal(beforePending, await Scalar(owner, "SELECT to_jsonb(e)::text FROM grainledger.capture_business_events e WHERE id=$1", pending.Event));
        Assert.Equal(beforeCode, await Scalar(owner, "SELECT code FROM grainledger.ledger_lots WHERE id=$1", lot));
        Assert.Equal("true", await Scalar(owner, "SELECT grainledger.ledger_procurement_package_complete($1)::text", accepted.Procurement));
        Assert.Equal("true", await Scalar(owner, "SELECT grainledger.ledger_procurement_package_complete($1)::text", pending.Procurement));
        Assert.Equal("UNKNOWN", await Scalar(owner, "SELECT variety_status FROM grainledger.ledger_lots WHERE id=$1 AND rice_variety_id IS NULL AND rice_variety_name IS NULL", lot));
        Assert.Equal("10.000000", await Scalar(owner, "SELECT quantity_kg::text FROM grainledger.ledger_positions WHERE lot_id=$1", lot));
        Assert.Equal("true", await Scalar(owner, "SELECT (lot_id IS NULL)::text FROM grainledger.ledger_bulk_location_last_lots WHERE location_id=$1", silo));

        await using (var runtime = new NpgsqlConnection(runtimeString))
        {
            await runtime.OpenAsync();
            foreach (var table in new[] { "ledger_bulk_location_last_lots", "ledger_byproduct_variety_policy_revisions" })
                foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
                    Assert.Equal("false", await Scalar(runtime, "SELECT has_table_privilege(current_user,$1,$2)::text", "grainledger." + table, privilege));
            Assert.Equal("false", await Scalar(runtime, "SELECT has_function_privilege(current_user,'grainledger.ledger_resolve_lot_variety(uuid,text,uuid[])','EXECUTE')::text"));
        }
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:GrainLedger"] = runtimeString, ["ConnectionStrings:GrainLedgerMigration"] = ownerString })));
        LoginSession session;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            session = new LoginSession { UserId = actor, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
        }
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var cookieOptions = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("GrainLedger.Application");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim("session_id", session.Id.ToString())], "GrainLedger.Application");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = session.ExpiresAt }, "GrainLedger.Application");
        client.DefaultRequestHeaders.Add("Cookie", $"{cookieOptions.Cookie.Name}={cookieOptions.TicketDataFormat.Protect(ticket)}");
        await CsrfAsync(client);
        object Legacy(Guid op) => new { deviceId = "cutover", clientOperationId = op, capturedAt, payloadSchemaVersion = 1,
            payload = new { entityId = plotSeason, plotId = plot, seasonId = season, riceVariety = "Historic ST25" } };
        using (var replay = await RawAsync(client, HttpMethod.Post, "/api/plot-seasons", Legacy(operation)))
        {
            Assert.True(replay.StatusCode == HttpStatusCode.OK,
                $"Legacy replay: {(int)replay.StatusCode} {await replay.Content.ReadAsStringAsync()}");
            var body = await replay.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(plotSeason, body.GetProperty("id").GetGuid());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("riceVarietyId").ValueKind);
        }
        using (var rejected = await RawAsync(client, HttpMethod.Post, "/api/plot-seasons", Legacy(Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal("LEGACY_VARIETY_RESELECT_REQUIRED", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var variety = Guid.NewGuid();
        await Exec(owner, "INSERT INTO grainledger.catalog_rice_varieties(id,code,name) VALUES ($1,'CUTOVER','Catalog ST25')", variety);
        var detail = await GetAsync(client, $"/api/plot-seasons/{plotSeason}");
        Assert.True(detail.GetProperty("canAssignRiceVariety").GetBoolean());
        Assert.False(detail.GetProperty("canEdit").GetBoolean());
        var update = new { plotId = plot, seasonId = season, riceVarietyId = variety, expectedVersion = detail.GetProperty("version").GetInt32() };
        var assigned = await SendAsync(client, HttpMethod.Put, $"/api/plot-seasons/{plotSeason}", update);
        Assert.Equal(variety, assigned.GetProperty("riceVarietyId").GetGuid());
        Assert.Equal("Historic ST25", await Scalar(owner, "SELECT rice_variety FROM grainledger.capture_plot_seasons WHERE id=$1", plotSeason));
        Assert.Equal("UNKNOWN", await Scalar(owner, "SELECT variety_status FROM grainledger.ledger_lots WHERE id=$1", lot));
        await RejectedAsync(client, HttpMethod.Put, $"/api/plot-seasons/{plotSeason}",
            new { plotId = plot, seasonId = season, riceVarietyId = variety, expectedVersion = assigned.GetProperty("version").GetInt32() }, HttpStatusCode.Conflict);
        var newLot = Guid.NewGuid();
        var request = CaptureCommand(new { entityId = Guid.NewGuid(), farmerId = farmer, collectionSiteId = facility,
            receivingLocationId = store, commodityId = catalogCommodity, receivedAt = DateTimeOffset.UtcNow,
            items = new[] { new { itemId = Guid.NewGuid(), lineNo = 1, plotSeasonId = plotSeason,
                measuredValue = "10", measuredUnit = "KG", moisturePercent = "0", lotId = newLot, sourceLotId = Guid.NewGuid() } } }, 2);
        var receipt = await SendAsync(client, HttpMethod.Post, "/api/procurements", request);
        Assert.Equal(variety, receipt.GetProperty("items")[0].GetProperty("riceVarietyId").GetGuid());
        Assert.Equal("Catalog ST25", receipt.GetProperty("items")[0].GetProperty("riceVariety").GetString());

        LoginSession operatorSession;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            operatorSession = new LoginSession { UserId = operatorId, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) };
            db.Sessions.Add(operatorSession);
            await db.SaveChangesAsync();
        }
        using var operatorClient = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        var operatorIdentity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, operatorId.ToString()), new Claim("session_id", operatorSession.Id.ToString())], "GrainLedger.Application");
        var operatorTicket = new AuthenticationTicket(new ClaimsPrincipal(operatorIdentity), new AuthenticationProperties { ExpiresUtc = operatorSession.ExpiresAt }, "GrainLedger.Application");
        operatorClient.DefaultRequestHeaders.Add("Cookie", $"{cookieOptions.Cookie.Name}={cookieOptions.TicketDataFormat.Protect(operatorTicket)}");
        await CsrfAsync(operatorClient);
        await SendAsync(operatorClient, HttpMethod.Post, $"/api/ledger/receiving/{pending.Procurement}/measurements",
            new { operationId = Guid.NewGuid(), expectedReceivingVersion = 0, capturedAt = DateTimeOffset.UtcNow,
                observations = "Legacy pending arrival after assignment", items = new[] {
                    new { procurementItemId = pending.Item, measuredValue = "10", measuredUnit = "KG" } } });
        Assert.Equal("UNKNOWN", await Scalar(owner, "SELECT variety_status FROM grainledger.ledger_lots WHERE id=$1 AND rice_variety_id IS NULL", pending.Lot));
        Assert.Equal("true", await Scalar(owner, "SELECT grainledger.ledger_procurement_package_complete($1)::text", pending.Procurement));
        await SendAsync(operatorClient, HttpMethod.Post, $"/api/ledger/receiving/{receipt.GetProperty("id").GetGuid()}/measurements", MeasurementBody(receipt));
        Assert.Equal("SINGLE", await Scalar(owner, "SELECT variety_status FROM grainledger.ledger_lots WHERE id=$1 AND rice_variety_id=$2 AND rice_variety_name='Catalog ST25'", newLot, variety));
        Assert.Equal("UNKNOWN", await Scalar(owner, "SELECT variety_status FROM grainledger.ledger_lots WHERE id=$1", lot));
    }

    private sealed record LegacyReceipt(Guid Procurement, Guid Item, Guid Source, Guid Lot, Guid Event);

    private static async Task<LegacyReceipt> CreateLegacyReceipt(NpgsqlConnection db, Guid company, Guid actor,
        Guid farmer, Guid plot, Guid season, Guid plotSeason, Guid facility, Guid location, Guid commodity, string code)
    {
        var receipt = new LegacyReceipt(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var operation = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var time = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        await using var transaction = await db.BeginTransactionAsync();
        var lotCode = await Scalar(db, "SELECT grainledger.ledger_reserve_source_lot_code($1,$2,$3,$4)", actor, company, receipt.Procurement, receipt.Lot);
        var snapshot = new {
            Id = receipt.Procurement, Code = code, FarmerId = farmer, CollectionSiteId = facility,
            ReceivingLocationId = location, CommodityId = commodity, Status = "PENDING_ARRIVAL", ReceivedBy = actor,
            ReceivedAt = time, CapturedAt = time, RecordedAt = time, EventId = receipt.Event,
            TotalNetMassKg = "10", ReceivingVersion = 0,
            Items = new[] { new { Id = receipt.Item, LineNo = 1, PlotSeasonId = plotSeason, PlotId = plot,
                SeasonId = season, RiceVariety = "Historic ST25", MeasuredValue = "10", MeasuredUnit = "KG",
                NetMassKg = "10", MoisturePercent = "0", SourceLotId = receipt.Source, LotId = receipt.Lot,
                LotCode = lotCode, Stage = "RAW_PADDY", InitialStock = new { LocationId = location, ReceivedKg = "0" } } }
        };
        var payload = JsonSerializer.Serialize(new { schemaVersion = 2, procurementId = receipt.Procurement, result = snapshot });
        var result = JsonSerializer.Serialize(snapshot);
        await Exec(db, "INSERT INTO grainledger.capture_business_events(id,company_id,actor_id,event_type,occurred_at,recorded_at,correlation_id,payload) VALUES ($1,$2,$3,'PROCUREMENT_COMMITTED',$4,$4,$5,$6::jsonb)", receipt.Event, company, actor, time, operation, payload);
        await Exec(db, "INSERT INTO grainledger.capture_procurements(id,company_id,code,farmer_id,collection_site_id,received_by,received_at,status,event_id) VALUES ($1,$2,$3,$4,$5,$6,$7,'COMMITTED',$8)", receipt.Procurement, company, code, farmer, facility, actor, time, receipt.Event);
        await Exec(db, "INSERT INTO grainledger.capture_procurement_items(id,company_id,procurement_id,line_no,plot_season_id,net_mass_kg,measured_value,measured_unit,moisture_percent) VALUES ($1,$2,$3,1,$4,10,10,'KG',0)", receipt.Item, company, receipt.Procurement, plotSeason);
        await Exec(db, "INSERT INTO grainledger.capture_source_lots(id,company_id,lot_id,procurement_item_id) VALUES ($1,$2,$3,$4)", receipt.Source, company, receipt.Lot, receipt.Item);
        await Exec(db, "INSERT INTO grainledger.capture_offline_operations(company_id,submitted_by,device_id,client_operation_id,payload_hash,payload,captured_at,received_at,status,applied_event_id,result_payload) VALUES ($1,$2,'legacy-receipts',$3,'legacy','{\"operationType\":\"PROCUREMENT_COMMITTED\"}',$4,$4,'APPLIED',$5,$6::jsonb)", company, actor, operation, time, receipt.Event, result);
        await Exec(db, "SELECT grainledger.ledger_register_pending_procurement($1,$2)", actor, receipt.Procurement);
        await transaction.CommitAsync();
        return receipt;
    }

    private static async Task Exec(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> Scalar(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return (string?)await command.ExecuteScalarAsync();
    }
}
