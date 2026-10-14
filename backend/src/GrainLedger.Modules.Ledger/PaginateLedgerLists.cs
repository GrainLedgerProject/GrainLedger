using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261014210000_PaginateLedgerLists")]
public sealed class PaginateLedgerLists : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "PagedReceiving", "PagedLists" })
        {
            using var stream = typeof(PaginateLedgerLists).Assembly.GetManifestResourceStream($"GrainLedger.Modules.Ledger.Sql.{name}.sql")!;
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Ledger list pagination cannot restore obsolete unbounded read contracts.");
}
