using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113000100_ContainerExecution")]
public sealed class ContainerExecution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const string name = "GrainLedger.Data.Sql.ContainerExecution.sql";
        using var stream = typeof(ContainerExecution).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing migration resource: {name}");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Actual loading, source allocations and closure history must be retained.");
}
