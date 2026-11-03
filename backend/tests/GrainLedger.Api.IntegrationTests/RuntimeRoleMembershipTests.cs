using GrainLedger.Api.Features.Ledger;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class RuntimeRoleMembershipTests
{
    [Theory]
    [InlineData("direct", false)]
    [InlineData("direct", true)]
    [InlineData("multihop", false)]
    [InlineData("multihop", true)]
    [InlineData("inherited", false)]
    [InlineData("inherited", true)]
    public async Task Settable_roles_cannot_bypass_protected_privileges(string path, bool helper)
    {
        var factory = new ApiFactory();
        try
        {
            await factory.InitializeAsync();
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            var memberships = path switch
            {
                "direct" => "GRANT unsafe_member TO grainledger_test_runtime WITH INHERIT FALSE, SET TRUE;",
                "multihop" => "GRANT unsafe_member TO intermediate_member WITH INHERIT FALSE, SET TRUE; GRANT intermediate_member TO grainledger_test_runtime WITH INHERIT FALSE, SET TRUE;",
                "inherited" => "GRANT unsafe_member TO intermediate_member WITH INHERIT TRUE, SET FALSE; GRANT intermediate_member TO grainledger_test_runtime WITH INHERIT FALSE, SET TRUE;",
                _ => throw new ArgumentOutOfRangeException(nameof(path))
            };
            var grant = helper
                ? "GRANT EXECUTE ON FUNCTION grainledger.ledger_pool_ingress(uuid,uuid,text,uuid,uuid,numeric) TO unsafe_member;"
                : "GRANT UPDATE ON grainledger.ledger_postings TO unsafe_member;";
            var privilege = helper
                ? "has_function_privilege(current_user,'grainledger.ledger_pool_ingress(uuid,uuid,text,uuid,uuid,numeric)','EXECUTE')"
                : "has_table_privilege(current_user,'grainledger.ledger_postings','UPDATE')";
            await using (var setup = new NpgsqlCommand($"""
                CREATE ROLE unsafe_member NOLOGIN;
                CREATE ROLE intermediate_member NOLOGIN;
                GRANT USAGE ON SCHEMA grainledger TO unsafe_member, intermediate_member;
                {grant}
                {memberships}
                """, owner))
                await setup.ExecuteNonQueryAsync();

            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
            await runtime.OpenAsync();
            await using (var before = new NpgsqlCommand($"SELECT NOT {privilege}", runtime))
                Assert.Equal(true, await before.ExecuteScalarAsync());
            var target = path == "inherited" ? "intermediate_member" : "unsafe_member";
            await using (var effective = new NpgsqlCommand($"""
                SET ROLE {target};
                SELECT {privilege};
                """, runtime))
                Assert.Equal(true, await effective.ExecuteScalarAsync());

            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime"));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("settable")]
    [InlineData("inherited")]
    [InlineData("admin_multihop")]
    public async Task Admin_option_can_enable_set_role_and_is_rejected(string path)
    {
        var factory = new ApiFactory();
        try
        {
            await factory.InitializeAsync();
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            var memberships = path switch
            {
                "direct" => "GRANT unsafe_member TO grainledger_test_runtime WITH ADMIN TRUE, INHERIT FALSE, SET FALSE;",
                "settable" => "GRANT unsafe_member TO intermediate_member WITH ADMIN TRUE, INHERIT FALSE, SET FALSE; GRANT intermediate_member TO grainledger_test_runtime WITH INHERIT FALSE, SET TRUE;",
                "inherited" => "GRANT unsafe_member TO intermediate_member WITH ADMIN TRUE, INHERIT FALSE, SET FALSE; GRANT intermediate_member TO grainledger_test_runtime WITH INHERIT TRUE, SET FALSE;",
                "admin_multihop" => "GRANT unsafe_member TO intermediate_member WITH INHERIT FALSE, SET TRUE; GRANT intermediate_member TO grainledger_test_runtime WITH ADMIN TRUE, INHERIT FALSE, SET FALSE;",
                _ => throw new ArgumentOutOfRangeException(nameof(path))
            };
            await using (var setup = new NpgsqlCommand($"""
                CREATE ROLE unsafe_member NOLOGIN;
                CREATE ROLE intermediate_member NOLOGIN;
                GRANT USAGE ON SCHEMA grainledger TO unsafe_member;
                GRANT UPDATE ON grainledger.ledger_postings TO unsafe_member;
                {memberships}
                """, owner))
                await setup.ExecuteNonQueryAsync();

            await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
            await runtime.OpenAsync();
            await using (var transaction = await runtime.BeginTransactionAsync())
            {
                if (path == "settable")
                {
                    await using var setIntermediate = new NpgsqlCommand("SET ROLE intermediate_member", runtime, transaction);
                    await setIntermediate.ExecuteNonQueryAsync();
                }
                var grantedRole = path == "admin_multihop" ? "intermediate_member" : "unsafe_member";
                await using var exploit = new NpgsqlCommand($"""
                    GRANT {grantedRole} TO grainledger_test_runtime WITH SET TRUE;
                    SET ROLE unsafe_member;
                    SELECT has_table_privilege(current_user,'grainledger.ledger_postings','UPDATE');
                    """, runtime, transaction);
                Assert.Equal(true, await exploit.ExecuteScalarAsync());
                await transaction.RollbackAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString));
            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime"));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Startup_role_cannot_hide_unsafe_session_identity()
    {
        var factory = new ApiFactory();
        try
        {
            await factory.InitializeAsync();
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            await using (var create = new NpgsqlCommand("CREATE ROLE safe_current NOLOGIN", owner))
                await create.ExecuteNonQueryAsync();
            await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "safe_current");
            await using (var setup = new NpgsqlCommand("""
                GRANT safe_current TO grainledger_test_runtime WITH INHERIT FALSE, SET TRUE;
                GRANT UPDATE ON grainledger.ledger_postings TO grainledger_test_runtime;
                ALTER ROLE grainledger_test_runtime SET role = 'safe_current';
                """, owner))
                await setup.ExecuteNonQueryAsync();

            var connectionString = new NpgsqlConnectionStringBuilder(factory.RuntimeConnectionString) { Pooling = false }.ConnectionString;
            await using var runtime = new NpgsqlConnection(connectionString);
            await runtime.OpenAsync();
            await using (var before = new NpgsqlCommand("""
                SELECT session_user = 'grainledger_test_runtime'
                    AND current_user = 'safe_current'
                    AND NOT has_table_privilege(current_user,'grainledger.ledger_postings','UPDATE');
                """, runtime))
                Assert.Equal(true, await before.ExecuteScalarAsync());
            await using (var reset = new NpgsqlCommand("""
                SET ROLE NONE;
                SELECT current_user = session_user
                    AND has_table_privilege(current_user,'grainledger.ledger_postings','UPDATE');
                """, runtime))
                Assert.Equal(true, await reset.ExecuteScalarAsync());
            await using (var restore = new NpgsqlCommand("RESET ROLE; SELECT current_user = 'safe_current'", runtime))
                Assert.Equal(true, await restore.ExecuteScalarAsync());

            await Assert.ThrowsAsync<InvalidOperationException>(() => LedgerDatabase.ValidateRuntimeAsync(connectionString));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Inaccessible_privileges_and_safe_settable_role_do_not_cause_rejection()
    {
        var factory = new ApiFactory();
        try
        {
            await factory.InitializeAsync();
            await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
            await owner.OpenAsync();
            await using (var setup = new NpgsqlCommand("""
                CREATE ROLE inaccessible_member NOLOGIN;
                CREATE ROLE safe_member NOLOGIN;
                CREATE ROLE safe_admin NOLOGIN;
                GRANT USAGE ON SCHEMA grainledger TO safe_member;
                GRANT SELECT ON grainledger.ledger_postings TO safe_member;
                GRANT safe_member TO grainledger_test_runtime WITH INHERIT FALSE, SET TRUE;
                GRANT SELECT ON grainledger.ledger_postings TO safe_admin;
                GRANT safe_admin TO grainledger_test_runtime WITH ADMIN TRUE, INHERIT FALSE, SET FALSE;
                GRANT UPDATE ON grainledger.ledger_postings TO inaccessible_member;
                GRANT EXECUTE ON FUNCTION grainledger.ledger_pool_ingress(uuid,uuid,text,uuid,uuid,numeric) TO inaccessible_member;
                GRANT inaccessible_member TO grainledger_test_runtime WITH INHERIT FALSE, SET FALSE;
                """, owner))
                await setup.ExecuteNonQueryAsync();
            await LedgerDatabase.ValidateRuntimeAsync(factory.RuntimeConnectionString);
            await LedgerDatabase.ConfigureRuntimeRoleAsync(factory.OwnerConnectionString, "grainledger_test_runtime");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }
}
