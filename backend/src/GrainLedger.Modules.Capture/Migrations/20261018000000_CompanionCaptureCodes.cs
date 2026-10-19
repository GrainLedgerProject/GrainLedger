using GrainLedger.Modules.Capture;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261018000000_CompanionCaptureCodes")]
public sealed class CompanionCaptureCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "code_number", schema: "capture", table: "farmers",
            type: "bigint", nullable: true);
        migrationBuilder.Sql("""
            WITH numbered AS (
                SELECT id, row_number() OVER (PARTITION BY company_id ORDER BY created_at, id) AS number
                FROM capture.farmers
            )
            UPDATE capture.farmers f SET code_number = n.number FROM numbered n WHERE f.id = n.id;
            """);
        migrationBuilder.CreateIndex(name: "ux_farmers_company_code_number", schema: "capture",
            table: "farmers", columns: ["company_id", "code_number"], unique: true);
        migrationBuilder.CreateTable(name: "code_counters", schema: "capture", columns: table => new
        {
            company_id = table.Column<Guid>(type: "uuid", nullable: false),
            kind = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
            scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            next_value = table.Column<long>(type: "bigint", nullable: false)
        }, constraints: table => table.PrimaryKey("pk_code_counters", x => new { x.company_id, x.kind, x.scope }));
        migrationBuilder.Sql("""
            INSERT INTO capture.code_counters(company_id,kind,scope,next_value)
            SELECT company_id,'FRM','',max(code_number)+1 FROM capture.farmers GROUP BY company_id;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "code_counters", schema: "capture");
        migrationBuilder.DropIndex(name: "ux_farmers_company_code_number", schema: "capture", table: "farmers");
        migrationBuilder.DropColumn(name: "code_number", schema: "capture", table: "farmers");
    }
}
