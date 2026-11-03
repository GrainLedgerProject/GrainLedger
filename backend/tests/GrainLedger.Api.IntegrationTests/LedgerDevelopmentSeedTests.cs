using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using Npgsql;
using Xunit;

namespace GrainLedger.Api.IntegrationTests;

public sealed class LedgerDevelopmentSeedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "LedgerSeed!Password123";
    private static Guid Id(int value) => Guid.Parse($"6c656467-6572-4000-8000-{value:x12}");

    [Fact]
    public async Task Cli_seed_creates_official_receipt_and_setup_then_rerun_preserves_manual_edits()
    {
        var run = await RunCliAsync("Development", "--seed-ledger");
        Assert.True(run.ExitCode == 0, $"CLI failed ({run.ExitCode}).\n{run.Output}");

        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("""
            SELECT p."Role", c.code, mu.code, cap.status,
                   cap_item.net_mass_kg, cap_item.measured_value, cap_item.measured_unit,
                   lot.id, lot.stage, lot.product_identity, lot.receipt_id, lot.receipt_line_id,
                   pos.quantity_kg, ev.kind, ev.details->>'captureEventId', ev.details->>'farmerId',
                   ev.details->>'plotSeasonId', ev.details->>'seasonId',
                   post.quantity_kg, post.location_id, src.lot_id, contribution.quantity_kg,
                   cap_event.event_type, cap_event.actor_id, ps.season_id, plot.farmer_id
            FROM grainledger.ledger_facilities f
            JOIN grainledger.ledger_locations l ON l.facility_id=f.id AND l.id=@location
            JOIN grainledger.ledger_equipment e ON e.facility_id=f.id AND e.id=@dryer
            JOIN grainledger.ledger_assignments a ON a.facility_id=f.id
            JOIN grainledger.identity_users p ON p."Id"=a.user_id AND p."Role"='MillOperator'
            JOIN grainledger.catalog_commodities c ON c.code='PADDY'
            JOIN grainledger.catalog_measurement_units mu ON mu.code='KG'
            JOIN grainledger.capture_procurements cap ON cap.id=@receipt
            JOIN grainledger.capture_procurement_items cap_item ON cap_item.procurement_id=cap.id AND cap_item.id=@line
            JOIN grainledger.capture_plot_seasons ps ON ps.id=cap_item.plot_season_id AND ps.company_id=cap.company_id
            JOIN grainledger.capture_plots plot ON plot.id=ps.plot_id AND plot.company_id=ps.company_id
            JOIN grainledger.ledger_lots lot ON lot.id=@lot AND lot.receipt_id=cap.id AND lot.receipt_line_id=cap_item.id
            JOIN grainledger.ledger_positions pos ON pos.lot_id=lot.id AND pos.location_id=l.id
            JOIN grainledger.ledger_events ev ON ev.lot_id=lot.id AND ev.kind='RECEIPT'
            JOIN grainledger.ledger_postings post ON post.event_id=ev.id AND post.lot_id=lot.id
            JOIN grainledger.capture_source_lots src ON src.procurement_item_id=cap_item.id AND src.lot_id=lot.id
            JOIN grainledger.ledger_contributions contribution ON contribution.lot_id=lot.id AND contribution.source_lot_id=lot.id
            JOIN grainledger.capture_business_events cap_event ON cap_event.id=(ev.details->>'captureEventId')::uuid
                AND cap_event.company_id=cap.company_id AND cap_event.actor_id=cap.received_by
                AND cap_event.event_type='PROCUREMENT_COMMITTED'
                AND c.id::text=cap_event.payload->'result'->>'CommodityId'
            WHERE f.id=@facility AND lot.owner_id=cap.company_id
                AND EXISTS (SELECT 1 FROM grainledger.ledger_equipment WHERE id=@mill AND facility_id=f.id)
                AND EXISTS (SELECT 1 FROM grainledger.ledger_locations WHERE id=@dryStore AND facility_id=f.id)
                AND EXISTS (SELECT 1 FROM grainledger.ledger_locations WHERE id=@isolation AND facility_id=f.id)
                AND EXISTS (SELECT 1 FROM grainledger.ledger_assignments ma JOIN grainledger.identity_users u ON u."Id"=ma.user_id
                    WHERE ma.facility_id=f.id AND u."Role"='MillManager')
            """, connection))
        {
            command.Parameters.AddWithValue("facility", Id(1));
            command.Parameters.AddWithValue("location", Id(2));
            command.Parameters.AddWithValue("dryer", Id(5));
            command.Parameters.AddWithValue("mill", Id(6));
            command.Parameters.AddWithValue("dryStore", Id(3));
            command.Parameters.AddWithValue("isolation", Id(4));
            command.Parameters.AddWithValue("receipt", Id(20));
            command.Parameters.AddWithValue("line", Id(21));
            command.Parameters.AddWithValue("lot", Id(22));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "Seed must create assigned mill setup and official received stock.");
            Assert.Equal("MillOperator", reader.GetString(0));
            Assert.Equal("PADDY", reader.GetString(1));
            Assert.Equal("KG", reader.GetString(2));
            Assert.Equal("COMMITTED", reader.GetString(3));
            Assert.Equal(600m, reader.GetDecimal(4));
            Assert.Equal(600m, reader.GetDecimal(5));
            Assert.Equal("KG", reader.GetString(6));
            Assert.Equal(Id(22), reader.GetGuid(7));
            Assert.Equal("RAW_PADDY", reader.GetString(8));
            Assert.Equal("RAW_PADDY", reader.GetString(9));
            Assert.Equal(Id(20), reader.GetGuid(10));
            Assert.Equal(Id(21), reader.GetGuid(11));
            Assert.Equal(600m, reader.GetDecimal(12));
            Assert.Equal("RECEIPT", reader.GetString(13));
            Assert.False(string.IsNullOrWhiteSpace(reader.GetString(14)));
            Assert.Equal(Id(10).ToString(), reader.GetString(15));
            Assert.Equal(Id(12).ToString(), reader.GetString(16));
            Assert.Equal(reader.GetGuid(24).ToString(), reader.GetString(17));
            Assert.Equal(600m, reader.GetDecimal(18));
            Assert.Equal(Id(2), reader.GetGuid(19));
            Assert.Equal(Id(22), reader.GetGuid(20));
            Assert.Equal(600m, reader.GetDecimal(21));
            Assert.Equal("PROCUREMENT_COMMITTED", reader.GetString(22));
            Assert.NotEqual(Guid.Empty, reader.GetGuid(23));
            Assert.Equal(Id(10), reader.GetGuid(25));
        }
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "mill.operator@grainledger.local",
            password = Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        using var transfer = await client.PostAsJsonAsync("/api/ledger/transfers", new
        {
            id = Guid.NewGuid(), lotId = Id(22), fromLocationId = Id(2), toLocationId = Id(3),
            quantityKg = "100", occurredAt = DateTimeOffset.UtcNow
        });
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        await using (var command = new NpgsqlCommand(
            "UPDATE grainledger.ledger_facilities SET name='Manual facility name' WHERE id=@id", connection))
        {
            command.Parameters.AddWithValue("id", Id(1));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        run = await RunCliAsync("Development", "--seed-ledger");
        Assert.True(run.ExitCode == 0, $"CLI rerun failed ({run.ExitCode}).\n{run.Output}");
        await using (var command = new NpgsqlCommand("""
            SELECT f.name,
                   (SELECT quantity_kg FROM grainledger.ledger_positions WHERE lot_id=@lot AND location_id=@location),
                   (SELECT quantity_kg FROM grainledger.ledger_positions WHERE lot_id=@lot AND location_id=@dryStore),
                   (SELECT count(*) FROM grainledger.ledger_lots WHERE receipt_id=@receipt),
                   (SELECT count(*) FROM grainledger.ledger_events e JOIN grainledger.ledger_lots l ON l.id=e.lot_id WHERE l.receipt_id=@receipt AND e.kind='RECEIPT'),
                   (SELECT count(*) FROM grainledger.ledger_postings p JOIN grainledger.ledger_events e ON e.id=p.event_id WHERE p.lot_id=@lot AND e.kind='RECEIPT')
            FROM grainledger.ledger_facilities f WHERE f.id=@facility
            """, connection))
        {
            command.Parameters.AddWithValue("facility", Id(1));
            command.Parameters.AddWithValue("location", Id(2));
            command.Parameters.AddWithValue("dryStore", Id(3));
            command.Parameters.AddWithValue("receipt", Id(20));
            command.Parameters.AddWithValue("lot", Id(22));
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Manual facility name", reader.GetString(0));
            Assert.Equal(500m, reader.GetDecimal(1));
            Assert.Equal(100m, reader.GetDecimal(2));
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.Equal(1L, reader.GetInt64(4));
            Assert.Equal(1L, reader.GetInt64(5));
        }
    }

    [Fact]
    public async Task Cli_rejects_non_development_and_mixed_flags_without_database_writes()
    {
        var before = await DatabaseCountsAsync();
        var production = await RunCliAsync("Production", "--seed-ledger");
        Assert.NotEqual(0, production.ExitCode);
        Assert.Contains("Development", production.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await DatabaseCountsAsync());

        var mixed = await RunCliAsync("Development", "--migrate", "--seed-ledger");
        Assert.NotEqual(0, mixed.ExitCode);
        Assert.Contains("--migrate", mixed.Output, StringComparison.Ordinal);
        Assert.Contains("--seed-ledger", mixed.Output, StringComparison.Ordinal);
        Assert.Equal(before, await DatabaseCountsAsync());
    }

    [Fact]
    public async Task Cli_setup_seed_creates_only_accounts_and_mill_setup_and_preserves_edits()
    {
        await using var freshFactory = new ApiFactory();
        await freshFactory.InitializeAsync();

        var run = await RunCliAsync(freshFactory, "Development", Password, "--seed-setup");
        Assert.True(run.ExitCode == 0, $"CLI failed ({run.ExitCode}).\n{run.Output}");

        await using (var connection = new NpgsqlConnection(freshFactory.OwnerConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT
                    (SELECT count(*) FROM grainledger.identity_organizations),
                    (SELECT count(*) FROM grainledger.identity_users WHERE "Provider"='local'),
                    (SELECT count(*) FROM grainledger.ledger_facilities),
                    (SELECT count(*) FROM grainledger.ledger_locations),
                    (SELECT count(*) FROM grainledger.ledger_equipment),
                    (SELECT count(*) FROM grainledger.ledger_assignments),
                    (SELECT count(*) FROM grainledger.ledger_policies),
                    (SELECT count(*) FROM grainledger.ledger_compatibilities),
                    (SELECT count(*) FROM grainledger.ledger_mixed_bulk_policies),
                    (SELECT count(*) FROM grainledger.ledger_evidence),
                    (SELECT count(*) FROM grainledger.capture_farmers),
                    (SELECT count(*) FROM grainledger.capture_plots),
                    (SELECT count(*) FROM grainledger.capture_crop_seasons),
                    (SELECT count(*) FROM grainledger.capture_plot_seasons),
                    (SELECT count(*) FROM grainledger.capture_procurements),
                    (SELECT count(*) FROM grainledger.capture_lab_reports),
                    (SELECT count(*) FROM grainledger.ledger_lots),
                    (SELECT count(*) FROM grainledger.ledger_positions),
                    (SELECT count(*) FROM grainledger.ledger_postings),
                    (SELECT count(*) FROM grainledger.exports_export_orders),
                    (SELECT count(*) FROM grainledger.ledger_receiving_threshold_default),
                    (SELECT count(*) FROM grainledger.ledger_receiving_threshold_history),
                    (SELECT count(*) FROM grainledger.catalog_commodities WHERE code='PADDY'),
                    (SELECT count(*) FROM grainledger.catalog_measurement_units WHERE code='KG'),
                    (SELECT percentage FROM grainledger.ledger_receiving_threshold_default WHERE singleton),
                    (SELECT count(*) FROM (VALUES
                        ('admin@grainledger.local', 'Administrator'),
                        ('mill.manager@grainledger.local', 'MillManager'),
                        ('mill.operator@grainledger.local', 'MillOperator'),
                        ('customer.manager@grainledger.local', 'CustomerManager'),
                        ('export@grainledger.local', 'ExportStaff'),
                        ('purchasing@grainledger.local', 'PurchasingStaff')
                    ) AS expected(email, role)
                    WHERE EXISTS (SELECT 1 FROM grainledger.identity_users u
                        WHERE u."Provider"='local' AND lower(u."Email")=expected.email AND u."Role"=expected.role))
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2L, reader.GetInt64(0));
            Assert.Equal(6L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(3L, reader.GetInt64(3));
            Assert.Equal(2L, reader.GetInt64(4));
            Assert.Equal(2L, reader.GetInt64(5));
            for (var i = 6; i <= 19; i++) Assert.Equal(0L, reader.GetInt64(i));
            Assert.Equal(1L, reader.GetInt64(20));
            Assert.Equal(1L, reader.GetInt64(21));
            Assert.Equal(0L, reader.GetInt64(22));
            Assert.Equal(0L, reader.GetInt64(23));
            Assert.Equal(2m, reader.GetDecimal(24));
            Assert.Equal(6L, reader.GetInt64(25));
        }

        using (var client = freshFactory.CreateClient())
        using (var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@grainledger.local",
            password = Password
        }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await using (var connection = new NpgsqlConnection(freshFactory.OwnerConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE grainledger.ledger_facilities SET name='Manual setup name' WHERE id=@id", connection);
            command.Parameters.AddWithValue("id", Id(1));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        run = await RunCliAsync(freshFactory, "Development", Password, "--seed-setup");
        Assert.True(run.ExitCode == 0, $"CLI rerun failed ({run.ExitCode}).\n{run.Output}");
        await using (var connection = new NpgsqlConnection(freshFactory.OwnerConnectionString))
        await using (var command = new NpgsqlCommand("""
            SELECT f.name,
                   (SELECT count(*) FROM grainledger.identity_organizations),
                   (SELECT count(*) FROM grainledger.identity_users WHERE "Provider"='local'),
                   (SELECT count(*) FROM grainledger.ledger_lots),
                   (SELECT count(*) FROM grainledger.ledger_postings),
                   (SELECT count(*) FROM grainledger.ledger_locations),
                   (SELECT count(*) FROM grainledger.ledger_equipment),
                   (SELECT count(*) FROM grainledger.ledger_assignments)
            FROM grainledger.ledger_facilities f WHERE f.id=@id
            """, connection))
        {
            command.Parameters.AddWithValue("id", Id(1));
            await connection.OpenAsync();
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Manual setup name", reader.GetString(0));
            Assert.Equal(2L, reader.GetInt64(1));
            Assert.Equal(6L, reader.GetInt64(2));
            Assert.Equal(0L, reader.GetInt64(3));
            Assert.Equal(0L, reader.GetInt64(4));
            Assert.Equal(3L, reader.GetInt64(5));
            Assert.Equal(2L, reader.GetInt64(6));
            Assert.Equal(2L, reader.GetInt64(7));
        }
        using var verifyClient = freshFactory.CreateClient();
        foreach (var (email, role) in new[]
        {
            ("admin@grainledger.local", "Administrator"),
            ("mill.operator@grainledger.local", "MillOperator")
        })
        {
            using var login = await verifyClient.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var body = await login.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(role, body.GetProperty("user").GetProperty("role").GetString());
        }

    }

    [Fact]
    public async Task Cli_setup_seed_rejects_invalid_environment_password_and_mixed_flags_without_writes()
    {
        await using var freshFactory = new ApiFactory();
        await freshFactory.InitializeAsync();
        var before = await SetupDatabaseCountsAsync(freshFactory);

        var production = await RunCliAsync(freshFactory, "Production", Password, "--seed-setup");
        Assert.NotEqual(0, production.ExitCode);
        Assert.Contains("--seed-setup is only available in Development.", production.Output, StringComparison.Ordinal);
        Assert.Equal(before, await SetupDatabaseCountsAsync(freshFactory));

        foreach (var password in new string?[] { null, "weak" })
        {
            var invalidPassword = await RunCliAsync(freshFactory, "Development", password, "--seed-setup");
            Assert.NotEqual(0, invalidPassword.ExitCode);
            Assert.Contains("DevelopmentSeed__Password", invalidPassword.Output, StringComparison.Ordinal);
            Assert.Equal(before, await SetupDatabaseCountsAsync(freshFactory));
        }

        var mixed = await RunCliAsync(freshFactory, "Development", Password, "--migrate", "--seed-setup");
        Assert.NotEqual(0, mixed.ExitCode);
        Assert.Contains("--seed-setup", mixed.Output, StringComparison.Ordinal);
        Assert.Equal(before, await SetupDatabaseCountsAsync(freshFactory));
    }

    private Task<(int ExitCode, string Output)> RunCliAsync(string environment, params string[] arguments) =>
        RunCliAsync(factory, environment, Password, arguments);

    private async Task<(int ExitCode, string Output)> RunCliAsync(ApiFactory targetFactory, string environment,
        string? password, params string[] arguments)
    {
        var apiPath = typeof(Program).Assembly.Location;
        var testAssemblyPath = typeof(LedgerDevelopmentSeedTests).Assembly.Location;
        var baseDirectory = AppContext.BaseDirectory;
        var runtimeConfig = Path.Combine(baseDirectory, $"{Path.GetFileNameWithoutExtension(testAssemblyPath)}.runtimeconfig.json");
        var depsFile = Path.Combine(baseDirectory, $"{Path.GetFileNameWithoutExtension(testAssemblyPath)}.deps.json");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = baseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(runtimeConfig);
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(depsFile);
        start.ArgumentList.Add(apiPath);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__GrainLedger"] = targetFactory.RuntimeConnectionString;
        start.Environment["ConnectionStrings__GrainLedgerMigration"] = targetFactory.OwnerConnectionString;
        start.Environment["Ledger__RuntimeRole"] = "grainledger_test_runtime";
        start.Environment["DevelopmentSeed__Password"] = password ?? "";
        start.Environment["Authentication__Jwt__SigningKey"] = "integration-test-only-signing-key-at-least-thirty-two-bytes";
        start.Environment["Authentication__Jwt__Issuer"] = "GrainLedger";
        start.Environment["Authentication__Jwt__Audience"] = "GrainLedger.Api";
        start.Environment.Remove("ASPNETCORE_URLS");
        start.Environment.Remove("ASPNETCORE_HTTPS_PORT");
        start.Environment.Remove("ASPNETCORE_HTTP_PORTS");
        start.Environment.Remove("ASPNETCORE_HTTPS_PORTS");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start API seed CLI.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("API seed CLI exceeded 90 seconds.");
        }
        return (process.ExitCode, $"{await stdout}\n{await stderr}");
    }

    private static async Task<string> SetupDatabaseCountsAsync(ApiFactory targetFactory)
    {
        await using var connection = new NpgsqlConnection(targetFactory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT concat_ws(':',
                (SELECT count(*) FROM grainledger.identity_organizations),
                (SELECT count(*) FROM grainledger.identity_users),
                (SELECT count(*) FROM grainledger.ledger_facilities),
                (SELECT count(*) FROM grainledger.ledger_locations),
                (SELECT count(*) FROM grainledger.ledger_equipment),
                (SELECT count(*) FROM grainledger.ledger_assignments),
                (SELECT count(*) FROM grainledger.ledger_policies),
                (SELECT count(*) FROM grainledger.ledger_evidence),
                (SELECT count(*) FROM grainledger.ledger_lots),
                (SELECT count(*) FROM grainledger.ledger_positions),
                (SELECT count(*) FROM grainledger.ledger_postings),
                (SELECT count(*) FROM grainledger.capture_procurements),
                (SELECT count(*) FROM grainledger.capture_lab_reports),
                (SELECT count(*) FROM grainledger.exports_export_orders))
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> DatabaseCountsAsync()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT concat_ws(':',
                (SELECT count(*) FROM grainledger.identity_organizations),
                (SELECT count(*) FROM grainledger.identity_users),
                (SELECT count(*) FROM grainledger.ledger_facilities),
                (SELECT count(*) FROM grainledger.ledger_locations),
                (SELECT count(*) FROM grainledger.ledger_equipment),
                (SELECT count(*) FROM grainledger.ledger_assignments),
                (SELECT count(*) FROM grainledger.ledger_evidence),
                (SELECT count(*) FROM grainledger.ledger_lots),
                (SELECT count(*) FROM grainledger.ledger_positions),
                (SELECT count(*) FROM grainledger.ledger_postings),
                (SELECT count(*) FROM grainledger.capture_procurements),
                (SELECT count(*) FROM grainledger.capture_procurement_items),
                (SELECT count(*) FROM grainledger.capture_business_events),
                (SELECT count(*) FROM grainledger.capture_source_lots),
                (SELECT count(*) FROM grainledger.capture_farmers),
                (SELECT count(*) FROM grainledger.capture_plots),
                (SELECT count(*) FROM grainledger.capture_crop_seasons),
                (SELECT count(*) FROM grainledger.capture_plot_seasons),
                (SELECT count(*) FROM grainledger.capture_offline_operations),
                (SELECT count(*) FROM grainledger.catalog_commodities),
                (SELECT count(*) FROM grainledger.catalog_measurement_units))
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
