using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureSprayDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spray_declarations",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plot_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence_ref = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spray_declarations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "spray_items",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "text", nullable: true),
                    substance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rate = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false),
                    rate_unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spray_items", x => x.id);
                    table.CheckConstraint("ck_spray_items_product_or_substance", "product_name IS NOT NULL OR substance_id IS NOT NULL");
                    table.CheckConstraint("ck_spray_items_rate_positive", "rate > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_spray_declarations_plot_season_id",
                schema: "capture",
                table: "spray_declarations",
                column: "plot_season_id");

            migrationBuilder.CreateIndex(
                name: "IX_spray_declarations_recorded_by",
                schema: "capture",
                table: "spray_declarations",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ux_spray_declarations_company_id",
                schema: "capture",
                table: "spray_declarations",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_spray_items_declaration_id",
                schema: "capture",
                table: "spray_items",
                column: "declaration_id");

            migrationBuilder.CreateIndex(
                name: "IX_spray_items_substance_id",
                schema: "capture",
                table: "spray_items",
                column: "substance_id");

            migrationBuilder.CreateIndex(
                name: "ux_spray_items_company_id",
                schema: "capture",
                table: "spray_items",
                columns: new[] { "company_id", "id" },
                unique: true);

            // Identity and Catalogs own the referenced tables. Capture adds only its foreign keys.
            migrationBuilder.AddForeignKey(
                name: "fk_spray_declarations_company", schema: "capture", table: "spray_declarations",
                column: "company_id", principalSchema: "identity", principalTable: "organizations",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_spray_declarations_company_plot_season", schema: "capture", table: "spray_declarations",
                columns: new[] { "company_id", "plot_season_id" }, principalSchema: "capture",
                principalTable: "plot_seasons", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_spray_declarations_recorded_by", schema: "capture", table: "spray_declarations",
                column: "recorded_by", principalSchema: "identity", principalTable: "users",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_spray_items_company", schema: "capture", table: "spray_items",
                column: "company_id", principalSchema: "identity", principalTable: "organizations",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_spray_items_company_declaration", schema: "capture", table: "spray_items",
                columns: new[] { "company_id", "declaration_id" }, principalSchema: "capture",
                principalTable: "spray_declarations", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_spray_items_substance", schema: "capture", table: "spray_items",
                column: "substance_id", principalSchema: "catalog", principalTable: "active_substances",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spray_items",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "spray_declarations",
                schema: "capture");
        }
    }
}
