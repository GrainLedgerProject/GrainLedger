using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261025000000_PlanningYieldRules")]
public sealed class PlanningYieldRulesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(PlanningYieldRulesMigration).Assembly.GetManifestResourceStream(
            "GrainLedger.Modules.Ledger.Sql.PlanningYieldRules.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Published planning yield history cannot be rolled back safely.");
}
