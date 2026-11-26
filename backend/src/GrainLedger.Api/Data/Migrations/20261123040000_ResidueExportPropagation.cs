using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace GrainLedger.Api.Data.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261123040000_ResidueExportPropagation")]
public sealed class ResidueExportPropagation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ResidueExportPropagation).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ResidueExportPropagation.sql")
            ?? throw new InvalidOperationException("Missing residue risk read migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Preserve residue history with a forward migration.");
}
