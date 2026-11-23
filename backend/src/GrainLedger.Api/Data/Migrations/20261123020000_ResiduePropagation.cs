using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace GrainLedger.Api.Data.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261123020000_ResiduePropagation")]
public sealed class ResiduePropagation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ResiduePropagation).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ResiduePropagation.sql")
            ?? throw new InvalidOperationException("Missing residue propagation migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Preserve residue history with a forward migration.");
}
