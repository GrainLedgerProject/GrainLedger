using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112000000_BlendApprovalReservation")]
public sealed class BlendApprovalReservation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        using var stream = typeof(BlendApprovalReservation).Assembly
            .GetManifestResourceStream("GrainLedger.Data.Sql.BlendApprovalReservation.sql")
            ?? throw new InvalidOperationException("Missing blend reservation migration SQL.");
        using var reader = new StreamReader(stream);
        migrationBuilder.Sql(reader.ReadToEnd());
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Blend reservations require a forward migration.");
}
