using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261021010000_CustodyTraceability")]
public sealed class CustodyTraceability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(CustodyTraceability).Assembly.GetManifestResourceStream("GrainLedger.Modules.Ledger.Sql.CustodyTraceability.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Traceback audit history must be retained.");
}
