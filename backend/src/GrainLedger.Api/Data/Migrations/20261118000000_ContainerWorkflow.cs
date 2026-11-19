using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118000000_ContainerWorkflow")]
public sealed class ContainerWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(ContainerWorkflow).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.ContainerWorkflow.sql")
            ?? throw new InvalidOperationException("Missing container workflow migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Container workflow history must be retained.");
}
