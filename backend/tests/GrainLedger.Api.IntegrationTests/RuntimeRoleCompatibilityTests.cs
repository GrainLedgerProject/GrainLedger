using GrainLedger.Api.Features.Ledger;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class RuntimeRoleCompatibilityTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Configuration_preserves_residue_and_installed_container_read_contracts(bool history, bool closedMass)
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();
        // The merged baseline installs both optional reads. Remove them only
        // in this isolated database to exercise partial deployments as well.
        await ExecuteAsync("DROP FUNCTION IF EXISTS grainledger.ledger_business_history(uuid,jsonb)");
        await ExecuteAsync("DROP FUNCTION IF EXISTS grainledger.ledger_closed_order_kg(uuid,uuid)");
        // Signature-only stubs model an independently deployed feature. Its
        // business authorization is outside this runtime-grant regression.
        if (history) await ExecuteAsync("CREATE FUNCTION grainledger.ledger_business_history(uuid,jsonb) RETURNS jsonb LANGUAGE sql AS 'SELECT null::jsonb'");
        if (closedMass) await ExecuteAsync("CREATE FUNCTION grainledger.ledger_closed_order_kg(uuid,uuid) RETURNS numeric LANGUAGE sql AS 'SELECT 0::numeric'");
        await ExecuteAsync("CREATE FUNCTION grainledger.runtime_compatibility_private_probe() RETURNS integer LANGUAGE sql AS 'SELECT 1'");

        await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);
        var entryPoints = new List<string>
        {
            "grainledger.ledger_residue_change_claim(uuid,integer,integer)",
            "grainledger.ledger_residue_change_read(uuid,text,uuid,jsonb)",
            "grainledger.ledger_residue_job(uuid,uuid,integer,text,uuid,jsonb)",
            "grainledger.exports_residue_job(uuid,uuid,integer,text,uuid,jsonb)",
            "grainledger.exports_residue_freshness_read(uuid,text,uuid,text)"
        };
        if (history) entryPoints.Add("grainledger.ledger_business_history(uuid,jsonb)");
        if (closedMass) entryPoints.Add("grainledger.ledger_closed_order_kg(uuid,uuid)");
        foreach (var signature in entryPoints)
        {
            await using var check = new NpgsqlCommand("SELECT has_function_privilege('grainledger_test_runtime',to_regprocedure($1),'EXECUTE')", owner);
            check.Parameters.AddWithValue(signature);
            Assert.Equal(true, await check.ExecuteScalarAsync());
        }

        // Missing required F12 execution is still rejected, then repaired by
        // explicit owner configuration without stripping the installed reads.
        await ExecuteAsync("REVOKE EXECUTE ON FUNCTION grainledger.ledger_residue_job(uuid,uuid,integer,text,uuid,jsonb) FROM grainledger_test_runtime");
        await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
        await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);

        // Compatibility never admits arbitrary public/private helper grants.
        await ExecuteAsync("GRANT EXECUTE ON FUNCTION grainledger.runtime_compatibility_private_probe() TO grainledger_test_runtime");
        await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
        await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);

        async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, owner);
            await command.ExecuteNonQueryAsync();
        }
    }
}
