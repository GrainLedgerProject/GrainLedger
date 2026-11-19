using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118010000_ContainerTracingFlowchart")]
public sealed class ContainerTracingFlowchart : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerTracingFlowchart).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ContainerTracingFlowchart.sql")
            ?? throw new InvalidOperationException("Missing container tracing migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Use a reviewed forward migration to preserve trace contracts.");
}
