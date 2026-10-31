using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrainLedger.Modules.Exports.Migrations;

[DbContext(typeof(ExportDbContext))]
[Migration("20261019000000_InitialExports")]
public sealed class InitialExports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA IF NOT EXISTS exports;
        CREATE TABLE exports.export_orders (
            id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
            code text NOT NULL, market_id uuid NOT NULL REFERENCES catalog.markets(id),
            commodity_id uuid NOT NULL REFERENCES catalog.commodities(id),
            required_mass_kg numeric(20,6) NOT NULL CHECK (required_mass_kg > 0),
            original_mass text NOT NULL, original_unit text NOT NULL,
            shipping_from date NOT NULL, shipping_to date NOT NULL CHECK (shipping_to >= shipping_from),
            created_by uuid NOT NULL REFERENCES identity.users("Id"), created_at timestamptz NOT NULL,
            version integer NOT NULL DEFAULT 1 CHECK (version > 0),
            current_specification_version integer NOT NULL DEFAULT 1 CHECK (current_specification_version > 0),
            UNIQUE(company_id, id), UNIQUE(company_id, code)
        );
        CREATE INDEX ix_export_orders_company_created ON exports.export_orders(company_id, created_at DESC, id);
        CREATE TABLE exports.order_specifications (
            id uuid PRIMARY KEY, order_id uuid NOT NULL, company_id uuid NOT NULL,
            version_no integer NOT NULL CHECK (version_no > 0), recorded_by uuid NOT NULL REFERENCES identity.users("Id"),
            recorded_at timestamptz NOT NULL,
            FOREIGN KEY(company_id, order_id) REFERENCES exports.export_orders(company_id, id),
            UNIQUE(order_id, version_no), UNIQUE(order_id, id)
        );
        CREATE TABLE exports.specification_limits (
            id uuid PRIMARY KEY, specification_id uuid NOT NULL REFERENCES exports.order_specifications(id),
            attribute_id uuid NOT NULL REFERENCES catalog.quality_attributes(id),
            attribute_code text NOT NULL, attribute_name text NOT NULL, unit text NOT NULL,
            min_value numeric(24,12), max_value numeric(24,12),
            CHECK (min_value IS NOT NULL OR max_value IS NOT NULL),
            CHECK (min_value IS NULL OR max_value IS NULL OR min_value <= max_value),
            UNIQUE(specification_id, attribute_id)
        );
        CREATE TABLE exports.containers (
            id uuid PRIMARY KEY, order_id uuid NOT NULL, company_id uuid NOT NULL,
            internal_code text NOT NULL, target_mass_kg numeric(20,6) NOT NULL CHECK (target_mass_kg > 0),
            original_mass text NOT NULL, original_unit text NOT NULL,
            status text NOT NULL DEFAULT 'PLANNED' CHECK (status IN ('PLANNED','LOADING','CLOSED')),
            version integer NOT NULL DEFAULT 1 CHECK (version > 0), created_at timestamptz NOT NULL,
            FOREIGN KEY(company_id, order_id) REFERENCES exports.export_orders(company_id, id),
            UNIQUE(company_id, internal_code), UNIQUE(company_id, id)
        );
        CREATE INDEX ix_export_containers_order ON exports.containers(order_id, id);
        CREATE TABLE exports.events (
            id uuid PRIMARY KEY, order_id uuid NOT NULL, company_id uuid NOT NULL,
            actor_id uuid NOT NULL REFERENCES identity.users("Id"), action text NOT NULL,
            snapshot jsonb NOT NULL, recorded_at timestamptz NOT NULL,
            FOREIGN KEY(company_id, order_id) REFERENCES exports.export_orders(company_id, id)
        );
        CREATE INDEX ix_export_events_order ON exports.events(order_id, recorded_at, id);
        CREATE TABLE exports.idempotent_operations (
            id uuid PRIMARY KEY, company_id uuid NOT NULL, actor_id uuid NOT NULL REFERENCES identity.users("Id"),
            operation_id uuid NOT NULL, request_hash text NOT NULL,
            order_id uuid NOT NULL, container_id uuid, specification_version integer,
            response_json jsonb NOT NULL, response_status integer NOT NULL, response_version integer NOT NULL,
            FOREIGN KEY(company_id, order_id) REFERENCES exports.export_orders(company_id, id),
            FOREIGN KEY(company_id, container_id) REFERENCES exports.containers(company_id, id),
            UNIQUE(company_id, actor_id, operation_id)
        );
        CREATE TABLE exports.code_counters (
            company_id uuid NOT NULL REFERENCES identity.organizations("Id"),
            kind text NOT NULL CHECK (kind IN ('EXP','CTN')),
            next_value bigint NOT NULL CHECK (next_value > 0),
            PRIMARY KEY(company_id, kind)
        );
        CREATE FUNCTION exports.reject_historical_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Export history is immutable' USING ERRCODE='23514';
        END;
        $$;
        CREATE TRIGGER immutable_specifications BEFORE UPDATE OR DELETE ON exports.order_specifications
            FOR EACH ROW EXECUTE FUNCTION exports.reject_historical_change();
        CREATE TRIGGER immutable_limits BEFORE UPDATE OR DELETE ON exports.specification_limits
            FOR EACH ROW EXECUTE FUNCTION exports.reject_historical_change();
        CREATE TRIGGER immutable_events BEFORE UPDATE OR DELETE ON exports.events
            FOR EACH ROW EXECUTE FUNCTION exports.reject_historical_change();
        CREATE TRIGGER immutable_operations BEFORE UPDATE OR DELETE ON exports.idempotent_operations
            FOR EACH ROW EXECUTE FUNCTION exports.reject_historical_change();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Export order history and issued codes cannot be rolled back safely.");
}
