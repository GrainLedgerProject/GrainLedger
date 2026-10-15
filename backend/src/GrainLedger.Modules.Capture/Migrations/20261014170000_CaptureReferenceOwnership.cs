using System;
using GrainLedger.Modules.Capture;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261014170000_CaptureReferenceOwnership")]
public sealed class CaptureReferenceOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "farmers", "plots", "crop_seasons", "plot_seasons" })
        {
            migrationBuilder.AddColumn<Guid>(name: "created_by", schema: "capture", table: table,
                type: "uuid", nullable: true);
            migrationBuilder.AddColumn<int>(name: "version", schema: "capture", table: table,
                type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AddColumn<bool>(name: "is_deleted", schema: "capture", table: table,
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddForeignKey(name: $"fk_{table}_created_by", schema: "capture", table: table,
                column: "created_by", principalSchema: "identity", principalTable: "users",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "farmers", "plots", "crop_seasons", "plot_seasons" })
        {
            migrationBuilder.DropForeignKey(name: $"fk_{table}_created_by", schema: "capture", table: table);
            migrationBuilder.DropColumn(name: "created_by", schema: "capture", table: table);
            migrationBuilder.DropColumn(name: "version", schema: "capture", table: table);
            migrationBuilder.DropColumn(name: "is_deleted", schema: "capture", table: table);
        }
    }
}
