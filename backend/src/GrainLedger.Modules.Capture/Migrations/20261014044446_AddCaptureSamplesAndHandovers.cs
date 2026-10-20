using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureSamplesAndHandovers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sample_handovers",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sample_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    from_party = table.Column<string>(type: "text", nullable: false),
                    to_party = table.Column<string>(type: "text", nullable: false),
                    handed_over_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seal_condition = table.Column<string>(type: "text", nullable: false),
                    evidence_ref = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sample_handovers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "samples",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_lot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seal_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    collected_by = table.Column<Guid>(type: "uuid", nullable: false),
                    collected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_samples", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sample_handovers_recorded_by",
                schema: "capture",
                table: "sample_handovers",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "IX_sample_handovers_sample_id",
                schema: "capture",
                table: "sample_handovers",
                column: "sample_id");

            migrationBuilder.CreateIndex(
                name: "ux_sample_handovers_company_id",
                schema: "capture",
                table: "sample_handovers",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_samples_collected_by",
                schema: "capture",
                table: "samples",
                column: "collected_by");

            migrationBuilder.CreateIndex(
                name: "IX_samples_source_lot_id",
                schema: "capture",
                table: "samples",
                column: "source_lot_id");

            migrationBuilder.CreateIndex(
                name: "ux_samples_company_code",
                schema: "capture",
                table: "samples",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_samples_company_id",
                schema: "capture",
                table: "samples",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_samples_company", schema: "capture", table: "samples",
                column: "company_id", principalSchema: "identity", principalTable: "organizations",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_samples_source_lot", schema: "capture", table: "samples",
                columns: new[] { "company_id", "source_lot_id" }, principalSchema: "capture",
                principalTable: "source_lots", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_samples_collected_by", schema: "capture", table: "samples",
                column: "collected_by", principalSchema: "identity", principalTable: "users",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_sample_handovers_company", schema: "capture", table: "sample_handovers",
                column: "company_id", principalSchema: "identity", principalTable: "organizations",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_sample_handovers_sample", schema: "capture", table: "sample_handovers",
                columns: new[] { "company_id", "sample_id" }, principalSchema: "capture",
                principalTable: "samples", principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_sample_handovers_recorded_by", schema: "capture", table: "sample_handovers",
                column: "recorded_by", principalSchema: "identity", principalTable: "users",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sample_handovers",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "samples",
                schema: "capture");
        }
    }
}
