using System.Text.Json;
using Npgsql;
using Xunit;
using static GrainLedger.Api.IntegrationTests.LedgerScenario;

namespace GrainLedger.Api.IntegrationTests;

public sealed class MockCaptureIsolationTests
{
    [Fact]
    public async Task Mock_migrations_and_farmer_receipt_workflow_leave_incompatible_capture_schema_untouched()
    {
        var factory = new ApiFactory { SeedIncompatibleLegacyCapture = true };
        await factory.InitializeAsync();
        try
        {
            using var scenario = await CreateAsync(factory);
            var status = await GetAsync(scenario.Purchaser, "/api/capture-mock/status");
            Assert.True(status.GetProperty("synthetic").GetBoolean());
            var farmer = await GetAsync(scenario.Purchaser, "/api/capture-mock/farmers");
            Assert.Contains(farmer.EnumerateArray(), row => row.GetProperty("id").GetGuid() == scenario.FarmerId);
            var receipt = await scenario.ReceiptAsync("5");
            Assert.Equal(5m, Quantity(await scenario.LotAsync(Lots(receipt)[0])));

            await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT legacy_key, legacy_payload::text FROM capture.farmers", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("untouched", reader.GetString(0));
            Assert.Equal("{\"source\": \"teammate\"}", reader.GetString(1));
            Assert.False(await reader.ReadAsync());
            await reader.DisposeAsync();
            await using var columns = new NpgsqlCommand("SELECT string_agg(column_name || ':' || data_type, ',' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_schema='capture' AND table_name='farmers'", connection);
            Assert.Equal("legacy_key:text,legacy_payload:jsonb", await columns.ExecuteScalarAsync());
            await using var history = new NpgsqlCommand("SELECT to_regclass('capture_mock.\"__EFMigrationsHistory\"') IS NOT NULL", connection);
            Assert.True((bool)(await history.ExecuteScalarAsync())!);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }
}
