using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureProcurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "procurement_items",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    plot_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                    net_mass_kg = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    measured_value = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false),
                    measured_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    moisture_percent = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_items", x => x.id);
                    table.CheckConstraint("ck_procurement_items_line_no", "line_no > 0");
                    table.CheckConstraint("ck_procurement_items_measured", "measured_value > 0");
                    table.CheckConstraint("ck_procurement_items_moisture", "moisture_percent >= 0 AND moisture_percent <= 100");
                    table.CheckConstraint("ck_procurement_items_net_mass", "net_mass_kg > 0");
                });

            migrationBuilder.CreateTable(
                name: "procurements",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    farmer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurements", x => x.id);
                    table.CheckConstraint("ck_procurements_status", "status IN ('DRAFT', 'COMMITTED')");
                });

            migrationBuilder.CreateTable(
                name: "source_lots",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    procurement_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_lots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_procurement_items_plot_season_id",
                schema: "capture",
                table: "procurement_items",
                column: "plot_season_id");

            migrationBuilder.CreateIndex(
                name: "ux_procurement_items_company_id",
                schema: "capture",
                table: "procurement_items",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_procurement_items_receipt_line",
                schema: "capture",
                table: "procurement_items",
                columns: new[] { "procurement_id", "line_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_procurements_company_id_received_at_id",
                schema: "capture",
                table: "procurements",
                columns: new[] { "company_id", "received_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_procurements_event_id",
                schema: "capture",
                table: "procurements",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_procurements_company_code",
                schema: "capture",
                table: "procurements",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_procurements_company_id",
                schema: "capture",
                table: "procurements",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_lots_lot_id",
                schema: "capture",
                table: "source_lots",
                column: "lot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_lots_procurement_item_id",
                schema: "capture",
                table: "source_lots",
                column: "procurement_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_source_lots_company_id",
                schema: "capture",
                table: "source_lots",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_procurements_company", schema: "capture", table: "procurements",
                column: "company_id", principalSchema: "identity", principalTable: "organizations",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_procurements_company_farmer", schema: "capture", table: "procurements",
                columns: new[] { "company_id", "farmer_id" }, principalSchema: "capture",
                principalTable: "farmers", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_procurements_received_by", schema: "capture", table: "procurements",
                column: "received_by", principalSchema: "identity", principalTable: "users",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_procurements_event", schema: "capture", table: "procurements",
                column: "event_id", principalSchema: "capture", principalTable: "business_events",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_procurement_items_company_procurement", schema: "capture", table: "procurement_items",
                columns: new[] { "company_id", "procurement_id" }, principalSchema: "capture",
                principalTable: "procurements", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_procurement_items_company_plot_season", schema: "capture", table: "procurement_items",
                columns: new[] { "company_id", "plot_season_id" }, principalSchema: "capture",
                principalTable: "plot_seasons", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_source_lots_company_item", schema: "capture", table: "source_lots",
                columns: new[] { "company_id", "procurement_item_id" }, principalSchema: "capture",
                principalTable: "procurement_items", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            // The Ledger team's lot schema is not in this branch. Its tenant-scoped FK is added at integration.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_lots",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "procurement_items",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "procurements",
                schema: "capture");
        }
    }
}
