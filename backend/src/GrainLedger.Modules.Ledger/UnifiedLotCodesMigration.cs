using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261017000000_UnifiedLotCodes")]
public sealed class UnifiedLotCodesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "UnifiedLotCodes", "UnifiedLotCodeBoundary", "UnifiedLotCodeProcessing" })
        {
            using var stream = typeof(UnifiedLotCodesMigration).Assembly.GetManifestResourceStream(
                $"GrainLedger.Modules.Ledger.Sql.{name}.sql")!;
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Issued lot codes and reservations cannot be rolled back safely.");
}
