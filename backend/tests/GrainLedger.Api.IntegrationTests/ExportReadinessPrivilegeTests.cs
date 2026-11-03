using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrainLedger.Api.Features.Identity;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class ExportReadinessPrivilegeTests
{
    [Fact]
    public async Task Readiness_requires_each_table_privilege_and_endpoints_fail_closed()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();

        var organization = await factory.SeedOrganizationAsync(OrganizationKind.Customer);
        var (user, session) = await factory.SeedUserAsync(Roles.ExportStaff, organization.Id);
        using var client = factory.CreateRealCookieClient(user, session);
        await using var owner = new NpgsqlConnection(factory.OwnerConnectionString);
        await owner.OpenAsync();

        try
        {
            await SetPrivilegeAsync(owner, "REVOKE", "exports_export_orders");
            AssertSelectRetainedInsertMissing(await CheckPrivilegesAsync(factory.RuntimeConnectionString,
                "exports_export_orders"));

            using (var availability = await client.GetAsync("/api/export-availability"))
            {
                Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
                var body = await availability.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(body.GetProperty("ready").GetBoolean());
            }
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                (await client.GetAsync("/api/export-orders")).StatusCode);

            await SetPrivilegeAsync(owner, "GRANT", "exports_export_orders");
            await SetPrivilegeAsync(owner, "REVOKE", "exports_blend_plan_drafts");
            AssertSelectRetainedInsertMissing(await CheckPrivilegesAsync(factory.RuntimeConnectionString,
                "exports_blend_plan_drafts"));

            var planningResponse = await client.GetAsync(
                $"/api/export-containers/{Guid.NewGuid()}/blend-plans");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, planningResponse.StatusCode);
        }
        finally
        {
            await SetPrivilegeAsync(owner, "GRANT", "exports_export_orders");
            await SetPrivilegeAsync(owner, "GRANT", "exports_blend_plan_drafts");
        }
    }

    private static async Task SetPrivilegeAsync(NpgsqlConnection owner, string action, string table)
    {
        var statement = action == "GRANT"
            ? $"GRANT INSERT ON TABLE grainledger.{table} TO grainledger_test_runtime"
            : $"REVOKE INSERT ON TABLE grainledger.{table} FROM grainledger_test_runtime";
        await using var command = new NpgsqlCommand(statement, owner);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(bool Select, bool Insert)> CheckPrivilegesAsync(string connectionString, string table)
    {
        await using var runtime = new NpgsqlConnection(connectionString);
        await runtime.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SELECT has_table_privilege(current_user, 'grainledger.{table}', 'SELECT'),
                   has_table_privilege(current_user, 'grainledger.{table}', 'INSERT')
            """, runtime);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static void AssertSelectRetainedInsertMissing((bool Select, bool Insert) privileges)
    {
        Assert.True(privileges.Select);
        Assert.False(privileges.Insert);
    }
}
