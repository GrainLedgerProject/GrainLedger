using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123010000_ResidueChangePublication")]
public sealed class ResidueChangePublication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ResidueChangePublication).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ResidueChangePublication.sql")
            ?? throw new InvalidOperationException("Missing residue publication migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Use a reviewed forward migration to preserve residue change history.");
}
