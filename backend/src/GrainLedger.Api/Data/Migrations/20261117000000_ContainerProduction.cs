using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117000000_ContainerProduction")]
public sealed class ContainerProduction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerProduction).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ContainerProduction.sql")
            ?? throw new InvalidOperationException("Missing container production migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Production and review history must be retained.");
}
