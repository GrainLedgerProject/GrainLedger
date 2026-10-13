using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Catalogs.Migrations
{
    /// <inheritdoc />
    public partial class AddMrlLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mrl_limits",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    mrl_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    substance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_value = table.Column<decimal>(type: "numeric(24,12)", precision: 24, scale: 12, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrl_limits", x => x.id);
                    table.CheckConstraint("ck_mrl_limits_nonnegative", "limit_value >= 0");
                    table.ForeignKey(
                        name: "FK_mrl_limits_active_substances_substance_id",
                        column: x => x.substance_id,
                        principalSchema: "catalog",
                        principalTable: "active_substances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mrl_limits_mrl_versions_mrl_version_id",
                        column: x => x.mrl_version_id,
                        principalSchema: "catalog",
                        principalTable: "mrl_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrl_limits_mrl_version_id_substance_id",
                schema: "catalog",
                table: "mrl_limits",
                columns: new[] { "mrl_version_id", "substance_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrl_limits_substance_id",
                schema: "catalog",
                table: "mrl_limits",
                column: "substance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrl_limits",
                schema: "catalog");
        }
    }
}
