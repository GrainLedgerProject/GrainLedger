using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261014200000_ManageLedgerRecords")]
public sealed class ManageLedgerRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var resource in new[] { "ManageRecords", "ManageActiveResources" })
        {
            using var stream = typeof(ManageLedgerRecords).Assembly.GetManifestResourceStream($"GrainLedger.Modules.Ledger.Sql.{resource}.sql")!;
            using var reader = new StreamReader(stream);
            migrationBuilder.Sql(reader.ReadToEnd());
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Ledger record management preserves unknown legacy creators and cannot be reversed.");
}
