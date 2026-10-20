using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class StopCollectingSprayRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_spray_items_rate_positive",
                schema: "capture",
                table: "spray_items");

            migrationBuilder.AlterColumn<string>(
                name: "rate_unit",
                schema: "capture",
                table: "spray_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<decimal>(
                name: "rate",
                schema: "capture",
                table: "spray_items",
                type: "numeric(24,12)",
                precision: 24,
                scale: 12,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(24,12)",
                oldPrecision: 24,
                oldScale: 12);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM capture.spray_items WHERE rate IS NULL OR rate_unit IS NULL) THEN
                        RAISE EXCEPTION 'Cannot restore required spray rate: rate-free declarations exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AlterColumn<string>(
                name: "rate_unit",
                schema: "capture",
                table: "spray_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "rate",
                schema: "capture",
                table: "spray_items",
                type: "numeric(24,12)",
                precision: 24,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(24,12)",
                oldPrecision: 24,
                oldScale: 12,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_spray_items_rate_positive",
                schema: "capture",
                table: "spray_items",
                sql: "rate > 0");
        }
    }
}
