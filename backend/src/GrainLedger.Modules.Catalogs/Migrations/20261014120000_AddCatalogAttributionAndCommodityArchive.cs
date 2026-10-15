using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Catalogs.Migrations
{
    public partial class AddCatalogAttributionAndCommodityArchive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "created_by", schema: "catalog", table: "commodities", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "is_archived", schema: "catalog", table: "commodities", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<Guid>(name: "created_by", schema: "catalog", table: "measurement_units", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "is_deleted", schema: "catalog", table: "measurement_units", type: "boolean", nullable: false, defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "created_by", schema: "catalog", table: "commodities");
            migrationBuilder.DropColumn(name: "is_archived", schema: "catalog", table: "commodities");
            migrationBuilder.DropColumn(name: "created_by", schema: "catalog", table: "measurement_units");
            migrationBuilder.DropColumn(name: "is_deleted", schema: "catalog", table: "measurement_units");
        }
    }
}