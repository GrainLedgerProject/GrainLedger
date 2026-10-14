using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class AddCapturePlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plots",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    boundary_geojson = table.Column<string>(type: "jsonb", nullable: false),
                    area_ha = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plots", x => x.id);
                    table.CheckConstraint("ck_plots_area_ha_positive", "area_ha > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ux_plots_company_id",
                schema: "capture",
                table: "plots",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plots_farmer_code",
                schema: "capture",
                table: "plots",
                columns: new[] { "farmer_id", "code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_plots_company", schema: "capture", table: "plots", column: "company_id",
                principalSchema: "identity", principalTable: "organizations", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_plots_company_farmer", schema: "capture", table: "plots",
                columns: new[] { "company_id", "farmer_id" }, principalSchema: "capture",
                principalTable: "farmers", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plots",
                schema: "capture");
        }
    }
}
