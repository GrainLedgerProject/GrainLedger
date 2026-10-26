using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261026000000_PlanningQualityObservations")]
public sealed class PlanningQualityObservationsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(PlanningQualityObservationsMigration).Assembly.GetManifestResourceStream(
            "GrainLedger.Modules.Ledger.Sql.PlanningQualityObservations.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Confirmed quality observation history cannot be rolled back safely.");
}
