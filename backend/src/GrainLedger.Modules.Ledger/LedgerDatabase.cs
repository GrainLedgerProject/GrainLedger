using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GrainLedger.Modules.Ledger;

public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options);

[DbContext(typeof(LedgerDbContext))]
[Migration("202610090001_InitialLedger")]
public sealed class InitialLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "Schema", "CodeIdentity", "Reads", "Operations", "Receipts", "Commands", "CorrectionBinding", "ReceiptBoundary" })
        {
            using var stream = typeof(InitialLedger).Assembly.GetManifestResourceStream($"GrainLedger.Modules.Ledger.Sql.{name}.sql")!;
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP SCHEMA ledger CASCADE;");
}

[DbContext(typeof(LedgerDbContext))]
[Migration("20261010000000_UseMockCaptureBoundary")]
public sealed class UseMockCaptureBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "Receipts", "ReceiptBoundary" })
        {
            using var stream = typeof(UseMockCaptureBoundary).Assembly.GetManifestResourceStream($"GrainLedger.Modules.Ledger.Sql.{name}.sql")!;
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("The synthetic receipt boundary cannot revert to another module's schema.");
}

public static class LedgerDatabase
{
    public static async Task ConfigureRuntimeRoleAsync(string connectionString, string runtimeRole, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeRole)) throw new InvalidOperationException("Ledger:RuntimeRole is required.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var check = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_roles r WHERE rolname=$1
                  AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolbypassrls
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_roles elevated
                      WHERE (elevated.rolsuper OR elevated.rolcreaterole OR elevated.rolbypassrls)
                        AND pg_has_role(r.oid,elevated.oid,'MEMBER')
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                      WHERE n.nspname IN ('ledger','identity','capture_mock')
                        AND pg_has_role(r.oid,c.relowner,'MEMBER')
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_namespace n WHERE n.nspname IN ('ledger','identity','capture_mock')
                        AND pg_has_role(r.oid,n.nspowner,'MEMBER')
                  )
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_database d WHERE d.datname=current_database()
                        AND pg_has_role(r.oid,d.datdba,'MEMBER')
                  )
            )
            """, connection);
        check.Parameters.AddWithValue(runtimeRole);
        if (!Equals(await check.ExecuteScalarAsync(ct), true)) throw new InvalidOperationException("Runtime role missing or has elevated/owner authority.");
        var role = new NpgsqlCommandBuilder().QuoteIdentifier(runtimeRole);
        await using var mockSchema = new NpgsqlCommand("SELECT to_regnamespace('capture_mock') IS NOT NULL", connection);
        var mockGrants = Equals(await mockSchema.ExecuteScalarAsync(ct), true) ? $"""
            GRANT USAGE ON SCHEMA capture_mock TO {role};
            GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA capture_mock TO {role};
            GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA capture_mock TO {role};
            """ : "";
        await using var grant = new NpgsqlCommand($"""
            REVOKE ALL ON SCHEMA ledger FROM {role};
            GRANT USAGE ON SCHEMA ledger,identity TO {role};
            REVOKE ALL ON ALL TABLES IN SCHEMA ledger FROM {role};
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA ledger FROM {role};
            GRANT SELECT ON ALL TABLES IN SCHEMA ledger TO {role};
            GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA identity TO {role};
            GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA identity TO {role};
            {mockGrants}
            GRANT EXECUTE ON FUNCTION
                ledger.command(uuid,text,jsonb),ledger.read(uuid,text,jsonb),ledger.receipt(uuid,jsonb),
                ledger.receipt_correction_request(uuid,uuid,uuid,uuid),
                ledger.reverse_receipt(uuid,uuid,uuid,uuid,uuid,text),
                ledger.receiving_block(uuid,uuid,uuid,boolean,uuid,text),
                ledger.can_read_lot(uuid,uuid),ledger.assigned(uuid,uuid),
                ledger.own(uuid,uuid),ledger.role(uuid)
            TO {role};
            """, connection);
        await grant.ExecuteNonQueryAsync(ct);
        await using var inherited = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_class c WHERE c.relnamespace='ledger'::regnamespace AND c.relkind IN ('r','p')
                  AND has_table_privilege($1,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
            ) OR has_schema_privilege($1,'ledger','CREATE')
              OR has_function_privilege($1,'ledger.post(uuid,uuid,uuid,numeric,uuid)','EXECUTE')
            """, connection);
        inherited.Parameters.AddWithValue(runtimeRole);
        if (Equals(await inherited.ExecuteScalarAsync(ct), true)) throw new InvalidOperationException("Runtime role inherits unsafe ledger mutation authority; remove inherited grants before deployment.");
    }

    public static async Task ValidateRuntimeAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_roles r WHERE rolname=current_user AND (
                    rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls
                    OR EXISTS (
                        SELECT 1 FROM pg_roles elevated
                        WHERE (elevated.rolsuper OR elevated.rolcreaterole OR elevated.rolbypassrls)
                          AND pg_has_role(r.oid,elevated.oid,'MEMBER')
                    )
                    OR EXISTS (
                        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                        WHERE n.nspname IN ('ledger','identity','capture_mock')
                          AND pg_has_role(r.oid,c.relowner,'MEMBER')
                    )
                    OR EXISTS (
                        SELECT 1 FROM pg_namespace n WHERE n.nspname IN ('ledger','identity','capture_mock')
                          AND pg_has_role(r.oid,n.nspowner,'MEMBER')
                    )
                    OR EXISTS (
                        SELECT 1 FROM pg_database d WHERE d.datname=current_database()
                          AND pg_has_role(r.oid,d.datdba,'MEMBER')
                    )
                    OR EXISTS (
                        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                        WHERE n.nspname='ledger' AND c.relkind IN ('r','p')
                          AND has_table_privilege(r.oid,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    )
                    OR has_schema_privilege(r.oid,'ledger','CREATE')
                    OR has_function_privilege(r.oid,'ledger.post(uuid,uuid,uuid,numeric,uuid)','EXECUTE')
                )
            )
            """, connection);
        if (Equals(await command.ExecuteScalarAsync(ct), true)) throw new InvalidOperationException("API database role must not be superuser, schema/table owner or owner-role member. Configure separate migration and runtime credentials.");
    }
}

public static class LedgerModuleRegistration
{
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LedgerDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("GrainLedger"),
            n => n.MigrationsAssembly(typeof(LedgerDbContext).Assembly.FullName).MigrationsHistoryTable("__EFMigrationsHistory", "ledger")));
        services.AddScoped<LedgerService>();
        return services;
    }
}
