using System.Text.Json;
using GrainLedger.Api.Data;
using GrainLedger.Api.Features.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ResidueChangeFoundationTests
{
    [Fact]
    public async Task Fresh_migration_is_idempotent_and_runtime_cannot_mutate_private_history()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await using var db = Context(factory.OwnerConnectionString);
        await db.Database.MigrateAsync();
        Assert.Contains("20261123000000_ResidueChangeFoundation", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using var runtime = await Open(factory.RuntimeConnectionString);
        Assert.True((bool)(await Scalar(runtime, """
            SELECT NOT has_table_privilege(current_user,'grainledger.ledger_residue_changes','SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
             AND NOT has_table_privilege(current_user,'grainledger.ledger_residue_change_targets','SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
             AND NOT has_table_privilege(current_user,'grainledger.ledger_residue_risk_events','SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
             AND NOT has_sequence_privilege(current_user,'grainledger.ledger_residue_change_serial','USAGE,SELECT,UPDATE')
             AND has_function_privilege(current_user,'grainledger.ledger_residue_change_claim(uuid,integer,integer)','EXECUTE')
             AND has_function_privilege(current_user,'grainledger.ledger_residue_change_read(uuid,text,uuid,jsonb)','EXECUTE')
             AND NOT has_function_privilege(current_user,'grainledger.ledger_residue_target_guard()','EXECUTE')
            """))!);
        await Error(runtime, "DELETE FROM grainledger.ledger_residue_changes", "42501");
    }

    [Fact]
    public async Task Cause_deduplication_immutability_and_transaction_rollback_are_enforced()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (actor, _) = await factory.SeedUserAsync(Roles.CustomerManager, company.Id);
        await using var owner = await Open(factory.OwnerConnectionString);
        var id = await Insert(owner, company.Id, actor.Id);
        await Error(owner, """UPDATE grainledger.ledger_residue_changes SET origin='{"changed":true}' WHERE id=@id""", "23514", ("id", id));
        await Error(owner, "DELETE FROM grainledger.ledger_residue_changes WHERE id=@id", "23514", ("id", id));
        await Error(owner, """
            INSERT INTO grainledger.ledger_residue_changes(company_id,cause_kind,subject_id,subject_version,actor_id,origin)
            SELECT company_id,cause_kind,subject_id,subject_version,actor_id,origin FROM grainledger.ledger_residue_changes WHERE id=@id
            """, "23505", ("id", id));
        var rolledBack = Guid.NewGuid();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await Insert(owner, company.Id, actor.Id, rolledBack, tx);
            await tx.RollbackAsync();
        }
        Assert.Equal(0L, await Scalar(owner, "SELECT count(*) FROM grainledger.ledger_residue_changes WHERE id=@id", ("id", rolledBack)));
    }

    [Fact]
    public async Task Claims_skip_locked_jobs_and_recover_expired_leases_without_claiming_future_work()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (actor, _) = await factory.SeedUserAsync(Roles.CustomerManager, company.Id);
        await using var owner = await Open(factory.OwnerConnectionString);
        var first = await Insert(owner, company.Id, actor.Id);
        var second = await Insert(owner, company.Id, actor.Id);
        var future = await Insert(owner, company.Id, actor.Id);
        await Scalar(owner, "UPDATE grainledger.ledger_residue_changes SET due_at=clock_timestamp()+interval '1 day' WHERE id=@id RETURNING id", ("id", future));
        await using var a = await Open(factory.RuntimeConnectionString);
        await using var b = await Open(factory.RuntimeConnectionString);
        await using var tx = await a.BeginTransactionAsync();
        var claimA = await Claim(a, 1, tx);
        var claimB = await Claim(b, 10);
        Assert.Single(claimA);
        Assert.Single(claimB);
        Assert.NotEqual(claimA[0].GetProperty("id").GetGuid(), claimB[0].GetProperty("id").GetGuid());
        Assert.Equal(new[] { first, second }.Order(), claimA.Concat(claimB).Select(x => x.GetProperty("id").GetGuid()).Order());
        await tx.CommitAsync();
        Assert.Empty(await Claim(b, 10));
        await Scalar(owner, "UPDATE grainledger.ledger_residue_changes SET lease_until=clock_timestamp()-interval '1 second' WHERE id=@id RETURNING id", ("id", first));
        var recovered = Assert.Single(await Claim(b, 10));
        Assert.Equal(first, recovered.GetProperty("id").GetGuid());
        Assert.Equal(2, recovered.GetProperty("generation").GetInt32());
        Assert.Equal(2, recovered.GetProperty("attempts").GetInt32());
        await Error(b, "SELECT grainledger.ledger_residue_change_claim(@token,101,30)", "22023", ("token", Guid.NewGuid()));
    }

    [Fact]
    public async Task Reads_recheck_current_role_and_company_and_redact_internal_origin()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        var company = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var other = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (actor, _) = await factory.SeedUserAsync(Roles.CustomerManager, company.Id);
        var (foreign, _) = await factory.SeedUserAsync(Roles.ExportStaff, other.Id);
        var (purchaser, _) = await factory.SeedUserAsync(Roles.PurchasingStaff, company.Id);
        await using var owner = await Open(factory.OwnerConnectionString);
        var id = await Insert(owner, company.Id, actor.Id);
        await using var runtime = await Open(factory.RuntimeConnectionString);
        var read = JsonDocument.Parse((string)(await Scalar(runtime,
            "SELECT grainledger.ledger_residue_change_read(@a,'change',@id,'{}')::text", ("a", actor.Id), ("id", id)))!).RootElement;
        Assert.Equal(id, read.GetProperty("id").GetGuid());
        Assert.False(read.TryGetProperty("origin", out _));
        Assert.False(read.TryGetProperty("lease_id", out _));
        await Error(runtime, "SELECT grainledger.ledger_residue_change_read(@a,'change',@id,'{}')", "P0002", ("a", foreign.Id), ("id", id));
        await Error(runtime, "SELECT grainledger.ledger_residue_change_read(@a,'change',@id,'{}')", "42501", ("a", purchaser.Id), ("id", id));
        var targets = JsonDocument.Parse((string)(await Scalar(runtime,
            "SELECT grainledger.ledger_residue_change_read(@a,'targets',@id,'{}')::text", ("a", actor.Id), ("id", id)))!).RootElement;
        Assert.Equal(0, targets.GetProperty("total").GetInt32());
        var risks = JsonDocument.Parse((string)(await Scalar(runtime,
            "SELECT grainledger.ledger_residue_change_read(@a,'risks',null,'{}')::text", ("a", actor.Id)))!).RootElement;
        Assert.Empty(risks.GetProperty("items").EnumerateArray());
        await Scalar(owner, "UPDATE grainledger.identity_users SET \"IsActive\"=false WHERE \"Id\"=@a RETURNING \"Id\"", ("a", actor.Id));
        await Error(runtime, "SELECT grainledger.ledger_residue_change_read(@a,'change',@id,'{}')", "42501", ("a", actor.Id), ("id", id));
    }

    [Fact]
    public async Task Upgrade_from_previous_migration_preserves_existing_history()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await using var owner = await Open(factory.OwnerConnectionString);
        var databaseName = "residue_upgrade_" + Guid.NewGuid().ToString("N");
        await Scalar(owner, $"CREATE DATABASE {databaseName}");
        var connection = new NpgsqlConnectionStringBuilder(factory.OwnerConnectionString) { Database = databaseName }.ConnectionString;
        await using var db = Context(connection);
        await db.GetService<IMigrator>().MigrateAsync("20261118010000_ContainerTracingFlowchart");
        await using var previous = await Open(connection);
        Assert.True((bool)(await Scalar(previous, "SELECT to_regclass('grainledger.ledger_residue_changes') IS NULL"))!);
        var before = await Scalar(previous, "SELECT jsonb_agg(to_jsonb(h))::text FROM grainledger.ledger_receiving_threshold_history h");
        Assert.NotEqual(DBNull.Value, before);
        await db.Database.MigrateAsync();
        Assert.Equal(before, await Scalar(previous, "SELECT jsonb_agg(to_jsonb(h))::text FROM grainledger.ledger_receiving_threshold_history h"));
        Assert.True((bool)(await Scalar(previous, "SELECT to_regclass('grainledger.ledger_residue_changes') IS NOT NULL"))!);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Targets_validate_owner_and_evaluation_and_risks_are_append_only_and_redacted()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        using var scenario = await LedgerScenario.CreateAsync(factory);
        var lots = LedgerScenario.Lots(await scenario.ReceiptAsync("10", "20"));
        var actor = scenario.Accounts["customer"].User.Id;
        await using var owner = await Open(factory.OwnerConnectionString);
        var change = await Insert(owner, scenario.OwnerId, actor);
        var target = Guid.NewGuid();
        var risk = Guid.NewGuid();
        const string insertTarget = """
            INSERT INTO grainledger.ledger_residue_change_targets(id,change_id,company_id,target_kind,target_id,relationship_basis)
            VALUES(@id,@change,@company,'LOT',@lot,'MATERIAL') RETURNING id
            """;
        await Error(owner, insertTarget, "23514", ("id", Guid.NewGuid()), ("change", change), ("company", scenario.ForeignOwnerId), ("lot", lots[0]));
        await Scalar(owner, insertTarget, ("id", target), ("change", change), ("company", scenario.OwnerId), ("lot", lots[0]));
        var evaluation = Guid.NewGuid();
        await Scalar(owner, """
            INSERT INTO grainledger.ledger_residue_evaluations(id,lot_id,actor_id,request,snapshot)
            VALUES(@id,@lot,@actor,'{}','{"legacy":"retained"}') RETURNING id
            """, ("id", evaluation), ("lot", lots[1]), ("actor", actor));
        await Error(owner, "UPDATE grainledger.ledger_residue_change_targets SET evaluation_id=@evaluation WHERE id=@id", "23514", ("evaluation", evaluation), ("id", target));
        const string insertRisk = """
            INSERT INTO grainledger.ledger_residue_risk_events(id,target_entry_id,dependency_revision,assessment_status,snapshot_at,snapshot)
            VALUES(@id,@target,'r1',@status,clock_timestamp(),'{"findings":[],"privateFarmer":"hidden"}') RETURNING id
            """;
        await Error(owner, insertRisk, "23514", ("id", risk), ("target", target), ("status", "PASS"));
        await Scalar(owner, insertRisk, ("id", risk), ("target", target), ("status", "COMPLETE"));
        await Error(owner, insertRisk, "23505", ("id", Guid.NewGuid()), ("target", target), ("status", "COMPLETE"));
        await Error(owner, "UPDATE grainledger.ledger_residue_risk_events SET snapshot='{}' WHERE id=@id", "23514", ("id", risk));
        await Error(owner, "DELETE FROM grainledger.ledger_residue_risk_events WHERE id=@id", "23514", ("id", risk));
        await Error(owner, "TRUNCATE grainledger.ledger_residue_risk_events CASCADE", "23514");
        await using var runtime = await Open(factory.RuntimeConnectionString);
        var read = JsonDocument.Parse((string)(await Scalar(runtime,
            "SELECT grainledger.ledger_residue_change_read(@actor,'risk',@id,'{}')::text", ("actor", actor), ("id", risk)))!).RootElement;
        Assert.Equal("COMPLETE", read.GetProperty("assessmentStatus").GetString());
        Assert.False(read.GetProperty("assessment").TryGetProperty("privateFarmer", out _));
        await Error(runtime, "SELECT grainledger.ledger_residue_change_read(@actor,'risk',@id,'{}')", "P0002",
            ("actor", scenario.Accounts["foreignCustomer"].User.Id), ("id", risk));
        Assert.Equal("retained", await Scalar(owner, "SELECT snapshot->>'legacy' FROM grainledger.ledger_residue_evaluations WHERE id=@id", ("id", evaluation)));
    }

    private static AppDbContext Context(string connection) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connection, p => p.MigrationsAssembly("GrainLedger.Api").MigrationsHistoryTable("__EFMigrationsHistory", "grainledger")).Options);
    private static async Task<NpgsqlConnection> Open(string connection)
    {
        var result = new NpgsqlConnection(connection);
        await result.OpenAsync();
        return result;
    }
    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql, params (string Key, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return await command.ExecuteScalarAsync();
    }
    private static async Task Error(NpgsqlConnection connection, string sql, string code, params (string Key, object Value)[] parameters)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Scalar(connection, sql, parameters));
        Assert.Equal(code, error.SqlState);
    }
    private static async Task<Guid> Insert(NpgsqlConnection connection, Guid company, Guid actor, Guid? id = null, NpgsqlTransaction? tx = null)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO grainledger.ledger_residue_changes(id,company_id,cause_kind,subject_id,subject_version,actor_id,origin)
            VALUES(@id,@company,'REPORT_SELECTION',@subject,'1',@actor,'{"private":"not-public"}') RETURNING id
            """, connection, tx);
        command.Parameters.AddWithValue("id", id ?? Guid.NewGuid());
        command.Parameters.AddWithValue("company", company);
        command.Parameters.AddWithValue("subject", Guid.NewGuid());
        command.Parameters.AddWithValue("actor", actor);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }
    private static async Task<JsonElement[]> Claim(NpgsqlConnection connection, int size, NpgsqlTransaction? tx = null)
    {
        await using var command = new NpgsqlCommand("SELECT grainledger.ledger_residue_change_claim(@token,@size,30)::text", connection, tx);
        command.Parameters.AddWithValue("token", Guid.NewGuid());
        command.Parameters.AddWithValue("size", size);
        using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
}
