using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrainLedger.Modules.Capture.Migrations
{
    /// <inheritdoc />
    public partial class ChangeSprayApplicationToDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing exact timestamps remain in the immutable SPRAY_DECLARATION_RECORDED event.
            // Interpret a historical timestamp in the field team's local calendar before converting.
            migrationBuilder.Sql("""
                ALTER TABLE capture.spray_declarations RENAME COLUMN applied_at TO applied_on;
                ALTER TABLE capture.spray_declarations
                    ALTER COLUMN applied_on TYPE date
                    USING (applied_on AT TIME ZONE 'Asia/Ho_Chi_Minh')::date;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("A date cannot be converted back to the original application time.");
        }
    }
}
