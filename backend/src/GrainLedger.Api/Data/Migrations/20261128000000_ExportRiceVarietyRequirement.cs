using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128000000_ExportRiceVarietyRequirement")]
public sealed class ExportRiceVarietyRequirement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE grainledger.exports_order_specifications
          ADD COLUMN required_rice_variety_id uuid REFERENCES grainledger.catalog_rice_varieties(id) ON DELETE RESTRICT,
          ADD COLUMN required_rice_variety_name text,
          ADD CONSTRAINT exports_specification_variety_pair CHECK (
            (required_rice_variety_id IS NULL AND required_rice_variety_name IS NULL) OR
            (required_rice_variety_id IS NOT NULL AND nullif(btrim(required_rice_variety_name),'') IS NOT NULL));
        CREATE INDEX exports_specification_variety_idx ON grainledger.exports_order_specifications(required_rice_variety_id);
        DO $migration$
        DECLARE definition text; marker text := '''marketId'',o.market_id';
        BEGIN
          IF to_regprocedure('grainledger.exports_residue_context(uuid,timestamp with time zone)') IS NULL THEN RETURN; END IF;
          SELECT pg_get_functiondef('grainledger.exports_residue_context(uuid,timestamp with time zone)'::regprocedure) INTO definition;
          IF position(marker IN definition)=0 THEN RAISE EXCEPTION 'Unexpected export residue context definition'; END IF;
          EXECUTE replace(definition,marker,$patch$
            'requiredRiceVarietyId',(SELECT s.required_rice_variety_id FROM grainledger.exports_order_specifications s WHERE s.order_id=o.id AND s.version_no=o.current_specification_version),
            'lotVarieties',(SELECT coalesce(jsonb_object_agg(l.id::text,jsonb_build_object('riceVarietyId',l.rice_variety_id,'varietyStatus',l.variety_status)),'{}') FROM grainledger.ledger_lots l WHERE l.id=ANY(lots)),
            'marketId',o.market_id$patch$);
        END $migration$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Use a forward migration to preserve specification history.");
}
