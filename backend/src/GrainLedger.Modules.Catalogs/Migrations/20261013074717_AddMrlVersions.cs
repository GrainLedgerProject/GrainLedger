using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Catalogs.Migrations
{
    /// <inheritdoc />
    public partial class AddMrlVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mrl_versions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    market_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commodity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrl_versions", x => x.id);
                    table.CheckConstraint("ck_mrl_versions_effective_interval", "effective_to IS NULL OR effective_to > effective_from");
                    table.CheckConstraint("ck_mrl_versions_publication", "(status = 'DRAFT' AND published_by IS NULL AND published_at IS NULL) OR (status = 'PUBLISHED' AND published_by IS NOT NULL AND published_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_mrl_versions_commodities_commodity_id",
                        column: x => x.commodity_id,
                        principalSchema: "catalog",
                        principalTable: "commodities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mrl_versions_markets_market_id",
                        column: x => x.market_id,
                        principalSchema: "catalog",
                        principalTable: "markets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mrl_versions_commodity_id",
                schema: "catalog",
                table: "mrl_versions",
                column: "commodity_id");

            migrationBuilder.CreateIndex(
                name: "IX_mrl_versions_market_id_commodity_id_version_no",
                schema: "catalog",
                table: "mrl_versions",
                columns: new[] { "market_id", "commodity_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrl_versions_published_by",
                schema: "catalog",
                table: "mrl_versions",
                column: "published_by");

            // Identity owns users; Catalog only adds the cross-schema reference.
            migrationBuilder.AddForeignKey(
                name: "fk_mrl_versions_published_by",
                schema: "catalog",
                table: "mrl_versions",
                column: "published_by",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrl_versions",
                schema: "catalog");
        }
    }
}
