using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261022000000_PlanningCompatibility")]
public sealed class PlanningCompatibilityMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(PlanningCompatibilityMigration).Assembly.GetManifestResourceStream(
            "GrainLedger.Modules.Ledger.Sql.PlanningCompatibility.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Planning compatibility reads depend on reviewed Ledger policy history.");
}
