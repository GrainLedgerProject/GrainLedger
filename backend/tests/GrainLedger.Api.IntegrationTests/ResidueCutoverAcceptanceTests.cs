using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;
using static GrainLedger.Api.IntegrationTests.LabReportTests;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueCutoverAcceptanceTests
{
    private sealed record Ids(Guid Company, Guid Actor, Guid Lot, Guid Source, Guid Substance, Guid Commodity,
        Guid Rule, Guid Setting, Guid Selection, Guid Group, Guid Evaluation)
    {
        public static Ids New() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private static readonly (string Table, Func<Ids, Guid> Id)[] Tables =
    [
        ("catalog_residue_rule_versions", x => x.Rule),
        ("catalog_residue_policy_setting_versions", x => x.Setting),
        ("capture_residue_report_selections", x => x.Selection),
        ("capture_residue_source_group_versions", x => x.Group),
        ("ledger_residue_evaluations", x => x.Evaluation)
    ];

    [Fact]
    public async Task Baseline_records_survive_forward_migration_and_restricted_history_enforces_engine_cutover()
    {
        // ApiFactory owns this disposable PostgreSQL server; this database starts at the baseline migration.
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        var dbName = "cutover_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", server);
            await create.ExecuteNonQueryAsync();
        }
        var ownerString = new NpgsqlConnectionStringBuilder(factory.OwnerConnectionString) { Database = dbName }.ConnectionString;
        var runtimeString = new NpgsqlConnectionStringBuilder(factory.RuntimeConnectionString) { Database = dbName }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ownerString, x => x.MigrationsAssembly("GrainLedger.Api")
                .MigrationsHistoryTable("__EFMigrationsHistory", "grainledger")).Options;
        var ids = Ids.New();
        var before = new Dictionary<string, string>();
        await using (var db = new AppDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("20261103000000_InitialMonolith");
        await using (var owner = new NpgsqlConnection(ownerString))
        {
            await owner.OpenAsync();
            await using var transaction = await owner.BeginTransactionAsync();
            var farmer = Guid.NewGuid();
            var plot = Guid.NewGuid();
            var season = Guid.NewGuid();
            var plotSeason = Guid.NewGuid();
            var procurement = Guid.NewGuid();
            var item = Guid.NewGuid();
            foreach (var (sql, values) in new (string, Guid[])[]
            {
                ("""
                 INSERT INTO grainledger.identity_organizations ("Id","Name","Kind","CreatedAt") VALUES ($1,'Cutover customer','Customer',now())
                 """, [ids.Company]),
                ("""
                 INSERT INTO grainledger.identity_users ("Id","Provider","ProviderSubject","Email","DisplayName","Role","OrganizationId","IsActive","Version","CreatedAt","UpdatedAt")
                 VALUES ($1,'test',$1::text,'cutover@example.invalid','Cutover','CustomerManager',$2,true,gen_random_uuid(),now(),now())
                 """, [ids.Actor, ids.Company]),
                ("""
                 INSERT INTO grainledger.catalog_active_substances(id,code,name) VALUES ($1,'CUTOVER','Cutover substance')
                 """, [ids.Substance]),
                ("""
                 INSERT INTO grainledger.catalog_commodities(id,code,name) VALUES ($1,'CUTOVER','Cutover commodity')
                 """, [ids.Commodity]),
                ("""
                 INSERT INTO grainledger.ledger_commodities(id,name) VALUES ($1,'Cutover commodity')
                 """, [ids.Commodity]),
                ("""
                 INSERT INTO grainledger.capture_farmers(id,company_id,code,name,created_at) VALUES ($1,$2,'CUTOVER','Cutover farmer',now())
                 """, [farmer, ids.Company]),
                ("""
                 INSERT INTO grainledger.capture_plots(id,company_id,farmer_id,code,boundary_geojson,area_ha)
                 VALUES ($1,$2,$3,'CUTOVER','{}',1)
                 """, [plot, ids.Company, farmer]),
                ("""
                 INSERT INTO grainledger.capture_crop_seasons(id,company_id,name,starts_on,ends_on)
                 VALUES ($1,$2,'Cutover season',current_date,current_date)
                 """, [season, ids.Company]),
                ("""
                 INSERT INTO grainledger.capture_plot_seasons(id,company_id,plot_id,season_id,rice_variety)
                 VALUES ($1,$2,$3,$4,'Cutover')
                 """, [plotSeason, ids.Company, plot, season]),
                ("""
                 INSERT INTO grainledger.capture_procurements(id,company_id,code,farmer_id,collection_site_id,received_by,received_at,status)
                 VALUES ($1,$2,'CUTOVER',$3,gen_random_uuid(),$4,now(),'DRAFT')
                 """, [procurement, ids.Company, farmer, ids.Actor]),
                ("""
                 INSERT INTO grainledger.capture_procurement_items(id,company_id,procurement_id,line_no,plot_season_id,net_mass_kg,measured_value,measured_unit,moisture_percent)
                 VALUES ($1,$2,$3,1,$4,1,1,'KG',0)
                 """, [item, ids.Company, procurement, plotSeason]),
                ("""
                 INSERT INTO grainledger.capture_source_lots(id,company_id,lot_id,procurement_item_id) VALUES ($1,$2,$3,$4)
                 """, [ids.Source, ids.Company, ids.Lot, item]),
                ("""
                 INSERT INTO grainledger.ledger_lot_codes(code,origin_lot_id) VALUES ('CUTOVER',$1)
                 """, [ids.Lot]),
                ("""
                 INSERT INTO grainledger.ledger_lots(id,code,code_origin_id,owner_id,commodity_id,stage,product_identity)
                 VALUES ($1,'CUTOVER',$1,$2,$3,'RAW_PADDY','RAW_PADDY')
                 """, [ids.Lot, ids.Company, ids.Commodity])
            })
            {
                await using var setup = new NpgsqlCommand(sql, owner);
                foreach (var value in values)
                    setup.Parameters.AddWithValue(value);
                await setup.ExecuteNonQueryAsync();
            }
            await SeedLegacyAsync(owner, ids);
            await transaction.CommitAsync();
            foreach (var (table, id) in Tables)
            {
                await using var row = new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM grainledger.{table} t WHERE id=$1", owner);
                row.Parameters.AddWithValue(id(ids));
                before.Add(table, (string)(await row.ExecuteScalarAsync())!);
            }
        }
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();
        await LedgerDatabase.ConfigureRuntimeRoleAsync(ownerString, "grainledger_test_runtime");
        await using var ownerAfter = new NpgsqlConnection(ownerString); await ownerAfter.OpenAsync();
        await using var runtime = new NpgsqlConnection(runtimeString); await runtime.OpenAsync();
        foreach (var (table, id) in Tables)
        {
            await using var persisted = new NpgsqlCommand($"SELECT to_jsonb(t)::text FROM grainledger.{table} t WHERE id=$1", ownerAfter);
            persisted.Parameters.AddWithValue(id(ids));
            Assert.Equal(before[table], (string)(await persisted.ExecuteScalarAsync())!);
            foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege(current_user,$1,$2)", runtime);
                check.Parameters.AddWithValue("grainledger." + table); check.Parameters.AddWithValue(privilege);
                Assert.Equal(false, await check.ExecuteScalarAsync());
            }
            foreach (var statement in new[]
            {
                $"INSERT INTO grainledger.{table} DEFAULT VALUES",
                $"UPDATE grainledger.{table} SET id=id WHERE id=$1",
                $"DELETE FROM grainledger.{table} WHERE id=$1"
            })
            {
                await using var mutation = new NpgsqlCommand(statement, runtime);
                if (!statement.StartsWith("INSERT", StringComparison.Ordinal)) mutation.Parameters.AddWithValue(id(ids));
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync())).SqlState);
            }
        }
        var history = await CallAsync(runtime, "SELECT grainledger.ledger_residue_history($1,$2,NULL)::text", ids.Actor, ids.Lot);
        using (var parsed = JsonDocument.Parse(history!)) Assert.Empty(parsed.RootElement.GetProperty("items").EnumerateArray());
        var missing = await Assert.ThrowsAsync<PostgresException>(() => CallAsync(runtime,
            "SELECT grainledger.ledger_residue_history($1,$2,$3)::text", ids.Actor, ids.Lot, ids.Evaluation));
        Assert.Equal("P0002", missing.SqlState);
        var conflict = await Assert.ThrowsAsync<PostgresException>(() => CallAsync(runtime,
            "SELECT grainledger.ledger_residue_replay($1,$2,$3,$4::jsonb)::text", ids.Actor, ids.Lot, ids.Evaluation, "{}"));
        Assert.Equal("23505", conflict.SqlState);
        Assert.Equal("Evaluation ID belongs to an unsupported residue engine; use a new ID", conflict.MessageText);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => CallAsync(runtime,
            "SELECT grainledger.ledger_residue_history($1,$2,$3)::text", Guid.NewGuid(), ids.Lot, ids.Evaluation));
        Assert.Equal("42501", denied.SqlState);
        var newId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { id = newId, lotId = ids.Lot, companyId = ids.Company, engineVersion = "residue-flow-as-received-3" });
        var appended = await CallAsync(runtime, "SELECT grainledger.ledger_residue_append($1,$2,$3,$4::jsonb,$5::jsonb)::text", ids.Actor, ids.Lot, newId, "{}", snapshot);
        var replay = await CallAsync(runtime, "SELECT grainledger.ledger_residue_replay($1,$2,$3,$4::jsonb)::text", ids.Actor, ids.Lot, newId, "{}");
        Assert.Equal(appended, replay);
        Assert.Equal(appended, await CallAsync(runtime, "SELECT grainledger.ledger_residue_history($1,$2,$3)::text", ids.Actor, ids.Lot, newId));
        var rejectedId = Guid.NewGuid();
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => CallAsync(runtime,
            "SELECT grainledger.ledger_residue_append($1,$2,$3,$4::jsonb,$5::jsonb)::text", ids.Actor, ids.Lot, rejectedId, "{}",
            JsonSerializer.Serialize(new { id = rejectedId, lotId = ids.Lot, companyId = ids.Company, engineVersion = "old-engine" })));
        Assert.Equal("Unsupported residue engine", rejected.MessageText);
        Assert.Equal("23514", rejected.SqlState);
    }

    [Fact]
    public async Task Legacy_rows_are_hidden_by_http_and_fresh_profile_and_binding_restore_calculation()
    {
        await using var factory = new ApiFactory(); await factory.InitializeAsync();
        using var s = await CreateAsync(factory);
        var f = await ResidueScenario.CreateAsync(s);
        var sample = await SampleAsync(s, await s.ReceiptAsync("100"));
        var (_, report) = await f.ReportAsync(sample);
        var ids = Ids.New() with { Company = s.OwnerId, Actor = s.Accounts["customer"].User.Id,
            Lot = sample.LotId, Source = sample.SourceLotId, Substance = f.SubstanceId, Commodity = s.CatalogCommodityId };
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync(); await SeedLegacyAsync(owner, ids);
        }
        foreach (var (path, id) in new[]
        {
            ("residue-rules", ids.Rule), ("residue-policy-settings", ids.Setting),
            ("residue-report-selections", ids.Selection), ("residue-source-groups", ids.Group)
        })
        {
            using var list = await s.Customer.GetAsync("/api/" + path);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var parsed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.DoesNotContain(parsed.RootElement.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
            using var detail = await s.Customer.GetAsync($"/api/{path}/{id}");
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        }
        var history = await GetAsync(s.Customer, $"/api/ledger/lots/{sample.LotId}/residue-evaluations");
        Assert.DoesNotContain(history.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == ids.Evaluation);
        using (var detail = await s.Customer.GetAsync($"/api/ledger/lots/{sample.LotId}/residue-evaluations/{ids.Evaluation}"))
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        using (var replay = await s.Customer.PostAsJsonAsync($"/api/ledger/lots/{sample.LotId}/residue-evaluations", new { id = ids.Evaluation, substanceIds = new[] { f.SubstanceId } }))
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        var malformed = f.RuleBody(); malformed["inputBasis"] = "DRY_WEIGHT";
        using (var response = await s.Admin.PostAsJsonAsync("/api/residue-rules", malformed)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var legacy = f.RuleBody(); legacy["kind"] = "PROCESS"; legacy["schemaVersion"] = 1;
        using (var response = await s.Admin.PostAsJsonAsync("/api/residue-rules", legacy)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var profileId = await f.PublishRuleAsync();
        var selection = f.SelectionBody(sample, report); selection["materialBasis"] = "DRY_WEIGHT";
        using (var response = await s.Customer.PostAsJsonAsync("/api/residue-report-selections", selection)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        selection.Remove("materialBasis"); selection["schemaVersion"] = 1;
        using (var response = await s.Customer.PostAsJsonAsync("/api/residue-report-selections", selection)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var group = f.GroupBody([sample.SourceLotId], [report.GetProperty("resultSets")[0].GetProperty("results")[0].GetProperty("id").GetGuid()]);
        group["materialBasis"] = "DRY_WEIGHT";
        using (var response = await s.Customer.PostAsJsonAsync("/api/residue-source-groups", group)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        group.Remove("materialBasis"); group["schemaVersion"] = 1;
        using (var response = await s.Customer.PostAsJsonAsync("/api/residue-source-groups", group)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var selectionId = await f.SelectAsync(sample, report);
        ResidueScenario.Complete(ResidueScenario.ResultOf(await f.EvaluateAsync(sample.LotId), f.SubstanceId), 0.1m, 0.2m);
        group["schemaVersion"] = 2;
        await SendAsync(s.Customer, HttpMethod.Post, "/api/residue-source-groups", group);
        await using var ownerAfter = new NpgsqlConnection(factory.OwnerConnectionString); await ownerAfter.OpenAsync();
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString); await runtime.OpenAsync();
        foreach (var (table, id, function, actor) in new[]
        {
            ("catalog_residue_rule_versions", profileId, "catalog_publish_residue_rule", s.Accounts["admin"].User.Id),
            ("capture_residue_report_selections", selectionId, "capture_publish_residue_selection", ids.Actor),
            ("capture_residue_source_group_versions", group["id"]!.GetValue<Guid>(), "capture_publish_residue_group", ids.Actor)
        })
        {
            await using var read = new NpgsqlCommand($"SELECT payload::text FROM grainledger.{table} WHERE id=$1", ownerAfter);
            read.Parameters.AddWithValue(id);
            var original = JsonNode.Parse((string)(await read.ExecuteScalarAsync())!)!.AsObject();
            foreach (var mode in new[] { "legacy", "basis" })
            {
                var invalid = original.DeepClone().AsObject();
                if (mode == "legacy") invalid["SchemaVersion"] = 1;
                else invalid[table == "catalog_residue_rule_versions" ? "InputBasis" : "MaterialBasis"] = "DRY_WEIGHT";
                var error = await Assert.ThrowsAsync<PostgresException>(() => CallAsync(runtime,
                    $"SELECT grainledger.{function}($1,$2::jsonb)::text", actor, invalid.ToJsonString()));
                Assert.Equal("22023", error.SqlState);
            }
        }
    }

    private static async Task SeedLegacyAsync(NpgsqlConnection owner, Ids x)
    {
        foreach (var (sql, values) in new (string, Guid[])[]
        {
            ("""
             INSERT INTO grainledger.catalog_residue_rule_versions(id,code,version_no,substance_id,commodity_id,effective_from,published_by,published_at,payload)
             VALUES($1,'OLD-SERIES',1,$2,$3,now()-interval '1 year',$4,now()-interval '1 day','{"Marker":"OLD","Kind":"PROCESS","InputBasis":"DRY_WEIGHT"}')
             """, [x.Rule, x.Substance, x.Commodity, x.Actor]),
            ("""
             INSERT INTO grainledger.catalog_residue_policy_setting_versions VALUES($1,$2,'OLD-SERIES',1,now()-interval '1 year',NULL,$3,now()-interval '1 day','{"Marker":"OLD","Mode":"SYSTEM_DEFAULT"}')
             """, [x.Setting, x.Company, x.Actor]),
            ("""
             INSERT INTO grainledger.capture_residue_report_selections VALUES($1,$2,$3,$4,now()-interval '1 year',NULL,$5,now()-interval '1 day','{"Marker":"OLD","MaterialBasis":"DRY_WEIGHT"}')
             """, [x.Selection, x.Company, x.Source, x.Substance, x.Actor]),
            ("""
             INSERT INTO grainledger.capture_residue_source_group_versions VALUES($1,$2,'OLD-GROUP',1,$3,ARRAY[$4],now()-interval '1 year',NULL,$5,now()-interval '1 day','{"Marker":"OLD","MaterialBasis":"DRY_WEIGHT"}')
             """, [x.Group, x.Company, x.Substance, x.Source, x.Actor]),
            ("""
             INSERT INTO grainledger.ledger_residue_evaluations(id,lot_id,actor_id,request,snapshot) VALUES($1,$2,$3,'{}','{"engineVersion":"old-engine","basis":"DRY_WEIGHT"}')
             """, [x.Evaluation, x.Lot, x.Actor])
        })
        {
            await using var cmd = new NpgsqlCommand(sql, owner);
            foreach (var value in values)
                cmd.Parameters.AddWithValue(value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string?> CallAsync(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
            if (value is string text) command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, text);
            else command.Parameters.AddWithValue(value);
        return (string?)await command.ExecuteScalarAsync();
    }
}
