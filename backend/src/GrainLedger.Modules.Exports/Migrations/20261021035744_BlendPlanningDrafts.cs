using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Exports.Migrations
{
    /// <inheritdoc />
    public partial class BlendPlanningDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blend_plan_drafts",
                schema: "exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_id = table.Column<Guid>(type: "uuid", nullable: false),
                    specification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    target_mass_kg = table.Column<decimal>(type: "numeric(20,6)", precision: 20, scale: 6, nullable: false),
                    demand_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blend_plan_drafts", x => x.id);
                    table.CheckConstraint("ck_plan_draft_mass", "target_mass_kg > 0");
                    table.CheckConstraint("ck_plan_draft_state", "(status = 'DRAFT' AND cancelled_by IS NULL AND cancelled_at IS NULL) OR (status = 'CANCELLED' AND cancelled_by IS NOT NULL AND cancelled_at IS NOT NULL)");
                    table.CheckConstraint("ck_plan_draft_versions", "version_no > 0 AND version > 0");
                });

            migrationBuilder.CreateTable(
                name: "blend_plan_events",
                schema: "exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blend_plan_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blend_plan_drafts_company_id_id",
                schema: "exports",
                table: "blend_plan_drafts",
                columns: new[] { "company_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_blend_plan_drafts_company_id_order_id",
                schema: "exports",
                table: "blend_plan_drafts",
                columns: new[] { "company_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_blend_plan_drafts_container_id_version_no",
                schema: "exports",
                table: "blend_plan_drafts",
                columns: new[] { "container_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_blend_plan_events_plan_id_recorded_at_id",
                schema: "exports",
                table: "blend_plan_events",
                columns: new[] { "plan_id", "recorded_at", "id" });
            migrationBuilder.Sql("""
                ALTER TABLE exports.containers ADD CONSTRAINT uq_container_order UNIQUE(order_id, id);
                ALTER TABLE exports.blend_plan_drafts
                    ADD FOREIGN KEY(company_id, order_id) REFERENCES exports.export_orders(company_id, id),
                    ADD FOREIGN KEY(company_id, container_id) REFERENCES exports.containers(company_id, id),
                    ADD FOREIGN KEY(order_id, container_id) REFERENCES exports.containers(order_id, id),
                    ADD FOREIGN KEY(order_id, specification_id) REFERENCES exports.order_specifications(order_id, id),
                    ADD FOREIGN KEY(created_by) REFERENCES identity.users("Id"),
                    ADD FOREIGN KEY(cancelled_by) REFERENCES identity.users("Id");
                ALTER TABLE exports.blend_plan_events
                    ADD FOREIGN KEY(company_id, plan_id) REFERENCES exports.blend_plan_drafts(company_id, id),
                    ADD FOREIGN KEY(actor_id) REFERENCES identity.users("Id");
                CREATE TRIGGER immutable_plan_events BEFORE UPDATE OR DELETE ON exports.blend_plan_events
                    FOR EACH ROW EXECUTE FUNCTION exports.reject_historical_change();

                CREATE FUNCTION exports.protect_plan_draft() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Plan history cannot be deleted' USING ERRCODE='23514';
                    END IF;
                    PERFORM pg_advisory_xact_lock(hashtextextended('export:' || replace(NEW.company_id::text, '-', ''), 0));
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD.status <> 'DRAFT' OR NEW.status <> 'CANCELLED' OR NEW.version <> OLD.version + 1
                            OR (to_jsonb(NEW) - ARRAY['status','version','cancelled_by','cancelled_at'])
                               IS DISTINCT FROM (to_jsonb(OLD) - ARRAY['status','version','cancelled_by','cancelled_at']) THEN
                            RAISE EXCEPTION 'Plan demand is immutable; only cancellation is supported' USING ERRCODE='23514';
                        END IF;
                    ELSE
                        IF NEW.status <> 'DRAFT' OR NEW.version <> 1 OR NOT EXISTS (
                            SELECT 1 FROM exports.containers c
                            JOIN exports.export_orders o ON o.id=c.order_id
                            JOIN exports.order_specifications s ON s.order_id=o.id AND s.version_no=o.current_specification_version
                            WHERE c.id=NEW.container_id AND c.company_id=NEW.company_id AND o.id=NEW.order_id
                              AND s.id=NEW.specification_id AND c.status='PLANNED' AND c.target_mass_kg=NEW.target_mass_kg
                        ) THEN
                            RAISE EXCEPTION 'Plan must reference current container demand' USING ERRCODE='23514';
                        END IF;
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER protect_plan_draft BEFORE INSERT OR UPDATE OR DELETE ON exports.blend_plan_drafts
                    FOR EACH ROW EXECUTE FUNCTION exports.protect_plan_draft();

                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Plan history cannot be rolled back safely.");
    }
}
