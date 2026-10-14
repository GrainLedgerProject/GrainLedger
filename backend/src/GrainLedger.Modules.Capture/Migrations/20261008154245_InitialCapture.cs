using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class InitialCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "capture");

            migrationBuilder.CreateTable(
                name: "business_events",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "farmers",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    contact = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_farmers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "offline_operations",
                schema: "capture",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    client_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    capture_geojson = table.Column<string>(type: "jsonb", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    applied_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_payload = table.Column<string>(type: "jsonb", nullable: true),
                    conflict_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offline_operations", x => x.id);
                });

            migrationBuilder.AddForeignKey(name: "fk_farmers_company", table: "farmers", schema: "capture",
                column: "company_id", principalTable: "organizations", principalSchema: "identity",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "fk_operations_company", table: "offline_operations", schema: "capture",
                column: "company_id", principalTable: "organizations", principalSchema: "identity",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "fk_operations_user", table: "offline_operations", schema: "capture",
                column: "submitted_by", principalTable: "users", principalSchema: "identity",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "fk_operations_event", table: "offline_operations", schema: "capture",
                column: "applied_event_id", principalTable: "business_events", principalSchema: "capture",
                principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "fk_events_company", table: "business_events", schema: "capture",
                column: "company_id", principalTable: "organizations", principalSchema: "identity",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "fk_events_actor", table: "business_events", schema: "capture",
                column: "actor_id", principalTable: "users", principalSchema: "identity",
                principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("ALTER TABLE capture.offline_operations ADD CONSTRAINT ck_offline_operations_status CHECK (status IN ('PENDING', 'APPLIED', 'CONFLICT', 'REJECTED'))");

            migrationBuilder.CreateIndex(
                name: "IX_business_events_actor_id",
                schema: "capture",
                table: "business_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_business_events_company_id",
                schema: "capture",
                table: "business_events",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_farmers_company_id_created_at_id",
                schema: "capture",
                table: "farmers",
                columns: new[] { "company_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_farmers_company_code",
                schema: "capture",
                table: "farmers",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_farmers_company_id",
                schema: "capture",
                table: "farmers",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_offline_operations_applied_event_id",
                schema: "capture",
                table: "offline_operations",
                column: "applied_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_offline_operations_company_id_id",
                schema: "capture",
                table: "offline_operations",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_offline_operations_submitted_by",
                schema: "capture",
                table: "offline_operations",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "ux_offline_operations_company_device_operation",
                schema: "capture",
                table: "offline_operations",
                columns: new[] { "company_id", "device_id", "client_operation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offline_operations",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "business_events",
                schema: "capture");

            migrationBuilder.DropTable(
                name: "farmers",
                schema: "capture");

        }
    }
}
