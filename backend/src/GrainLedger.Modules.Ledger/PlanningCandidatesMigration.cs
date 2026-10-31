using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Ledger;

[DbContext(typeof(LedgerDbContext))]
[Migration("20261021000000_PlanningCandidates")]
public sealed class PlanningCandidatesMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(PlanningCandidatesMigration).Assembly.GetManifestResourceStream(
            "GrainLedger.Modules.Ledger.Sql.PlanningCandidates.sql")!;
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Candidate lineage reads depend on immutable Ledger custody history.");
}
