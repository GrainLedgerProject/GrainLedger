using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261023000000_RetireEquipment")]
public sealed class RetireEquipment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(RetireEquipment).Assembly.GetManifestResourceStream("GrainLedger.Modules.Ledger.Sql.EquipmentLifecycle.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Equipment retirement preserves immutable operation history and cannot be reversed.");
}
