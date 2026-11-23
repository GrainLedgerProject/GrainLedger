using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123000000_ResidueChangeFoundation")]
public sealed class ResidueChangeFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ResidueChangeFoundation).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ResidueChanges.sql")
            ?? throw new InvalidOperationException("Missing residue change migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Use a reviewed forward migration to preserve residue change history.");
}
