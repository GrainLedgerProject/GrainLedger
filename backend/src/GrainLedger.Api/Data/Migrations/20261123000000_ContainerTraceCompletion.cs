using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123000000_ContainerTraceCompletion")]
public sealed class ContainerTraceCompletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerTraceCompletion).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ContainerTraceCompletion.sql")
            ?? throw new InvalidOperationException("Missing container tracing migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Use a reviewed forward migration to preserve trace contracts.");
}
