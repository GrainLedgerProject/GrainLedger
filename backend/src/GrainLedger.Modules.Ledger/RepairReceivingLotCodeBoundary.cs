using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261019010000_RepairReceivingLotCodeBoundary")]
public sealed class RepairReceivingLotCodeBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(RepairReceivingLotCodeBoundary).Assembly.GetManifestResourceStream("GrainLedger.Modules.Ledger.Sql.RepairReceivingLotCodeBoundary.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Receiving lot-code boundary repair cannot be rolled back to hardcoded source codes.");
}
