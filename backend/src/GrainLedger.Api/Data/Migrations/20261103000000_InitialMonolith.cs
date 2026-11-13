using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261103000000_InitialMonolith")]
public partial class InitialMonolith : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SET LOCAL check_function_bodies = false;");
        foreach (var resource in new[] { "InitialSchema", "InitialReferenceData", "InitialConstraints" })
        {
            var name = $"GrainLedger.Data.Sql.{resource}.sql";
            using var stream = typeof(InitialMonolith).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing migration resource: {name}");
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }

        migrationBuilder.Sql("""
            REVOKE ALL ON ALL TABLES IN SCHEMA grainledger FROM PUBLIC;
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA grainledger FROM PUBLIC;
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA grainledger FROM PUBLIC;
            REVOKE CREATE ON SCHEMA grainledger FROM PUBLIC;
            SET LOCAL check_function_bodies = true;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("The monolith baseline cannot be rolled back; use the explicit application database reset procedure.");
}
