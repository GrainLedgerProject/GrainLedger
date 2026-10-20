using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261021000000_ContainerDrafts")]
public sealed class ContainerDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerDrafts).Assembly.GetManifestResourceStream("GrainLedger.Modules.Ledger.Sql.Containers.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Container preparation history must be retained.");
}
