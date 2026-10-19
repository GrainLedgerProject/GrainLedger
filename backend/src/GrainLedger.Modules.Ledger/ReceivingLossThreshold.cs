using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261019000000_ReceivingLossThreshold")]
public sealed class ReceivingLossThreshold : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ReceivingLossThreshold).Assembly.GetManifestResourceStream("GrainLedger.Modules.Ledger.Sql.ReceivingLossThreshold.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Receiving threshold snapshots and configuration history cannot be rolled back.");
}
