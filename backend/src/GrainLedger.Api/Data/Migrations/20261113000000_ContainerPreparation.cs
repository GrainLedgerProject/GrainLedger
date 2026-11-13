using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113000000_ContainerPreparation")]
public sealed class ContainerPreparation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string name = "GrainLedger.Data.Sql.ContainerPreparation.sql";
        using var stream = typeof(ContainerPreparation).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing migration resource: {name}");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Container preparation history must be retained.");
}
