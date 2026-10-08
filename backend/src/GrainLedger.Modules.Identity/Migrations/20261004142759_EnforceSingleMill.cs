using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleMill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_organizations_Kind",
                schema: "identity",
                table: "organizations",
                column: "Kind",
                unique: true,
                filter: "\"Kind\" = 'Mill'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organizations_Kind",
                schema: "identity",
                table: "organizations");
        }
    }
}
