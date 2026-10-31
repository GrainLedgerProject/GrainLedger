using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261024000000_PlanningCompatibilityOutput")]
public sealed class PlanningCompatibilityOutputMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(PlanningCompatibilityOutputMigration).Assembly.GetManifestResourceStream(
            "GrainLedger.Modules.Ledger.Sql.PlanningCompatibilityOutput.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Planning output commodity is required for F14 assessment.");
}
