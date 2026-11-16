using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261104000000_WholeFlowResidueProfiles")]
public partial class WholeFlowResidueProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string name = "GrainLedger.Data.Sql.WholeFlowResidueProfiles.sql";
        using var stream = typeof(WholeFlowResidueProfiles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing migration resource: {name}");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Whole-flow residue profiles cannot be rolled back to the previous contract.");
}
