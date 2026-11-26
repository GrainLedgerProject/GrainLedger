using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127000000_LocationStockVarietyIdentity")]
public sealed class LocationStockVarietyIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION grainledger.ledger_location_stock(a uuid, location uuid, query jsonb) RETURNS jsonb
        LANGUAGE plpgsql STABLE SECURITY DEFINER
        SET search_path TO 'pg_catalog', 'grainledger', 'pg_temp'
        AS $$
        DECLARE
            actor_role text := grainledger.ledger_role(a);
            page_number integer := coalesce((query->>'page')::integer, 1);
            page_size integer := coalesce((query->>'pageSize')::integer, 25);
            result jsonb;
        BEGIN
            IF actor_role IS NULL OR actor_role NOT IN ('CustomerManager','ExportStaff','PurchasingStaff','MillManager','MillOperator') THEN
                RAISE EXCEPTION 'Stock access forbidden' USING ERRCODE='42501';
            END IF;
            IF page_number < 1 OR page_size NOT BETWEEN 1 AND 100 OR (page_number::bigint-1)*page_size > 2147483647 THEN
                RAISE EXCEPTION 'Invalid pagination' USING ERRCODE='22023';
            END IF;
            IF actor_role IN ('MillManager','MillOperator') AND NOT EXISTS (
                SELECT 1 FROM grainledger.ledger_locations lc
                WHERE lc.id=location AND NOT lc.is_deleted AND grainledger.ledger_assigned(a,lc.facility_id)
            ) THEN
                RAISE EXCEPTION 'Stock access forbidden' USING ERRCODE='42501';
            END IF;
            WITH scoped AS MATERIALIZED (
                SELECT l.id,l.code,l.stage,l.product_identity,l.variety_status,l.rice_variety_id,l.rice_variety_name,
                    o."Name" owner_name,p.quantity_kg,p.location_id
                FROM grainledger.ledger_positions p
                JOIN grainledger.ledger_locations lc ON lc.id=p.location_id AND NOT lc.is_deleted
                JOIN grainledger.ledger_facilities f ON f.id=lc.facility_id AND NOT f.is_deleted
                JOIN grainledger.ledger_lots l ON l.id=p.lot_id
                JOIN grainledger.identity_organizations o ON o."Id"=l.owner_id
                LEFT JOIN grainledger.ledger_mixed_bulk_pools pool ON pool.location_id=lc.id
                WHERE p.location_id=location AND p.quantity_kg>0
                  AND (NOT lc.mixed_bulk OR pool.lot_id=l.id)
                  AND ((actor_role IN ('CustomerManager','ExportStaff','PurchasingStaff') AND grainledger.ledger_own(a,l.owner_id))
                    OR (actor_role IN ('MillManager','MillOperator') AND grainledger.ledger_assigned(a,lc.facility_id)))
            ), selected AS MATERIALIZED (
                SELECT * FROM scoped ORDER BY code,id
                OFFSET (page_number::bigint-1)*page_size LIMIT page_size
            )
            SELECT jsonb_build_object(
                'locationId',location,
                'totalKg',coalesce((SELECT sum(quantity_kg) FROM scoped),0::numeric(20,6))::text,
                'items',coalesce((SELECT jsonb_agg(jsonb_build_object(
                    'lotId',s.id,'code',s.code,'stage',s.stage,'productIdentity',s.product_identity,
                    'riceVarietyId',s.rice_variety_id,'riceVariety',s.rice_variety_name,'varietyStatus',s.variety_status,'ownerName',s.owner_name,
                    'quantityKg',s.quantity_kg::text,
                    'availableKg',CASE WHEN grainledger.ledger_blocked(s.id) THEN '0.000000'
                        ELSE greatest(0,s.quantity_kg-grainledger.ledger_blend_reserved_kg(s.id,s.location_id))::numeric(20,6)::text END,
                    'restrictions',grainledger.ledger_lot_json(a,s.id,false)->'restrictions'
                ) ORDER BY s.code,s.id) FROM selected s),'[]'::jsonb),
                'page',page_number,'pageSize',page_size,'totalCount',(SELECT count(*) FROM scoped),
                'hasMore',(SELECT count(*) FROM scoped)>page_number::bigint*page_size
            ) INTO result;
            RETURN result;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Use a reviewed forward migration.");
}
