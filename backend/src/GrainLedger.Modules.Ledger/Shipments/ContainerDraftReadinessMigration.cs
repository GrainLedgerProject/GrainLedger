using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261025000000_ContainerDraftReadiness")]
public sealed class ContainerDraftReadiness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerDraftReadiness).Assembly.GetManifestResourceStream("GrainLedger.Modules.Ledger.Sql.ContainerDraftReadiness.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Container draft observations and history must be retained.");
}
