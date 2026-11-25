using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125000000_RiceVarietySegregation")]
public sealed class RiceVarietySegregation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(RiceVarietySegregation).Assembly.GetManifestResourceStream("GrainLedger.Data.Sql.RiceVarietySegregation.sql")
            ?? throw new InvalidOperationException("Missing rice variety segregation migration resource");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Use a reviewed forward migration to preserve trace contracts.");
}
