using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Catalogs.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "active_substances",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_active_substances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "catalog_events",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_state = table.Column<string>(type: "jsonb", nullable: true),
                    after_state = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_catalog_events", x => x.id);
                    table.CheckConstraint("ck_catalog_events_has_state", "before_state IS NOT NULL OR after_state IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_active_substances_code",
                schema: "catalog",
                table: "active_substances",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalog_events_actor_id",
                schema: "catalog",
                table: "catalog_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_events_correlation_id",
                schema: "catalog",
                table: "catalog_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_catalog_events_resource_type_resource_id_occurred_at",
                schema: "catalog",
                table: "catalog_events",
                columns: new[] { "resource_type", "resource_id", "occurred_at" });

            // Identity owns its users table; this migration only adds the cross-schema FK.
            migrationBuilder.AddForeignKey(
                name: "fk_catalog_events_actor",
                schema: "catalog",
                table: "catalog_events",
                column: "actor_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION catalog.reject_catalog_event_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'catalog_events is append-only';
                END;
                $$;
                CREATE TRIGGER trg_catalog_events_append_only
                BEFORE UPDATE OR DELETE ON catalog.catalog_events
                FOR EACH ROW EXECUTE FUNCTION catalog.reject_catalog_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_catalog_events_append_only ON catalog.catalog_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS catalog.reject_catalog_event_mutation();");
            migrationBuilder.DropTable(
                name: "active_substances",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "catalog_events",
                schema: "catalog");
        }
    }
}
