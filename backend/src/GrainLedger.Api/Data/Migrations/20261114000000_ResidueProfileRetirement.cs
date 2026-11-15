using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261114000000_ResidueProfileRetirement")]
public partial class ResidueProfileRetirement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string name = "GrainLedger.Data.Sql.ResidueProfileRetirement.sql";
        using var stream = typeof(ResidueProfileRetirement).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing migration resource: {name}");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Residue profile retirement history cannot be rolled back.");
}
