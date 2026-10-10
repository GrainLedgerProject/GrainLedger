using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SeparateRefreshTokenUseFromSessionRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefreshTokenUsedAt",
                schema: "identity",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefreshTokenUsedAt",
                schema: "identity",
                table: "sessions");
        }
    }
}
