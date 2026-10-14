using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureCropAndPlotSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crop_seasons",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crop_seasons", x => x.id);
                    table.CheckConstraint("ck_crop_seasons_dates", "ends_on >= starts_on");
                });

            migrationBuilder.CreateTable(
                name: "plot_seasons",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rice_variety = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plot_seasons", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_crop_seasons_company_id",
                schema: "capture",
                table: "crop_seasons",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plot_seasons_season_id",
                schema: "capture",
                table: "plot_seasons",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ux_plot_seasons_company_id",
                schema: "capture",
                table: "plot_seasons",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plot_seasons_plot_season",
                schema: "capture",
                table: "plot_seasons",
                columns: new[] { "plot_id", "season_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_crop_seasons_company", schema: "capture", table: "crop_seasons", column: "company_id",
                principalSchema: "identity", principalTable: "organizations", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_plot_seasons_company", schema: "capture", table: "plot_seasons", column: "company_id",
                principalSchema: "identity", principalTable: "organizations", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_plot_seasons_company_plot", schema: "capture", table: "plot_seasons",
                columns: new[] { "company_id", "plot_id" }, principalSchema: "capture", principalTable: "plots",
                principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_plot_seasons_company_season", schema: "capture", table: "plot_seasons",
                columns: new[] { "company_id", "season_id" }, principalSchema: "capture", principalTable: "crop_seasons",
                principalColumns: new[] { "company_id", "id" }, onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plot_seasons",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "crop_seasons",
                schema: "capture");
        }
    }
}
