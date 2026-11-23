using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123010000_ContainerTraceCorrections")]
public sealed class ContainerTraceCorrections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerTraceCorrections).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ContainerTraceCorrections.sql")
            ?? throw new InvalidOperationException("Missing container tracing migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Use a reviewed forward migration to preserve trace contracts.");
}
