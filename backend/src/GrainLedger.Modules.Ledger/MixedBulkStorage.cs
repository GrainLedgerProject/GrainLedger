using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261020000000_MixedBulkStorage")]
public sealed class MixedBulkStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[] { "MixedBulkStorage", "MixedBulkCommands", "MixedBulkSafety", "MixedBulkReads" })
        {
            using var stream = typeof(MixedBulkStorage).Assembly.GetManifestResourceStream(
                $"GrainLedger.Modules.Ledger.Sql.{name}.sql")!;
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Mixed-bulk custody history is immutable and cannot be rolled back safely.");
}
