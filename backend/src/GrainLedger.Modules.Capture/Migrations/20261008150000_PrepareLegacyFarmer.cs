using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Capture.Migrations;

/// <summary>
/// Keeps the empty, earlier Capture farmer table and its existing foreign keys
/// while making room for the Farmer API's table in InitialCapture.
/// </summary>
[DbContext(typeof(CaptureDbContext))]
[Migration("20261008150000_PrepareLegacyFarmer")]
public sealed class PrepareLegacyFarmer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $migration$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'capture'
                  AND table_name = 'farmers'
                  AND column_name = 'body'
              ) THEN
                IF to_regclass('capture.legacy_farmers_20261009') IS NOT NULL THEN
                  RAISE EXCEPTION 'Legacy farmer archive name already exists';
                END IF;
                IF EXISTS (SELECT 1 FROM capture.farmers) THEN
                  RAISE EXCEPTION 'Legacy farmers table contains data; reconcile before migrating';
                END IF;
                ALTER TABLE capture.farmers RENAME TO legacy_farmers_20261009;
              END IF;
            END
            $migration$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $migration$
            BEGIN
              IF to_regclass('capture.legacy_farmers_20261009') IS NOT NULL THEN
                IF to_regclass('capture.farmers') IS NOT NULL THEN
                  RAISE EXCEPTION 'Remove the new farmers table before restoring the legacy name';
                END IF;
                ALTER TABLE capture.legacy_farmers_20261009 RENAME TO farmers;
              END IF;
            END
            $migration$;
            """);
    }
}
