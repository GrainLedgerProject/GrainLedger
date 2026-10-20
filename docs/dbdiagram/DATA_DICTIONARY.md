# Live PostgreSQL data dictionary

Catalog snapshot captured **2026-10-19** (`2026-10-19T15:08:32.454Z`); 61 tables, 424 columns, 120 foreign keys, 57 CHECK constraints, 154 indexes. Includes 60 business/auth tables and technical `ledger.__EFMigrationsHistory`. Snapshot is authoritative for deployed PostgreSQL state, not migration history or future design.

Types, identifier case, nullability and defaults below come from live catalog. `NOT NULL` means required. Defaults are SQL expressions. No data values are included.

## Interpretation and expressiveness

- Physical foreign keys below are catalog constraints only. They do not imply application authorization, tenant ownership, role checks, workflow ordering, or semantic consistency beyond declared key constraints.
- Application-level rules are not database constraints. JSON values and actor/ID values without physical FKs remain unlinked. Application checks are not inferred as constraints.
- DBML represents tables/columns/types/nullability/defaults, primary/unique keys, ordinary btree indexes on columns, and foreign-key targets/actions. It does not represent CHECK constraints or trigger definitions, or fully encode partial-index predicates, expression indexes, index sort direction/opclasses/include columns, constraint deferrability, and some PostgreSQL-specific options. Exact CHECK constraints, trigger-constraint metadata, and index SQL available in this snapshot are inventoried below; snapshot does not include trigger bodies.
- Partial unique indexes are intentionally absent as unconditional DBML unique keys. Their exact predicates and SQL appear in index inventory.
- `identity.audit_events_Id_seq` and `ledger.lot_code_serial` sequence configuration shown without live `last_value`.
- DBML `increment` marks the generated identity column; exact PostgreSQL generation mode is recorded in its column note and the table below.
- Snapshot does not establish that runtime API role can read `ledger.receiving_threshold_*`; do not claim it can.

## Tables and columns

### `capture.business_events`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NULL | — | — |
| `actor_id` | `uuid` | NULL | — | — |
| `event_type` | `character varying(60)` | NOT NULL | — | — |
| `occurred_at` | `timestamp with time zone` | NOT NULL | — | — |
| `recorded_at` | `timestamp with time zone` | NOT NULL | — | — |
| `correlation_id` | `uuid` | NOT NULL | — | — |
| `payload` | `jsonb` | NOT NULL | — | — |


### `capture.code_counters`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `company_id` | `uuid` | NOT NULL | — | — |
| `kind` | `character varying(3)` | NOT NULL | — | — |
| `scope` | `character varying(32)` | NOT NULL | — | — |
| `next_value` | `bigint` | NOT NULL | — | — |


### `capture.crop_seasons`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `starts_on` | `date` | NOT NULL | — | — |
| `ends_on` | `date` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `integer` | NOT NULL | `1` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `capture.farmers`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `code` | `character varying(40)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `contact` | `text` | NULL | — | — |
| `created_at` | `timestamp with time zone` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `integer` | NOT NULL | `1` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |
| `code_number` | `bigint` | NULL | — | — |


### `capture.offline_operations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `submitted_by` | `uuid` | NOT NULL | — | — |
| `device_id` | `character varying(100)` | NOT NULL | — | — |
| `client_operation_id` | `uuid` | NOT NULL | — | — |
| `payload_hash` | `character varying(128)` | NOT NULL | — | — |
| `payload` | `jsonb` | NOT NULL | — | — |
| `captured_at` | `timestamp with time zone` | NOT NULL | — | — |
| `capture_geojson` | `jsonb` | NULL | — | — |
| `received_at` | `timestamp with time zone` | NOT NULL | — | — |
| `status` | `character varying(32)` | NOT NULL | — | — |
| `applied_event_id` | `uuid` | NULL | — | — |
| `result_payload` | `jsonb` | NULL | — | — |
| `conflict_reason` | `text` | NULL | — | — |


### `capture.plot_seasons`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `plot_id` | `uuid` | NOT NULL | — | — |
| `season_id` | `uuid` | NOT NULL | — | — |
| `rice_variety` | `text` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `integer` | NOT NULL | `1` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `capture.plots`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `farmer_id` | `uuid` | NOT NULL | — | — |
| `code` | `character varying(40)` | NOT NULL | — | — |
| `boundary_geojson` | `jsonb` | NOT NULL | — | — |
| `area_ha` | `numeric(24,12)` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `integer` | NOT NULL | `1` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `capture.procurement_items`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `procurement_id` | `uuid` | NOT NULL | — | — |
| `line_no` | `integer` | NOT NULL | — | — |
| `plot_season_id` | `uuid` | NOT NULL | — | — |
| `net_mass_kg` | `numeric(20,6)` | NOT NULL | — | — |
| `measured_value` | `numeric(24,12)` | NOT NULL | — | — |
| `measured_unit` | `character varying(20)` | NOT NULL | — | — |
| `moisture_percent` | `numeric(24,12)` | NOT NULL | — | — |


### `capture.procurements`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `code` | `character varying(40)` | NOT NULL | — | — |
| `farmer_id` | `uuid` | NOT NULL | — | — |
| `collection_site_id` | `uuid` | NOT NULL | — | — |
| `received_by` | `uuid` | NOT NULL | — | — |
| `received_at` | `timestamp with time zone` | NOT NULL | — | — |
| `status` | `character varying(32)` | NOT NULL | — | — |
| `event_id` | `uuid` | NULL | — | — |
| `receiving_workflow` | `text` | NULL | `'PHYSICAL_ARRIVAL_V1'::text` | — |


### `capture.sample_handovers`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `sample_id` | `uuid` | NOT NULL | — | — |
| `recorded_by` | `uuid` | NOT NULL | — | — |
| `from_party` | `text` | NOT NULL | — | — |
| `to_party` | `text` | NOT NULL | — | — |
| `handed_over_at` | `timestamp with time zone` | NOT NULL | — | — |
| `seal_condition` | `text` | NOT NULL | — | — |
| `evidence_ref` | `text` | NULL | — | — |


### `capture.samples`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `code` | `character varying(60)` | NOT NULL | — | — |
| `source_lot_id` | `uuid` | NOT NULL | — | — |
| `seal_code` | `character varying(100)` | NOT NULL | — | — |
| `collected_by` | `uuid` | NOT NULL | — | — |
| `collected_at` | `timestamp with time zone` | NOT NULL | — | — |


### `capture.source_lots`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `procurement_item_id` | `uuid` | NOT NULL | — | — |


### `capture.spray_declarations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `plot_season_id` | `uuid` | NOT NULL | — | — |
| `applied_on` | `date` | NOT NULL | — | — |
| `recorded_by` | `uuid` | NOT NULL | — | — |
| `recorded_at` | `timestamp with time zone` | NOT NULL | — | — |
| `evidence_ref` | `text` | NULL | — | — |


### `capture.spray_items`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `company_id` | `uuid` | NOT NULL | — | — |
| `declaration_id` | `uuid` | NOT NULL | — | — |
| `product_name` | `text` | NULL | — | — |
| `substance_id` | `uuid` | NULL | — | — |
| `rate` | `numeric(24,12)` | NULL | — | — |
| `rate_unit` | `character varying(40)` | NULL | — | — |


### `catalog.active_substances`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `code` | `character varying(60)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |


### `catalog.catalog_events`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `actor_id` | `uuid` | NOT NULL | — | — |
| `event_type` | `character varying(60)` | NOT NULL | — | — |
| `resource_type` | `character varying(60)` | NOT NULL | — | — |
| `resource_id` | `uuid` | NOT NULL | — | — |
| `occurred_at` | `timestamp with time zone` | NOT NULL | — | — |
| `correlation_id` | `uuid` | NOT NULL | — | — |
| `before_state` | `jsonb` | NULL | — | — |
| `after_state` | `jsonb` | NULL | — | — |


### `catalog.commodities`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `code` | `character varying(40)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `is_archived` | `boolean` | NOT NULL | `false` | — |


### `catalog.markets`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `code` | `character varying(40)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |


### `catalog.measurement_units`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `code` | `character varying(30)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `quantity_kind` | `character varying(32)` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `catalog.mrl_limits`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `mrl_version_id` | `uuid` | NOT NULL | — | — |
| `substance_id` | `uuid` | NOT NULL | — | — |
| `limit_value` | `numeric(24,12)` | NOT NULL | — | — |
| `unit` | `character varying(20)` | NOT NULL | — | — |


### `catalog.mrl_versions`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `market_id` | `uuid` | NOT NULL | — | — |
| `commodity_id` | `uuid` | NOT NULL | — | — |
| `version_no` | `integer` | NOT NULL | — | — |
| `status` | `character varying(32)` | NOT NULL | — | — |
| `effective_from` | `timestamp with time zone` | NOT NULL | — | — |
| `effective_to` | `timestamp with time zone` | NULL | — | — |
| `published_by` | `uuid` | NULL | — | — |
| `published_at` | `timestamp with time zone` | NULL | — | — |
| `source_ref` | `text` | NOT NULL | — | — |


### `catalog.quality_attributes`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `code` | `character varying(60)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `unit` | `character varying(30)` | NOT NULL | — | — |


### `catalog.rice_varieties`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `code` | `character varying(40)` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |


### `identity.audit_events`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `Id` | `bigint` | NOT NULL | — | GENERATED BY DEFAULT AS IDENTITY |
| `ActorUserId` | `uuid` | NULL | — | — |
| `SubjectUserId` | `uuid` | NULL | — | — |
| `Action` | `character varying(100)` | NOT NULL | — | — |
| `DetailsJson` | `jsonb` | NOT NULL | — | — |
| `OccurredAt` | `timestamp with time zone` | NOT NULL | — | — |
| `IpAddress` | `text` | NULL | — | — |


### `identity.organizations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | NOT NULL | — | — |
| `Name` | `character varying(160)` | NOT NULL | — | — |
| `Kind` | `character varying(20)` | NOT NULL | — | — |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | — | — |


### `identity.password_reset_codes`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | NOT NULL | — | — |
| `UserId` | `uuid` | NOT NULL | — | — |
| `CodeHash` | `character varying(64)` | NOT NULL | — | — |
| `FailedAttempts` | `integer` | NOT NULL | — | — |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | — | — |
| `ExpiresAt` | `timestamp with time zone` | NOT NULL | — | — |
| `LastSentAt` | `timestamp with time zone` | NOT NULL | — | — |
| `ConsumedAt` | `timestamp with time zone` | NULL | — | — |


### `identity.registration_attempts`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | NOT NULL | — | — |
| `Email` | `character varying(320)` | NOT NULL | — | — |
| `NormalizedEmail` | `character varying(320)` | NOT NULL | — | — |
| `DisplayName` | `character varying(120)` | NOT NULL | — | — |
| `PasswordHash` | `character varying(1000)` | NOT NULL | — | — |
| `CodeHash` | `character varying(64)` | NOT NULL | — | — |
| `FailedAttempts` | `integer` | NOT NULL | — | — |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | — | — |
| `ExpiresAt` | `timestamp with time zone` | NOT NULL | — | — |
| `LastSentAt` | `timestamp with time zone` | NOT NULL | — | — |
| `InvalidatedAt` | `timestamp with time zone` | NULL | — | — |
| `ConsumedAt` | `timestamp with time zone` | NULL | — | — |


### `identity.sessions`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | NOT NULL | — | — |
| `UserId` | `uuid` | NOT NULL | — | — |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | — | — |
| `LastSeenAt` | `timestamp with time zone` | NOT NULL | — | — |
| `ExpiresAt` | `timestamp with time zone` | NOT NULL | — | — |
| `RevokedAt` | `timestamp with time zone` | NULL | — | — |
| `IpAddress` | `character varying(64)` | NULL | — | — |
| `UserAgent` | `character varying(512)` | NULL | — | — |
| `FamilyId` | `uuid` | NOT NULL | `'00000000-0000-0000-0000-000000000000'::uuid` | — |
| `RefreshTokenHash` | `character varying(64)` | NULL | — | — |
| `ReplacedBySessionId` | `uuid` | NULL | — | — |
| `RefreshTokenUsedAt` | `timestamp with time zone` | NULL | — | — |


### `identity.users`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `Id` | `uuid` | NOT NULL | — | — |
| `Provider` | `character varying(30)` | NOT NULL | — | — |
| `ProviderSubject` | `character varying(255)` | NOT NULL | — | — |
| `Email` | `character varying(320)` | NOT NULL | — | — |
| `DisplayName` | `character varying(120)` | NOT NULL | — | — |
| `PhoneNumber` | `character varying(32)` | NULL | — | — |
| `Role` | `character varying(40)` | NULL | — | — |
| `OrganizationId` | `uuid` | NULL | — | — |
| `IsActive` | `boolean` | NOT NULL | — | — |
| `Version` | `uuid` | NOT NULL | — | — |
| `CreatedAt` | `timestamp with time zone` | NOT NULL | — | — |
| `UpdatedAt` | `timestamp with time zone` | NOT NULL | — | — |
| `FailedLoginCount` | `integer` | NOT NULL | `0` | — |
| `LockoutEnd` | `timestamp with time zone` | NULL | — | — |
| `NormalizedEmail` | `character varying(320)` | NULL | — | — |
| `PasswordHash` | `character varying(1000)` | NULL | — | — |


### `ledger.__EFMigrationsHistory`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `MigrationId` | `character varying(150)` | NOT NULL | — | — |
| `ProductVersion` | `character varying(32)` | NOT NULL | — | — |


### `ledger.arrival_measurement_sets`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `procurement_id` | `uuid` | NOT NULL | — | — |
| `operation_id` | `uuid` | NOT NULL | — | — |
| `operator_id` | `uuid` | NOT NULL | — | — |
| `captured_at` | `timestamp with time zone` | NOT NULL | — | — |
| `recorded_at` | `timestamp with time zone` | NOT NULL | `clock_timestamp()` | — |
| `observations` | `text` | NOT NULL | — | — |
| `expected_version` | `integer` | NOT NULL | — | — |
| `threshold_company_id` | `uuid` | NULL | — | — |
| `threshold_percentage` | `numeric(8,6)` | NULL | — | — |
| `threshold_source` | `text` | NULL | — | — |
| `threshold_revision` | `uuid` | NULL | — | — |


### `ledger.arrival_measurements`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `measurement_set_id` | `uuid` | NOT NULL | — | — |
| `procurement_item_id` | `uuid` | NOT NULL | — | — |
| `measured_value` | `numeric(24,12)` | NOT NULL | — | — |
| `measured_unit` | `text` | NOT NULL | — | — |
| `actual_kg` | `numeric(20,6)` | NOT NULL | — | — |


### `ledger.arrival_operations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `operation_id` | `uuid` | NOT NULL | — | — |
| `procurement_id` | `uuid` | NOT NULL | — | — |
| `actor_id` | `uuid` | NOT NULL | — | — |
| `kind` | `text` | NOT NULL | — | — |
| `payload` | `jsonb` | NOT NULL | — | — |
| `result` | `jsonb` | NOT NULL | — | — |
| `recorded_at` | `timestamp with time zone` | NOT NULL | `clock_timestamp()` | — |


### `ledger.arrival_reviews`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `procurement_id` | `uuid` | NOT NULL | — | — |
| `measurement_set_id` | `uuid` | NOT NULL | — | — |
| `operation_id` | `uuid` | NOT NULL | — | — |
| `expected_version` | `integer` | NOT NULL | — | — |
| `action` | `text` | NOT NULL | — | — |
| `reason` | `text` | NOT NULL | — | — |
| `evidence_id` | `uuid` | NOT NULL | — | — |
| `manager_id` | `uuid` | NOT NULL | — | — |
| `recorded_at` | `timestamp with time zone` | NOT NULL | `clock_timestamp()` | — |


### `ledger.assignments`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `user_id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `ledger.catalog_commodities`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `catalog_commodity_id` | `uuid` | NOT NULL | — | — |
| `ledger_commodity_id` | `uuid` | NOT NULL | — | — |


### `ledger.commodities`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |


### `ledger.compatibilities`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `input_products` | `text[]` | NOT NULL | — | — |
| `output_commodity_id` | `uuid` | NOT NULL | — | — |
| `effective_from` | `timestamp with time zone` | NOT NULL | — | — |
| `evidence_id` | `uuid` | NOT NULL | — | — |


### `ledger.contributions`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `source_lot_id` | `uuid` | NOT NULL | — | — |
| `quantity_kg` | `numeric(20,6)` | NOT NULL | — | — |


### `ledger.corrections`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `operation_id` | `uuid` | NOT NULL | — | — |
| `reason` | `text` | NOT NULL | — | — |
| `status` | `text` | NOT NULL | `'REQUESTED'::text` | — |
| `evidence_id` | `uuid` | NULL | — | — |
| `reconciliation` | `jsonb` | NULL | — | — |
| `replacement_operation_id` | `uuid` | NULL | — | — |
| `replacement_version` | `uuid` | NULL | — | — |
| `replacement_content` | `jsonb` | NULL | — | — |


### `ledger.equipment`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `ledger.events`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `kind` | `text` | NOT NULL | — | — |
| `actor_id` | `uuid` | NOT NULL | — | — |
| `operation_id` | `uuid` | NULL | — | — |
| `lot_id` | `uuid` | NULL | — | — |
| `details` | `jsonb` | NOT NULL | — | — |
| `occurred_at` | `timestamp with time zone` | NOT NULL | `now()` | — |


### `ledger.evidence`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `owner_id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `file_name` | `text` | NOT NULL | — | — |
| `content_type` | `text` | NOT NULL | — | — |
| `content` | `bytea` | NOT NULL | — | — |
| `sha256` | `text` | NOT NULL | — | — |
| `actor_id` | `uuid` | NOT NULL | — | — |
| `created_at` | `timestamp with time zone` | NOT NULL | `now()` | — |


### `ledger.facilities`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `ledger.holds`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `reason` | `text` | NOT NULL | — | — |
| `status` | `text` | NOT NULL | `'ACTIVE'::text` | — |
| `evidence_id` | `uuid` | NULL | — | — |
| `response` | `text` | NULL | — | — |
| `created_at` | `timestamp with time zone` | NOT NULL | `now()` | — |


### `ledger.locations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `name` | `text` | NOT NULL | — | — |
| `kind` | `text` | NOT NULL | — | — |
| `owner_id` | `uuid` | NULL | — | — |
| `cleared` | `boolean` | NOT NULL | `true` | — |
| `created_by` | `uuid` | NULL | — | — |
| `version` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |


### `ledger.lot_code_reservations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `code` | `text` | NOT NULL | — | — |
| `owner_id` | `uuid` | NOT NULL | — | — |
| `context_kind` | `text` | NOT NULL | — | — |
| `context_id` | `uuid` | NOT NULL | — | — |
| `stage` | `text` | NOT NULL | — | — |
| `product_identity` | `text` | NOT NULL | — | — |
| `issued_at` | `timestamp with time zone` | NOT NULL | `clock_timestamp()` | — |


### `ledger.lot_codes`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `code` | `text` | NOT NULL | — | — |
| `origin_lot_id` | `uuid` | NOT NULL | — | — |


### `ledger.lots`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `code` | `text` | NOT NULL | — | — |
| `code_origin_id` | `uuid` | NOT NULL | — | — |
| `owner_id` | `uuid` | NOT NULL | — | — |
| `commodity_id` | `uuid` | NOT NULL | — | — |
| `stage` | `text` | NOT NULL | — | — |
| `product_identity` | `text` | NOT NULL | — | — |
| `receipt_id` | `uuid` | NULL | — | — |
| `receipt_line_id` | `uuid` | NULL | — | — |
| `operation_id` | `uuid` | NULL | — | — |
| `created_at` | `timestamp with time zone` | NOT NULL | `now()` | — |


### `ledger.notifications`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `user_id` | `uuid` | NOT NULL | — | — |
| `hold_id` | `uuid` | NOT NULL | — | — |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `message` | `text` | NOT NULL | — | — |
| `created_at` | `timestamp with time zone` | NOT NULL | `now()` | — |
| `read_at` | `timestamp with time zone` | NULL | — | — |


### `ledger.operations`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `owner_id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `kind` | `text` | NOT NULL | — | — |
| `occurred_at` | `timestamp with time zone` | NOT NULL | — | — |
| `equipment_id` | `uuid` | NOT NULL | — | — |
| `content` | `jsonb` | NOT NULL | — | — |
| `status` | `text` | NOT NULL | `'DRAFT'::text` | — |
| `version` | `uuid` | NOT NULL | — | — |
| `confirmed_version` | `uuid` | NULL | — | — |
| `confirmed_by` | `uuid` | NULL | — | — |
| `confirmation_evidence_id` | `uuid` | NULL | — | — |
| `variance_kg` | `numeric(20,6)` | NULL | — | — |
| `allowed_variance_kg` | `numeric(20,6)` | NULL | — | — |
| `correction_id` | `uuid` | NULL | — | — |
| `created_by` | `uuid` | NULL | — | — |
| `is_deleted` | `boolean` | NOT NULL | `false` | — |
| `lot_code_scheme` | `text` | NOT NULL | `'LEGACY'::text` | — |


### `ledger.policies`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | — | — |
| `facility_id` | `uuid` | NOT NULL | — | — |
| `kind` | `text` | NOT NULL | — | — |
| `tolerance_percent` | `numeric(20,6)` | NOT NULL | — | — |
| `effective_from` | `timestamp with time zone` | NOT NULL | — | — |
| `evidence_id` | `uuid` | NOT NULL | — | — |


### `ledger.positions`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `location_id` | `uuid` | NOT NULL | — | — |
| `quantity_kg` | `numeric(20,6)` | NOT NULL | — | — |


### `ledger.postings`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `id` | `uuid` | NOT NULL | `gen_random_uuid()` | — |
| `event_id` | `uuid` | NOT NULL | — | — |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `location_id` | `uuid` | NOT NULL | — | — |
| `quantity_kg` | `numeric(20,6)` | NOT NULL | — | — |
| `reverses_posting_id` | `uuid` | NULL | — | — |


### `ledger.receipt_correction_blocks`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `correction_id` | `uuid` | NOT NULL | — | — |
| `receipt_id` | `uuid` | NOT NULL | — | — |
| `owner_id` | `uuid` | NOT NULL | — | — |
| `committed` | `boolean` | NOT NULL | `false` | — |


### `ledger.receipt_reversals`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `receipt_id` | `uuid` | NOT NULL | — | — |
| `correction_id` | `uuid` | NOT NULL | — | — |
| `owner_id` | `uuid` | NOT NULL | — | — |
| `evidence_id` | `uuid` | NOT NULL | — | — |


### `ledger.receiving`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `procurement_id` | `uuid` | NOT NULL | — | — |
| `status` | `text` | NOT NULL | — | — |
| `version` | `integer` | NOT NULL | `0` | — |
| `latest_measurement_set_id` | `uuid` | NULL | — | — |
| `accepted_measurement_set_id` | `uuid` | NULL | — | — |
| `accepted_review_id` | `uuid` | NULL | — | — |


### `ledger.receiving_blocks`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `discrepancy_id` | `uuid` | NOT NULL | — | — |
| `lot_id` | `uuid` | NOT NULL | — | — |
| `blocked` | `boolean` | NOT NULL | — | — |
| `evidence_id` | `uuid` | NOT NULL | — | — |
| `reason` | `text` | NOT NULL | — | — |
| `real_difference` | `boolean` | NOT NULL | `false` | — |


### `ledger.receiving_threshold_default`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `singleton` | `boolean` | NOT NULL | `true` | — |
| `percentage` | `numeric(8,6)` | NOT NULL | — | — |
| `revision` | `uuid` | NOT NULL | — | — |


### `ledger.receiving_threshold_history`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `revision` | `uuid` | NOT NULL | — | — |
| `company_id` | `uuid` | NULL | — | — |
| `scope` | `text` | NOT NULL | — | — |
| `action` | `text` | NOT NULL | — | — |
| `old_percentage` | `numeric(8,6)` | NULL | — | — |
| `new_percentage` | `numeric(8,6)` | NULL | — | — |
| `actor_id` | `uuid` | NULL | — | — |
| `recorded_at` | `timestamp with time zone` | NOT NULL | `clock_timestamp()` | — |
| `reason` | `text` | NOT NULL | — | — |
| `migration_id` | `text` | NULL | — | — |


### `ledger.receiving_threshold_overrides`

| Column | PostgreSQL type | Nullability | Default | Generation |
| --- | --- | --- | --- | --- |
| `company_id` | `uuid` | NOT NULL | — | — |
| `percentage` | `numeric(8,6)` | NOT NULL | — | — |
| `revision` | `uuid` | NOT NULL | — | — |


## Constraints

### Primary keys (61)

- `capture.business_events` / `PK_business_events`: `PRIMARY KEY (id)`
- `capture.code_counters` / `pk_code_counters`: `PRIMARY KEY (company_id, kind, scope)`
- `capture.crop_seasons` / `PK_crop_seasons`: `PRIMARY KEY (id)`
- `capture.farmers` / `PK_farmers`: `PRIMARY KEY (id)`
- `capture.offline_operations` / `PK_offline_operations`: `PRIMARY KEY (id)`
- `capture.plot_seasons` / `PK_plot_seasons`: `PRIMARY KEY (id)`
- `capture.plots` / `PK_plots`: `PRIMARY KEY (id)`
- `capture.procurement_items` / `PK_procurement_items`: `PRIMARY KEY (id)`
- `capture.procurements` / `PK_procurements`: `PRIMARY KEY (id)`
- `capture.sample_handovers` / `PK_sample_handovers`: `PRIMARY KEY (id)`
- `capture.samples` / `PK_samples`: `PRIMARY KEY (id)`
- `capture.source_lots` / `PK_source_lots`: `PRIMARY KEY (id)`
- `capture.spray_declarations` / `PK_spray_declarations`: `PRIMARY KEY (id)`
- `capture.spray_items` / `PK_spray_items`: `PRIMARY KEY (id)`
- `catalog.active_substances` / `PK_active_substances`: `PRIMARY KEY (id)`
- `catalog.catalog_events` / `PK_catalog_events`: `PRIMARY KEY (id)`
- `catalog.commodities` / `PK_commodities`: `PRIMARY KEY (id)`
- `catalog.markets` / `PK_markets`: `PRIMARY KEY (id)`
- `catalog.measurement_units` / `PK_measurement_units`: `PRIMARY KEY (id)`
- `catalog.mrl_limits` / `PK_mrl_limits`: `PRIMARY KEY (id)`
- `catalog.mrl_versions` / `PK_mrl_versions`: `PRIMARY KEY (id)`
- `catalog.quality_attributes` / `PK_quality_attributes`: `PRIMARY KEY (id)`
- `catalog.rice_varieties` / `PK_rice_varieties`: `PRIMARY KEY (id)`
- `identity.audit_events` / `PK_audit_events`: `PRIMARY KEY ("Id")`
- `identity.organizations` / `PK_organizations`: `PRIMARY KEY ("Id")`
- `identity.password_reset_codes` / `PK_password_reset_codes`: `PRIMARY KEY ("Id")`
- `identity.registration_attempts` / `PK_registration_attempts`: `PRIMARY KEY ("Id")`
- `identity.sessions` / `PK_sessions`: `PRIMARY KEY ("Id")`
- `identity.users` / `PK_users`: `PRIMARY KEY ("Id")`
- `ledger.__EFMigrationsHistory` / `PK___EFMigrationsHistory`: `PRIMARY KEY ("MigrationId")`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_pkey`: `PRIMARY KEY (id)`
- `ledger.arrival_measurements` / `arrival_measurements_pkey`: `PRIMARY KEY (measurement_set_id, procurement_item_id)`
- `ledger.arrival_operations` / `arrival_operations_pkey`: `PRIMARY KEY (operation_id)`
- `ledger.arrival_reviews` / `arrival_reviews_pkey`: `PRIMARY KEY (id)`
- `ledger.assignments` / `assignments_pkey`: `PRIMARY KEY (user_id, facility_id)`
- `ledger.catalog_commodities` / `catalog_commodities_pkey`: `PRIMARY KEY (catalog_commodity_id)`
- `ledger.commodities` / `commodities_pkey`: `PRIMARY KEY (id)`
- `ledger.compatibilities` / `compatibilities_pkey`: `PRIMARY KEY (id)`
- `ledger.contributions` / `contributions_pkey`: `PRIMARY KEY (lot_id, source_lot_id)`
- `ledger.corrections` / `corrections_pkey`: `PRIMARY KEY (id)`
- `ledger.equipment` / `equipment_pkey`: `PRIMARY KEY (id)`
- `ledger.events` / `events_pkey`: `PRIMARY KEY (id)`
- `ledger.evidence` / `evidence_pkey`: `PRIMARY KEY (id)`
- `ledger.facilities` / `facilities_pkey`: `PRIMARY KEY (id)`
- `ledger.holds` / `holds_pkey`: `PRIMARY KEY (id)`
- `ledger.locations` / `locations_pkey`: `PRIMARY KEY (id)`
- `ledger.lot_code_reservations` / `lot_code_reservations_pkey`: `PRIMARY KEY (lot_id)`
- `ledger.lot_codes` / `lot_codes_pkey`: `PRIMARY KEY (code)`
- `ledger.lots` / `lots_pkey`: `PRIMARY KEY (id)`
- `ledger.notifications` / `notifications_pkey`: `PRIMARY KEY (id)`
- `ledger.operations` / `operations_pkey`: `PRIMARY KEY (id)`
- `ledger.policies` / `policies_pkey`: `PRIMARY KEY (id)`
- `ledger.positions` / `positions_pkey`: `PRIMARY KEY (lot_id, location_id)`
- `ledger.postings` / `postings_pkey`: `PRIMARY KEY (id)`
- `ledger.receipt_correction_blocks` / `receipt_correction_blocks_pkey`: `PRIMARY KEY (correction_id)`
- `ledger.receipt_reversals` / `receipt_reversals_pkey`: `PRIMARY KEY (receipt_id)`
- `ledger.receiving` / `receiving_pkey`: `PRIMARY KEY (procurement_id)`
- `ledger.receiving_blocks` / `receiving_blocks_pkey`: `PRIMARY KEY (discrepancy_id)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_pkey`: `PRIMARY KEY (singleton)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_pkey`: `PRIMARY KEY (revision)`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_pkey`: `PRIMARY KEY (company_id)`

### Unique constraints (16)

- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_operation_id_key`: `UNIQUE (operation_id)`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_procurement_id_id_key`: `UNIQUE (procurement_id, id)`
- `ledger.arrival_reviews` / `arrival_reviews_operation_id_key`: `UNIQUE (operation_id)`
- `ledger.arrival_reviews` / `arrival_reviews_procurement_id_id_key`: `UNIQUE (procurement_id, id)`
- `ledger.catalog_commodities` / `catalog_commodities_ledger_commodity_id_key`: `UNIQUE (ledger_commodity_id)`
- `ledger.corrections` / `corrections_operation_id_key`: `UNIQUE (operation_id)`
- `ledger.corrections` / `corrections_replacement_operation_id_key`: `UNIQUE (replacement_operation_id)`
- `ledger.lot_code_reservations` / `lot_code_reservations_code_key`: `UNIQUE (code)`
- `ledger.lot_codes` / `lot_codes_code_origin_lot_id_key`: `UNIQUE (code, origin_lot_id)`
- `ledger.lots` / `lots_receipt_line_id_key`: `UNIQUE (receipt_line_id)`
- `ledger.policies` / `policies_facility_id_kind_effective_from_key`: `UNIQUE (facility_id, kind, effective_from)`
- `ledger.postings` / `postings_reverses_posting_id_key`: `UNIQUE (reverses_posting_id)`
- `ledger.receipt_correction_blocks` / `receipt_correction_blocks_receipt_id_key`: `UNIQUE (receipt_id)`
- `ledger.receipt_reversals` / `receipt_reversals_correction_id_key`: `UNIQUE (correction_id)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_revision_key`: `UNIQUE (revision)`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_revision_key`: `UNIQUE (revision)`

### CHECK constraints (57)

- `capture.crop_seasons` / `ck_crop_seasons_dates`: `CHECK (ends_on >= starts_on)`
- `capture.offline_operations` / `ck_offline_operations_status`: `CHECK (status::text = ANY (ARRAY['PENDING'::character varying, 'APPLIED'::character varying, 'CONFLICT'::character varying, 'REJECTED'::character varying]::text[]))`
- `capture.plots` / `ck_plots_area_ha_positive`: `CHECK (area_ha > 0::numeric)`
- `capture.procurement_items` / `ck_procurement_items_line_no`: `CHECK (line_no > 0)`
- `capture.procurement_items` / `ck_procurement_items_measured`: `CHECK (measured_value > 0::numeric)`
- `capture.procurement_items` / `ck_procurement_items_moisture`: `CHECK (moisture_percent >= 0::numeric AND moisture_percent <= 100::numeric)`
- `capture.procurement_items` / `ck_procurement_items_net_mass`: `CHECK (net_mass_kg > 0::numeric)`
- `capture.procurements` / `ck_procurements_status`: `CHECK (status::text = ANY (ARRAY['DRAFT'::character varying, 'COMMITTED'::character varying]::text[]))`
- `capture.procurements` / `procurements_receiving_workflow_check`: `CHECK (receiving_workflow IS NULL OR receiving_workflow = 'PHYSICAL_ARRIVAL_V1'::text)`
- `capture.spray_items` / `ck_spray_items_product_or_substance`: `CHECK (product_name IS NOT NULL OR substance_id IS NOT NULL)`
- `catalog.catalog_events` / `ck_catalog_events_has_state`: `CHECK (before_state IS NOT NULL OR after_state IS NOT NULL)`
- `catalog.measurement_units` / `ck_measurement_units_quantity_kind`: `CHECK (quantity_kind::text = ANY (ARRAY['MASS'::character varying, 'AREA'::character varying, 'RESIDUE_CONCENTRATION'::character varying, 'APPLICATION_RATE'::character varying, 'FRACTION'::character varying]::text[]))`
- `catalog.mrl_limits` / `ck_mrl_limits_nonnegative`: `CHECK (limit_value >= 0::numeric)`
- `catalog.mrl_versions` / `ck_mrl_versions_effective_interval`: `CHECK (effective_to IS NULL OR effective_to > effective_from)`
- `catalog.mrl_versions` / `ck_mrl_versions_publication`: `CHECK (status::text = 'DRAFT'::text AND published_by IS NULL AND published_at IS NULL OR status::text = 'PUBLISHED'::text AND published_by IS NOT NULL AND published_at IS NOT NULL)`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_observations_check`: `CHECK (length(observations) <= 4000)`
- `ledger.arrival_measurement_sets` / `arrival_threshold_shape`: `CHECK (threshold_company_id IS NULL AND threshold_percentage IS NULL AND threshold_source IS NULL AND threshold_revision IS NULL OR threshold_company_id IS NOT NULL AND threshold_percentage IS NOT NULL AND threshold_percentage >= 0::numeric AND threshold_percentage < 100::numeric AND threshold_source IS NOT NULL AND (threshold_source = ANY (ARRAY['SYSTEM_DEFAULT'::text, 'COMPANY_OVERRIDE'::text])) AND threshold_revision IS NOT NULL)`
- `ledger.arrival_measurements` / `arrival_measurements_actual_kg_check`: `CHECK (actual_kg > 0::numeric)`
- `ledger.arrival_measurements` / `arrival_measurements_check`: `CHECK (actual_kg =
CASE measured_unit
    WHEN 'KG'::text THEN measured_value
    WHEN 'G'::text THEN measured_value / 1000::numeric
    WHEN 'T'::text THEN measured_value * 1000::numeric
    ELSE NULL::numeric
END)`
- `ledger.arrival_measurements` / `arrival_measurements_measured_unit_check`: `CHECK (measured_unit = ANY (ARRAY['KG'::text, 'G'::text, 'T'::text]))`
- `ledger.arrival_measurements` / `arrival_measurements_measured_value_check`: `CHECK (measured_value > 0::numeric)`
- `ledger.arrival_reviews` / `arrival_reviews_action_check`: `CHECK (action = ANY (ARRAY['APPROVE_SHORTAGE'::text, 'REQUEST_REMEASUREMENT'::text, 'KEEP_BLOCKED'::text]))`
- `ledger.arrival_reviews` / `arrival_reviews_reason_check`: `CHECK (length(TRIM(BOTH FROM reason)) >= 1 AND length(TRIM(BOTH FROM reason)) <= 4000)`
- `ledger.commodities` / `commodities_name_check`: `CHECK (length(name) > 0)`
- `ledger.compatibilities` / `compatibilities_input_products_check`: `CHECK (cardinality(input_products) > 0)`
- `ledger.contributions` / `contributions_quantity_kg_check`: `CHECK (quantity_kg >= 0::numeric)`
- `ledger.corrections` / `corrections_reason_check`: `CHECK (length(reason) > 0)`
- `ledger.corrections` / `corrections_status_check`: `CHECK (status = ANY (ARRAY['REQUESTED'::text, 'RECONCILED'::text, 'CANCELLED'::text, 'COMMITTED'::text]))`
- `ledger.equipment` / `equipment_name_check`: `CHECK (length(name) >= 1 AND length(name) <= 160)`
- `ledger.evidence` / `evidence_content_check`: `CHECK (octet_length(content) >= 1 AND octet_length(content) <= 10485760)`
- `ledger.facilities` / `facilities_name_check`: `CHECK (length(name) >= 1 AND length(name) <= 160)`
- `ledger.holds` / `holds_reason_check`: `CHECK (length(reason) > 0)`
- `ledger.locations` / `locations_kind_check`: `CHECK (kind = ANY (ARRAY['STORE'::text, 'SILO'::text, 'COMPARTMENT'::text, 'ISOLATION'::text]))`
- `ledger.locations` / `locations_name_check`: `CHECK (length(name) >= 1 AND length(name) <= 160)`
- `ledger.lot_code_reservations` / `lot_code_reservations_context_kind_check`: `CHECK (context_kind = ANY (ARRAY['SOURCE'::text, 'OPERATION'::text]))`
- `ledger.lots` / `lots_stage_check`: `CHECK (stage = ANY (ARRAY['RAW_PADDY'::text, 'DRIED_PADDY'::text, 'HEAD_RICE'::text, 'BROKEN_RICE'::text, 'BRAN'::text, 'HUSK'::text, 'BLENDED_RICE'::text, 'BAGGED_RICE'::text]))`
- `ledger.operations` / `operations_kind_check`: `CHECK (kind = ANY (ARRAY['DRYING'::text, 'MILLING'::text, 'MERGE'::text, 'BLENDING'::text, 'BAGGING'::text]))`
- `ledger.operations` / `operations_lot_code_scheme_check`: `CHECK (lot_code_scheme = ANY (ARRAY['LEGACY'::text, 'GENERATED'::text]))`
- `ledger.operations` / `operations_status_check`: `CHECK (status = ANY (ARRAY['DRAFT'::text, 'COMMITTED'::text, 'CORRECTED'::text]))`
- `ledger.policies` / `policies_kind_check`: `CHECK (kind = ANY (ARRAY['DRYING'::text, 'MILLING'::text, 'MERGE'::text, 'BLENDING'::text, 'BAGGING'::text]))`
- `ledger.policies` / `policies_tolerance_percent_check`: `CHECK (tolerance_percent >= 0::numeric AND tolerance_percent <= 100::numeric)`
- `ledger.positions` / `positions_quantity_kg_check`: `CHECK (quantity_kg >= 0::numeric)`
- `ledger.postings` / `postings_quantity_kg_check`: `CHECK (quantity_kg <> 0::numeric)`
- `ledger.receiving` / `receiving_check`: `CHECK ((status = ANY (ARRAY['RECEIVED_MATCHED'::text, 'RECEIVED_WITHIN_TOLERANCE'::text, 'RECEIVED_SHORTAGE_APPROVED'::text])) = (accepted_measurement_set_id IS NOT NULL))`
- `ledger.receiving` / `receiving_check1`: `CHECK ((status = 'RECEIVED_SHORTAGE_APPROVED'::text) = (accepted_review_id IS NOT NULL))`
- `ledger.receiving` / `receiving_status_check`: `CHECK (status = ANY (ARRAY['PENDING_ARRIVAL'::text, 'AWAITING_MANAGER_REVIEW'::text, 'REMEASUREMENT_REQUESTED'::text, 'RECEIVED_MATCHED'::text, 'RECEIVED_WITHIN_TOLERANCE'::text, 'RECEIVED_SHORTAGE_APPROVED'::text, 'BLOCKED_UNRESOLVED'::text]))`
- `ledger.receiving` / `receiving_version_check`: `CHECK (version >= 0)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_percentage_check`: `CHECK (percentage >= 0::numeric AND percentage < 100::numeric)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_singleton_check`: `CHECK (singleton)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_action_check`: `CHECK (action = ANY (ARRAY['INITIALIZE'::text, 'CREATE'::text, 'UPDATE'::text, 'DELETE'::text]))`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_check`: `CHECK ((scope = 'SYSTEM_DEFAULT'::text) = (company_id IS NULL))`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_check1`: `CHECK (action = 'INITIALIZE'::text AND scope = 'SYSTEM_DEFAULT'::text AND actor_id IS NULL AND migration_id IS NOT NULL AND old_percentage IS NULL AND new_percentage IS NOT NULL OR action <> 'INITIALIZE'::text AND actor_id IS NOT NULL AND migration_id IS NULL AND (action = 'CREATE'::text AND scope = 'COMPANY_OVERRIDE'::text AND old_percentage IS NULL AND new_percentage IS NOT NULL OR action = 'UPDATE'::text AND old_percentage IS NOT NULL AND new_percentage IS NOT NULL OR action = 'DELETE'::text AND scope = 'COMPANY_OVERRIDE'::text AND old_percentage IS NOT NULL AND new_percentage IS NULL))`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_new_percentage_check`: `CHECK (new_percentage >= 0::numeric AND new_percentage < 100::numeric)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_old_percentage_check`: `CHECK (old_percentage >= 0::numeric AND old_percentage < 100::numeric)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_reason_check`: `CHECK (length(TRIM(BOTH FROM reason)) >= 1 AND length(TRIM(BOTH FROM reason)) <= 4000)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_scope_check`: `CHECK (scope = ANY (ARRAY['SYSTEM_DEFAULT'::text, 'COMPANY_OVERRIDE'::text]))`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_percentage_check`: `CHECK (percentage >= 0::numeric AND percentage < 100::numeric)`

### Trigger constraint metadata (13)

- `capture.business_events` / `procurement_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `capture.offline_operations` / `procurement_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `capture.procurement_items` / `procurement_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `capture.procurements` / `procurement_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `capture.source_lots` / `procurement_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.arrival_measurement_sets` / `arrival_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.arrival_measurements` / `arrival_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.arrival_operations` / `arrival_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.arrival_reviews` / `arrival_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.lots` / `receipt_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.receipt_reversals` / `receipt_correction_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.receiving` / `arrival_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`
- `ledger.receiving_blocks` / `receiving_resolution_package`: `TRIGGER DEFERRABLE INITIALLY DEFERRED`

### Foreign keys (120)

- `capture.business_events` / `fk_events_actor`: `FOREIGN KEY (actor_id) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.business_events` / `fk_events_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.crop_seasons` / `fk_crop_seasons_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.crop_seasons` / `fk_crop_seasons_created_by`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.farmers` / `fk_farmers_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.farmers` / `fk_farmers_created_by`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.offline_operations` / `fk_operations_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.offline_operations` / `fk_operations_event`: `FOREIGN KEY (applied_event_id) REFERENCES capture.business_events(id) ON DELETE RESTRICT`
- `capture.offline_operations` / `fk_operations_user`: `FOREIGN KEY (submitted_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.plot_seasons` / `fk_plot_seasons_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.plot_seasons` / `fk_plot_seasons_company_plot`: `FOREIGN KEY (company_id, plot_id) REFERENCES capture.plots(company_id, id) ON DELETE RESTRICT`
- `capture.plot_seasons` / `fk_plot_seasons_company_season`: `FOREIGN KEY (company_id, season_id) REFERENCES capture.crop_seasons(company_id, id) ON DELETE RESTRICT`
- `capture.plot_seasons` / `fk_plot_seasons_created_by`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.plots` / `fk_plots_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.plots` / `fk_plots_company_farmer`: `FOREIGN KEY (company_id, farmer_id) REFERENCES capture.farmers(company_id, id) ON DELETE RESTRICT`
- `capture.plots` / `fk_plots_created_by`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.procurement_items` / `fk_procurement_items_company_plot_season`: `FOREIGN KEY (company_id, plot_season_id) REFERENCES capture.plot_seasons(company_id, id) ON DELETE RESTRICT`
- `capture.procurement_items` / `fk_procurement_items_company_procurement`: `FOREIGN KEY (company_id, procurement_id) REFERENCES capture.procurements(company_id, id) ON DELETE RESTRICT`
- `capture.procurements` / `fk_procurements_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.procurements` / `fk_procurements_company_farmer`: `FOREIGN KEY (company_id, farmer_id) REFERENCES capture.farmers(company_id, id) ON DELETE RESTRICT`
- `capture.procurements` / `fk_procurements_event`: `FOREIGN KEY (event_id) REFERENCES capture.business_events(id) ON DELETE RESTRICT`
- `capture.procurements` / `fk_procurements_received_by`: `FOREIGN KEY (received_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.sample_handovers` / `fk_sample_handovers_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.sample_handovers` / `fk_sample_handovers_recorded_by`: `FOREIGN KEY (recorded_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.sample_handovers` / `fk_sample_handovers_sample`: `FOREIGN KEY (company_id, sample_id) REFERENCES capture.samples(company_id, id) ON DELETE RESTRICT`
- `capture.samples` / `fk_samples_collected_by`: `FOREIGN KEY (collected_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.samples` / `fk_samples_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.samples` / `fk_samples_source_lot`: `FOREIGN KEY (company_id, source_lot_id) REFERENCES capture.source_lots(company_id, id) ON DELETE RESTRICT`
- `capture.source_lots` / `fk_source_lots_company_item`: `FOREIGN KEY (company_id, procurement_item_id) REFERENCES capture.procurement_items(company_id, id) ON DELETE RESTRICT`
- `capture.spray_declarations` / `fk_spray_declarations_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.spray_declarations` / `fk_spray_declarations_company_plot_season`: `FOREIGN KEY (company_id, plot_season_id) REFERENCES capture.plot_seasons(company_id, id) ON DELETE RESTRICT`
- `capture.spray_declarations` / `fk_spray_declarations_recorded_by`: `FOREIGN KEY (recorded_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `capture.spray_items` / `fk_spray_items_company`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `capture.spray_items` / `fk_spray_items_company_declaration`: `FOREIGN KEY (company_id, declaration_id) REFERENCES capture.spray_declarations(company_id, id) ON DELETE RESTRICT`
- `capture.spray_items` / `fk_spray_items_substance`: `FOREIGN KEY (substance_id) REFERENCES catalog.active_substances(id) ON DELETE RESTRICT`
- `catalog.catalog_events` / `fk_catalog_events_actor`: `FOREIGN KEY (actor_id) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `catalog.mrl_limits` / `FK_mrl_limits_active_substances_substance_id`: `FOREIGN KEY (substance_id) REFERENCES catalog.active_substances(id) ON DELETE RESTRICT`
- `catalog.mrl_limits` / `FK_mrl_limits_mrl_versions_mrl_version_id`: `FOREIGN KEY (mrl_version_id) REFERENCES catalog.mrl_versions(id) ON DELETE RESTRICT`
- `catalog.mrl_versions` / `FK_mrl_versions_commodities_commodity_id`: `FOREIGN KEY (commodity_id) REFERENCES catalog.commodities(id) ON DELETE RESTRICT`
- `catalog.mrl_versions` / `FK_mrl_versions_markets_market_id`: `FOREIGN KEY (market_id) REFERENCES catalog.markets(id) ON DELETE RESTRICT`
- `catalog.mrl_versions` / `fk_mrl_versions_published_by`: `FOREIGN KEY (published_by) REFERENCES identity.users("Id") ON DELETE RESTRICT`
- `identity.password_reset_codes` / `FK_password_reset_codes_users_UserId`: `FOREIGN KEY ("UserId") REFERENCES identity.users("Id") ON DELETE CASCADE`
- `identity.sessions` / `FK_sessions_users_UserId`: `FOREIGN KEY ("UserId") REFERENCES identity.users("Id") ON DELETE CASCADE`
- `identity.users` / `FK_users_organizations_OrganizationId`: `FOREIGN KEY ("OrganizationId") REFERENCES identity.organizations("Id") ON DELETE RESTRICT`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_operator_id_fkey`: `FOREIGN KEY (operator_id) REFERENCES identity.users("Id")`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_procurement_id_fkey`: `FOREIGN KEY (procurement_id) REFERENCES ledger.receiving(procurement_id)`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_threshold_company_id_fkey`: `FOREIGN KEY (threshold_company_id) REFERENCES identity.organizations("Id")`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_threshold_revision_fkey`: `FOREIGN KEY (threshold_revision) REFERENCES ledger.receiving_threshold_history(revision)`
- `ledger.arrival_measurements` / `arrival_measurements_measurement_set_id_fkey`: `FOREIGN KEY (measurement_set_id) REFERENCES ledger.arrival_measurement_sets(id)`
- `ledger.arrival_measurements` / `arrival_measurements_procurement_item_id_fkey`: `FOREIGN KEY (procurement_item_id) REFERENCES capture.procurement_items(id)`
- `ledger.arrival_operations` / `arrival_operations_actor_id_fkey`: `FOREIGN KEY (actor_id) REFERENCES identity.users("Id")`
- `ledger.arrival_operations` / `arrival_operations_procurement_id_fkey`: `FOREIGN KEY (procurement_id) REFERENCES ledger.receiving(procurement_id)`
- `ledger.arrival_reviews` / `arrival_reviews_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.arrival_reviews` / `arrival_reviews_manager_id_fkey`: `FOREIGN KEY (manager_id) REFERENCES identity.users("Id")`
- `ledger.arrival_reviews` / `arrival_reviews_procurement_id_fkey`: `FOREIGN KEY (procurement_id) REFERENCES ledger.receiving(procurement_id)`
- `ledger.arrival_reviews` / `arrival_reviews_procurement_id_measurement_set_id_fkey`: `FOREIGN KEY (procurement_id, measurement_set_id) REFERENCES ledger.arrival_measurement_sets(procurement_id, id)`
- `ledger.assignments` / `assignments_created_by_fkey`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id")`
- `ledger.assignments` / `assignments_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.assignments` / `assignments_user_id_fkey`: `FOREIGN KEY (user_id) REFERENCES identity.users("Id")`
- `ledger.catalog_commodities` / `catalog_commodities_catalog_commodity_id_fkey`: `FOREIGN KEY (catalog_commodity_id) REFERENCES catalog.commodities(id)`
- `ledger.catalog_commodities` / `catalog_commodities_ledger_commodity_id_fkey`: `FOREIGN KEY (ledger_commodity_id) REFERENCES ledger.commodities(id)`
- `ledger.compatibilities` / `compatibilities_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.compatibilities` / `compatibilities_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.compatibilities` / `compatibilities_output_commodity_id_fkey`: `FOREIGN KEY (output_commodity_id) REFERENCES ledger.commodities(id)`
- `ledger.contributions` / `contributions_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.contributions` / `contributions_source_lot_id_fkey`: `FOREIGN KEY (source_lot_id) REFERENCES ledger.lots(id)`
- `ledger.corrections` / `corrections_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.corrections` / `corrections_operation_id_fkey`: `FOREIGN KEY (operation_id) REFERENCES ledger.operations(id)`
- `ledger.corrections` / `corrections_replacement_operation_id_fkey`: `FOREIGN KEY (replacement_operation_id) REFERENCES ledger.operations(id)`
- `ledger.equipment` / `equipment_created_by_fkey`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id")`
- `ledger.equipment` / `equipment_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.events` / `events_actor_id_fkey`: `FOREIGN KEY (actor_id) REFERENCES identity.users("Id")`
- `ledger.events` / `events_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.events` / `events_operation_id_fkey`: `FOREIGN KEY (operation_id) REFERENCES ledger.operations(id)`
- `ledger.evidence` / `evidence_actor_id_fkey`: `FOREIGN KEY (actor_id) REFERENCES identity.users("Id")`
- `ledger.evidence` / `evidence_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.evidence` / `evidence_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.facilities` / `facilities_created_by_fkey`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id")`
- `ledger.holds` / `holds_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.holds` / `holds_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.locations` / `locations_created_by_fkey`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id")`
- `ledger.locations` / `locations_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.locations` / `locations_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.lot_code_reservations` / `lot_code_reservations_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.lot_codes` / `lot_codes_origin_lot_id_fkey`: `FOREIGN KEY (origin_lot_id) REFERENCES ledger.lots(id) DEFERRABLE INITIALLY DEFERRED`
- `ledger.lots` / `lot_code_lineage`: `FOREIGN KEY (code, code_origin_id) REFERENCES ledger.lot_codes(code, origin_lot_id)`
- `ledger.lots` / `lots_commodity_id_fkey`: `FOREIGN KEY (commodity_id) REFERENCES ledger.commodities(id)`
- `ledger.lots` / `lots_operation_id_fkey`: `FOREIGN KEY (operation_id) REFERENCES ledger.operations(id)`
- `ledger.lots` / `lots_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.notifications` / `notifications_hold_id_fkey`: `FOREIGN KEY (hold_id) REFERENCES ledger.holds(id)`
- `ledger.notifications` / `notifications_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.notifications` / `notifications_user_id_fkey`: `FOREIGN KEY (user_id) REFERENCES identity.users("Id")`
- `ledger.operations` / `operations_confirmation_evidence_id_fkey`: `FOREIGN KEY (confirmation_evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.operations` / `operations_confirmed_by_fkey`: `FOREIGN KEY (confirmed_by) REFERENCES identity.users("Id")`
- `ledger.operations` / `operations_created_by_fkey`: `FOREIGN KEY (created_by) REFERENCES identity.users("Id")`
- `ledger.operations` / `operations_equipment_id_fkey`: `FOREIGN KEY (equipment_id) REFERENCES ledger.equipment(id)`
- `ledger.operations` / `operations_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.operations` / `operations_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.policies` / `policies_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.policies` / `policies_facility_id_fkey`: `FOREIGN KEY (facility_id) REFERENCES ledger.facilities(id)`
- `ledger.positions` / `positions_location_id_fkey`: `FOREIGN KEY (location_id) REFERENCES ledger.locations(id)`
- `ledger.positions` / `positions_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.postings` / `postings_event_id_fkey`: `FOREIGN KEY (event_id) REFERENCES ledger.events(id)`
- `ledger.postings` / `postings_location_id_fkey`: `FOREIGN KEY (location_id) REFERENCES ledger.locations(id)`
- `ledger.postings` / `postings_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.postings` / `postings_reverses_posting_id_fkey`: `FOREIGN KEY (reverses_posting_id) REFERENCES ledger.postings(id)`
- `ledger.receipt_correction_blocks` / `receipt_correction_blocks_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.receipt_reversals` / `receipt_reversals_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.receipt_reversals` / `receipt_reversals_owner_id_fkey`: `FOREIGN KEY (owner_id) REFERENCES identity.organizations("Id")`
- `ledger.receiving` / `receiving_procurement_id_accepted_measurement_set_id_fkey`: `FOREIGN KEY (procurement_id, accepted_measurement_set_id) REFERENCES ledger.arrival_measurement_sets(procurement_id, id) DEFERRABLE INITIALLY DEFERRED`
- `ledger.receiving` / `receiving_procurement_id_accepted_review_id_fkey`: `FOREIGN KEY (procurement_id, accepted_review_id) REFERENCES ledger.arrival_reviews(procurement_id, id) DEFERRABLE INITIALLY DEFERRED`
- `ledger.receiving` / `receiving_procurement_id_fkey`: `FOREIGN KEY (procurement_id) REFERENCES capture.procurements(id)`
- `ledger.receiving` / `receiving_procurement_id_latest_measurement_set_id_fkey`: `FOREIGN KEY (procurement_id, latest_measurement_set_id) REFERENCES ledger.arrival_measurement_sets(procurement_id, id) DEFERRABLE INITIALLY DEFERRED`
- `ledger.receiving_blocks` / `receiving_blocks_evidence_id_fkey`: `FOREIGN KEY (evidence_id) REFERENCES ledger.evidence(id)`
- `ledger.receiving_blocks` / `receiving_blocks_lot_id_fkey`: `FOREIGN KEY (lot_id) REFERENCES ledger.lots(id)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_revision_fkey`: `FOREIGN KEY (revision) REFERENCES ledger.receiving_threshold_history(revision)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_actor_id_fkey`: `FOREIGN KEY (actor_id) REFERENCES identity.users("Id")`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_company_id_fkey`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id")`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_company_id_fkey`: `FOREIGN KEY (company_id) REFERENCES identity.organizations("Id")`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_revision_fkey`: `FOREIGN KEY (revision) REFERENCES ledger.receiving_threshold_history(revision)`

## Indexes (154)

- `capture.business_events` / `IX_business_events_actor_id`: `CREATE INDEX "IX_business_events_actor_id" ON capture.business_events USING btree (actor_id)`
- `capture.business_events` / `IX_business_events_company_id`: `CREATE INDEX "IX_business_events_company_id" ON capture.business_events USING btree (company_id)`
- `capture.business_events` / `PK_business_events`: `CREATE UNIQUE INDEX "PK_business_events" ON capture.business_events USING btree (id)`
- `capture.code_counters` / `pk_code_counters`: `CREATE UNIQUE INDEX pk_code_counters ON capture.code_counters USING btree (company_id, kind, scope)`
- `capture.crop_seasons` / `PK_crop_seasons`: `CREATE UNIQUE INDEX "PK_crop_seasons" ON capture.crop_seasons USING btree (id)`
- `capture.crop_seasons` / `ux_crop_seasons_company_id`: `CREATE UNIQUE INDEX ux_crop_seasons_company_id ON capture.crop_seasons USING btree (company_id, id)`
- `capture.farmers` / `IX_farmers_company_id_created_at_id`: `CREATE INDEX "IX_farmers_company_id_created_at_id" ON capture.farmers USING btree (company_id, created_at, id)`
- `capture.farmers` / `PK_farmers`: `CREATE UNIQUE INDEX "PK_farmers" ON capture.farmers USING btree (id)`
- `capture.farmers` / `ux_farmers_company_code`: `CREATE UNIQUE INDEX ux_farmers_company_code ON capture.farmers USING btree (company_id, code)`
- `capture.farmers` / `ux_farmers_company_code_number`: `CREATE UNIQUE INDEX ux_farmers_company_code_number ON capture.farmers USING btree (company_id, code_number)`
- `capture.farmers` / `ux_farmers_company_id`: `CREATE UNIQUE INDEX ux_farmers_company_id ON capture.farmers USING btree (company_id, id)`
- `capture.offline_operations` / `IX_offline_operations_applied_event_id`: `CREATE UNIQUE INDEX "IX_offline_operations_applied_event_id" ON capture.offline_operations USING btree (applied_event_id)`
- `capture.offline_operations` / `IX_offline_operations_company_id_id`: `CREATE UNIQUE INDEX "IX_offline_operations_company_id_id" ON capture.offline_operations USING btree (company_id, id)`
- `capture.offline_operations` / `IX_offline_operations_submitted_by`: `CREATE INDEX "IX_offline_operations_submitted_by" ON capture.offline_operations USING btree (submitted_by)`
- `capture.offline_operations` / `PK_offline_operations`: `CREATE UNIQUE INDEX "PK_offline_operations" ON capture.offline_operations USING btree (id)`
- `capture.offline_operations` / `ux_offline_operations_company_device_operation`: `CREATE UNIQUE INDEX ux_offline_operations_company_device_operation ON capture.offline_operations USING btree (company_id, device_id, client_operation_id)`
- `capture.plot_seasons` / `IX_plot_seasons_season_id`: `CREATE INDEX "IX_plot_seasons_season_id" ON capture.plot_seasons USING btree (season_id)`
- `capture.plot_seasons` / `PK_plot_seasons`: `CREATE UNIQUE INDEX "PK_plot_seasons" ON capture.plot_seasons USING btree (id)`
- `capture.plot_seasons` / `ux_plot_seasons_company_id`: `CREATE UNIQUE INDEX ux_plot_seasons_company_id ON capture.plot_seasons USING btree (company_id, id)`
- `capture.plot_seasons` / `ux_plot_seasons_plot_season`: `CREATE UNIQUE INDEX ux_plot_seasons_plot_season ON capture.plot_seasons USING btree (plot_id, season_id)`
- `capture.plots` / `PK_plots`: `CREATE UNIQUE INDEX "PK_plots" ON capture.plots USING btree (id)`
- `capture.plots` / `ux_plots_company_id`: `CREATE UNIQUE INDEX ux_plots_company_id ON capture.plots USING btree (company_id, id)`
- `capture.plots` / `ux_plots_farmer_code`: `CREATE UNIQUE INDEX ux_plots_farmer_code ON capture.plots USING btree (farmer_id, code)`
- `capture.procurement_items` / `IX_procurement_items_plot_season_id`: `CREATE INDEX "IX_procurement_items_plot_season_id" ON capture.procurement_items USING btree (plot_season_id)`
- `capture.procurement_items` / `PK_procurement_items`: `CREATE UNIQUE INDEX "PK_procurement_items" ON capture.procurement_items USING btree (id)`
- `capture.procurement_items` / `ux_procurement_items_company_id`: `CREATE UNIQUE INDEX ux_procurement_items_company_id ON capture.procurement_items USING btree (company_id, id)`
- `capture.procurement_items` / `ux_procurement_items_receipt_line`: `CREATE UNIQUE INDEX ux_procurement_items_receipt_line ON capture.procurement_items USING btree (procurement_id, line_no)`
- `capture.procurements` / `IX_procurements_company_id_received_at_id`: `CREATE INDEX "IX_procurements_company_id_received_at_id" ON capture.procurements USING btree (company_id, received_at, id)`
- `capture.procurements` / `IX_procurements_event_id`: `CREATE UNIQUE INDEX "IX_procurements_event_id" ON capture.procurements USING btree (event_id)`
- `capture.procurements` / `PK_procurements`: `CREATE UNIQUE INDEX "PK_procurements" ON capture.procurements USING btree (id)`
- `capture.procurements` / `receiving_facility_queue`: `CREATE INDEX receiving_facility_queue ON capture.procurements USING btree (collection_site_id, received_at DESC, id) WHERE (receiving_workflow IS NOT NULL)`
- `capture.procurements` / `ux_procurements_company_code`: `CREATE UNIQUE INDEX ux_procurements_company_code ON capture.procurements USING btree (company_id, code)`
- `capture.procurements` / `ux_procurements_company_id`: `CREATE UNIQUE INDEX ux_procurements_company_id ON capture.procurements USING btree (company_id, id)`
- `capture.sample_handovers` / `IX_sample_handovers_recorded_by`: `CREATE INDEX "IX_sample_handovers_recorded_by" ON capture.sample_handovers USING btree (recorded_by)`
- `capture.sample_handovers` / `IX_sample_handovers_sample_id`: `CREATE INDEX "IX_sample_handovers_sample_id" ON capture.sample_handovers USING btree (sample_id)`
- `capture.sample_handovers` / `PK_sample_handovers`: `CREATE UNIQUE INDEX "PK_sample_handovers" ON capture.sample_handovers USING btree (id)`
- `capture.sample_handovers` / `ux_sample_handovers_company_id`: `CREATE UNIQUE INDEX ux_sample_handovers_company_id ON capture.sample_handovers USING btree (company_id, id)`
- `capture.samples` / `IX_samples_collected_by`: `CREATE INDEX "IX_samples_collected_by" ON capture.samples USING btree (collected_by)`
- `capture.samples` / `IX_samples_source_lot_id`: `CREATE INDEX "IX_samples_source_lot_id" ON capture.samples USING btree (source_lot_id)`
- `capture.samples` / `PK_samples`: `CREATE UNIQUE INDEX "PK_samples" ON capture.samples USING btree (id)`
- `capture.samples` / `ux_samples_company_code`: `CREATE UNIQUE INDEX ux_samples_company_code ON capture.samples USING btree (company_id, code)`
- `capture.samples` / `ux_samples_company_id`: `CREATE UNIQUE INDEX ux_samples_company_id ON capture.samples USING btree (company_id, id)`
- `capture.source_lots` / `IX_source_lots_lot_id`: `CREATE UNIQUE INDEX "IX_source_lots_lot_id" ON capture.source_lots USING btree (lot_id)`
- `capture.source_lots` / `IX_source_lots_procurement_item_id`: `CREATE UNIQUE INDEX "IX_source_lots_procurement_item_id" ON capture.source_lots USING btree (procurement_item_id)`
- `capture.source_lots` / `PK_source_lots`: `CREATE UNIQUE INDEX "PK_source_lots" ON capture.source_lots USING btree (id)`
- `capture.source_lots` / `ux_source_lots_company_id`: `CREATE UNIQUE INDEX ux_source_lots_company_id ON capture.source_lots USING btree (company_id, id)`
- `capture.spray_declarations` / `IX_spray_declarations_plot_season_id`: `CREATE INDEX "IX_spray_declarations_plot_season_id" ON capture.spray_declarations USING btree (plot_season_id)`
- `capture.spray_declarations` / `IX_spray_declarations_recorded_by`: `CREATE INDEX "IX_spray_declarations_recorded_by" ON capture.spray_declarations USING btree (recorded_by)`
- `capture.spray_declarations` / `PK_spray_declarations`: `CREATE UNIQUE INDEX "PK_spray_declarations" ON capture.spray_declarations USING btree (id)`
- `capture.spray_declarations` / `ux_spray_declarations_company_id`: `CREATE UNIQUE INDEX ux_spray_declarations_company_id ON capture.spray_declarations USING btree (company_id, id)`
- `capture.spray_items` / `IX_spray_items_declaration_id`: `CREATE INDEX "IX_spray_items_declaration_id" ON capture.spray_items USING btree (declaration_id)`
- `capture.spray_items` / `IX_spray_items_substance_id`: `CREATE INDEX "IX_spray_items_substance_id" ON capture.spray_items USING btree (substance_id)`
- `capture.spray_items` / `PK_spray_items`: `CREATE UNIQUE INDEX "PK_spray_items" ON capture.spray_items USING btree (id)`
- `capture.spray_items` / `ux_spray_items_company_id`: `CREATE UNIQUE INDEX ux_spray_items_company_id ON capture.spray_items USING btree (company_id, id)`
- `catalog.active_substances` / `IX_active_substances_code`: `CREATE UNIQUE INDEX "IX_active_substances_code" ON catalog.active_substances USING btree (code)`
- `catalog.active_substances` / `PK_active_substances`: `CREATE UNIQUE INDEX "PK_active_substances" ON catalog.active_substances USING btree (id)`
- `catalog.catalog_events` / `IX_catalog_events_actor_id`: `CREATE INDEX "IX_catalog_events_actor_id" ON catalog.catalog_events USING btree (actor_id)`
- `catalog.catalog_events` / `IX_catalog_events_correlation_id`: `CREATE INDEX "IX_catalog_events_correlation_id" ON catalog.catalog_events USING btree (correlation_id)`
- `catalog.catalog_events` / `IX_catalog_events_resource_type_resource_id_occurred_at`: `CREATE INDEX "IX_catalog_events_resource_type_resource_id_occurred_at" ON catalog.catalog_events USING btree (resource_type, resource_id, occurred_at)`
- `catalog.catalog_events` / `PK_catalog_events`: `CREATE UNIQUE INDEX "PK_catalog_events" ON catalog.catalog_events USING btree (id)`
- `catalog.commodities` / `IX_commodities_code`: `CREATE UNIQUE INDEX "IX_commodities_code" ON catalog.commodities USING btree (code)`
- `catalog.commodities` / `PK_commodities`: `CREATE UNIQUE INDEX "PK_commodities" ON catalog.commodities USING btree (id)`
- `catalog.markets` / `IX_markets_code`: `CREATE UNIQUE INDEX "IX_markets_code" ON catalog.markets USING btree (code)`
- `catalog.markets` / `PK_markets`: `CREATE UNIQUE INDEX "PK_markets" ON catalog.markets USING btree (id)`
- `catalog.measurement_units` / `IX_measurement_units_code`: `CREATE UNIQUE INDEX "IX_measurement_units_code" ON catalog.measurement_units USING btree (code)`
- `catalog.measurement_units` / `PK_measurement_units`: `CREATE UNIQUE INDEX "PK_measurement_units" ON catalog.measurement_units USING btree (id)`
- `catalog.mrl_limits` / `IX_mrl_limits_mrl_version_id_substance_id`: `CREATE UNIQUE INDEX "IX_mrl_limits_mrl_version_id_substance_id" ON catalog.mrl_limits USING btree (mrl_version_id, substance_id)`
- `catalog.mrl_limits` / `IX_mrl_limits_substance_id`: `CREATE INDEX "IX_mrl_limits_substance_id" ON catalog.mrl_limits USING btree (substance_id)`
- `catalog.mrl_limits` / `PK_mrl_limits`: `CREATE UNIQUE INDEX "PK_mrl_limits" ON catalog.mrl_limits USING btree (id)`
- `catalog.mrl_versions` / `IX_mrl_versions_commodity_id`: `CREATE INDEX "IX_mrl_versions_commodity_id" ON catalog.mrl_versions USING btree (commodity_id)`
- `catalog.mrl_versions` / `IX_mrl_versions_market_id_commodity_id_version_no`: `CREATE UNIQUE INDEX "IX_mrl_versions_market_id_commodity_id_version_no" ON catalog.mrl_versions USING btree (market_id, commodity_id, version_no)`
- `catalog.mrl_versions` / `IX_mrl_versions_published_by`: `CREATE INDEX "IX_mrl_versions_published_by" ON catalog.mrl_versions USING btree (published_by)`
- `catalog.mrl_versions` / `PK_mrl_versions`: `CREATE UNIQUE INDEX "PK_mrl_versions" ON catalog.mrl_versions USING btree (id)`
- `catalog.quality_attributes` / `IX_quality_attributes_code`: `CREATE UNIQUE INDEX "IX_quality_attributes_code" ON catalog.quality_attributes USING btree (code)`
- `catalog.quality_attributes` / `PK_quality_attributes`: `CREATE UNIQUE INDEX "PK_quality_attributes" ON catalog.quality_attributes USING btree (id)`
- `catalog.rice_varieties` / `IX_rice_varieties_code`: `CREATE UNIQUE INDEX "IX_rice_varieties_code" ON catalog.rice_varieties USING btree (code)`
- `catalog.rice_varieties` / `PK_rice_varieties`: `CREATE UNIQUE INDEX "PK_rice_varieties" ON catalog.rice_varieties USING btree (id)`
- `identity.audit_events` / `IX_audit_events_OccurredAt`: `CREATE INDEX "IX_audit_events_OccurredAt" ON identity.audit_events USING btree ("OccurredAt")`
- `identity.audit_events` / `PK_audit_events`: `CREATE UNIQUE INDEX "PK_audit_events" ON identity.audit_events USING btree ("Id")`
- `identity.organizations` / `IX_organizations_Kind`: `CREATE UNIQUE INDEX "IX_organizations_Kind" ON identity.organizations USING btree ("Kind") WHERE (("Kind")::text = 'Mill'::text)`
- `identity.organizations` / `IX_organizations_Name`: `CREATE UNIQUE INDEX "IX_organizations_Name" ON identity.organizations USING btree ("Name")`
- `identity.organizations` / `PK_organizations`: `CREATE UNIQUE INDEX "PK_organizations" ON identity.organizations USING btree ("Id")`
- `identity.password_reset_codes` / `IX_password_reset_codes_UserId_ConsumedAt`: `CREATE INDEX "IX_password_reset_codes_UserId_ConsumedAt" ON identity.password_reset_codes USING btree ("UserId", "ConsumedAt")`
- `identity.password_reset_codes` / `PK_password_reset_codes`: `CREATE UNIQUE INDEX "PK_password_reset_codes" ON identity.password_reset_codes USING btree ("Id")`
- `identity.registration_attempts` / `IX_registration_attempts_NormalizedEmail_InvalidatedAt`: `CREATE INDEX "IX_registration_attempts_NormalizedEmail_InvalidatedAt" ON identity.registration_attempts USING btree ("NormalizedEmail", "InvalidatedAt")`
- `identity.registration_attempts` / `PK_registration_attempts`: `CREATE UNIQUE INDEX "PK_registration_attempts" ON identity.registration_attempts USING btree ("Id")`
- `identity.sessions` / `IX_sessions_FamilyId`: `CREATE INDEX "IX_sessions_FamilyId" ON identity.sessions USING btree ("FamilyId")`
- `identity.sessions` / `IX_sessions_RefreshTokenHash`: `CREATE UNIQUE INDEX "IX_sessions_RefreshTokenHash" ON identity.sessions USING btree ("RefreshTokenHash") WHERE ("RefreshTokenHash" IS NOT NULL)`
- `identity.sessions` / `IX_sessions_UserId_RevokedAt`: `CREATE INDEX "IX_sessions_UserId_RevokedAt" ON identity.sessions USING btree ("UserId", "RevokedAt")`
- `identity.sessions` / `PK_sessions`: `CREATE UNIQUE INDEX "PK_sessions" ON identity.sessions USING btree ("Id")`
- `identity.users` / `IX_users_Email`: `CREATE INDEX "IX_users_Email" ON identity.users USING btree ("Email")`
- `identity.users` / `IX_users_NormalizedEmail`: `CREATE UNIQUE INDEX "IX_users_NormalizedEmail" ON identity.users USING btree ("NormalizedEmail") WHERE (("Provider")::text = 'local'::text)`
- `identity.users` / `IX_users_OrganizationId`: `CREATE INDEX "IX_users_OrganizationId" ON identity.users USING btree ("OrganizationId")`
- `identity.users` / `IX_users_Provider_ProviderSubject`: `CREATE UNIQUE INDEX "IX_users_Provider_ProviderSubject" ON identity.users USING btree ("Provider", "ProviderSubject")`
- `identity.users` / `PK_users`: `CREATE UNIQUE INDEX "PK_users" ON identity.users USING btree ("Id")`
- `ledger.__EFMigrationsHistory` / `PK___EFMigrationsHistory`: `CREATE UNIQUE INDEX "PK___EFMigrationsHistory" ON ledger."__EFMigrationsHistory" USING btree ("MigrationId")`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_operation_id_key`: `CREATE UNIQUE INDEX arrival_measurement_sets_operation_id_key ON ledger.arrival_measurement_sets USING btree (operation_id)`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_pkey`: `CREATE UNIQUE INDEX arrival_measurement_sets_pkey ON ledger.arrival_measurement_sets USING btree (id)`
- `ledger.arrival_measurement_sets` / `arrival_measurement_sets_procurement_id_id_key`: `CREATE UNIQUE INDEX arrival_measurement_sets_procurement_id_id_key ON ledger.arrival_measurement_sets USING btree (procurement_id, id)`
- `ledger.arrival_measurements` / `arrival_measurements_pkey`: `CREATE UNIQUE INDEX arrival_measurements_pkey ON ledger.arrival_measurements USING btree (measurement_set_id, procurement_item_id)`
- `ledger.arrival_operations` / `arrival_operations_pkey`: `CREATE UNIQUE INDEX arrival_operations_pkey ON ledger.arrival_operations USING btree (operation_id)`
- `ledger.arrival_reviews` / `arrival_reviews_operation_id_key`: `CREATE UNIQUE INDEX arrival_reviews_operation_id_key ON ledger.arrival_reviews USING btree (operation_id)`
- `ledger.arrival_reviews` / `arrival_reviews_pkey`: `CREATE UNIQUE INDEX arrival_reviews_pkey ON ledger.arrival_reviews USING btree (id)`
- `ledger.arrival_reviews` / `arrival_reviews_procurement_id_id_key`: `CREATE UNIQUE INDEX arrival_reviews_procurement_id_id_key ON ledger.arrival_reviews USING btree (procurement_id, id)`
- `ledger.assignments` / `assignments_pkey`: `CREATE UNIQUE INDEX assignments_pkey ON ledger.assignments USING btree (user_id, facility_id)`
- `ledger.catalog_commodities` / `catalog_commodities_ledger_commodity_id_key`: `CREATE UNIQUE INDEX catalog_commodities_ledger_commodity_id_key ON ledger.catalog_commodities USING btree (ledger_commodity_id)`
- `ledger.catalog_commodities` / `catalog_commodities_pkey`: `CREATE UNIQUE INDEX catalog_commodities_pkey ON ledger.catalog_commodities USING btree (catalog_commodity_id)`
- `ledger.commodities` / `commodities_pkey`: `CREATE UNIQUE INDEX commodities_pkey ON ledger.commodities USING btree (id)`
- `ledger.compatibilities` / `compatibilities_pkey`: `CREATE UNIQUE INDEX compatibilities_pkey ON ledger.compatibilities USING btree (id)`
- `ledger.contributions` / `contributions_pkey`: `CREATE UNIQUE INDEX contributions_pkey ON ledger.contributions USING btree (lot_id, source_lot_id)`
- `ledger.contributions` / `contributions_source_lot_lot_idx`: `CREATE INDEX contributions_source_lot_lot_idx ON ledger.contributions USING btree (source_lot_id, lot_id)`
- `ledger.corrections` / `corrections_operation_id_key`: `CREATE UNIQUE INDEX corrections_operation_id_key ON ledger.corrections USING btree (operation_id)`
- `ledger.corrections` / `corrections_pkey`: `CREATE UNIQUE INDEX corrections_pkey ON ledger.corrections USING btree (id)`
- `ledger.corrections` / `corrections_replacement_operation_id_key`: `CREATE UNIQUE INDEX corrections_replacement_operation_id_key ON ledger.corrections USING btree (replacement_operation_id)`
- `ledger.equipment` / `equipment_pkey`: `CREATE UNIQUE INDEX equipment_pkey ON ledger.equipment USING btree (id)`
- `ledger.events` / `events_lot_idx`: `CREATE INDEX events_lot_idx ON ledger.events USING btree (lot_id)`
- `ledger.events` / `events_operation_idx`: `CREATE INDEX events_operation_idx ON ledger.events USING btree (operation_id)`
- `ledger.events` / `events_pkey`: `CREATE UNIQUE INDEX events_pkey ON ledger.events USING btree (id)`
- `ledger.evidence` / `evidence_pkey`: `CREATE UNIQUE INDEX evidence_pkey ON ledger.evidence USING btree (id)`
- `ledger.facilities` / `facilities_pkey`: `CREATE UNIQUE INDEX facilities_pkey ON ledger.facilities USING btree (id)`
- `ledger.holds` / `holds_lot_idx`: `CREATE INDEX holds_lot_idx ON ledger.holds USING btree (lot_id)`
- `ledger.holds` / `holds_pkey`: `CREATE UNIQUE INDEX holds_pkey ON ledger.holds USING btree (id)`
- `ledger.locations` / `locations_pkey`: `CREATE UNIQUE INDEX locations_pkey ON ledger.locations USING btree (id)`
- `ledger.lot_code_reservations` / `lot_code_reservations_code_key`: `CREATE UNIQUE INDEX lot_code_reservations_code_key ON ledger.lot_code_reservations USING btree (code)`
- `ledger.lot_code_reservations` / `lot_code_reservations_context`: `CREATE INDEX lot_code_reservations_context ON ledger.lot_code_reservations USING btree (context_kind, context_id)`
- `ledger.lot_code_reservations` / `lot_code_reservations_pkey`: `CREATE UNIQUE INDEX lot_code_reservations_pkey ON ledger.lot_code_reservations USING btree (lot_id)`
- `ledger.lot_codes` / `lot_codes_code_origin_lot_id_key`: `CREATE UNIQUE INDEX lot_codes_code_origin_lot_id_key ON ledger.lot_codes USING btree (code, origin_lot_id)`
- `ledger.lot_codes` / `lot_codes_pkey`: `CREATE UNIQUE INDEX lot_codes_pkey ON ledger.lot_codes USING btree (code)`
- `ledger.lots` / `lots_owner_id_id`: `CREATE UNIQUE INDEX lots_owner_id_id ON ledger.lots USING btree (owner_id, id)`
- `ledger.lots` / `lots_pkey`: `CREATE UNIQUE INDEX lots_pkey ON ledger.lots USING btree (id)`
- `ledger.lots` / `lots_receipt_line_id_key`: `CREATE UNIQUE INDEX lots_receipt_line_id_key ON ledger.lots USING btree (receipt_line_id)`
- `ledger.notifications` / `notifications_pkey`: `CREATE UNIQUE INDEX notifications_pkey ON ledger.notifications USING btree (id)`
- `ledger.operations` / `operations_pkey`: `CREATE UNIQUE INDEX operations_pkey ON ledger.operations USING btree (id)`
- `ledger.policies` / `policies_facility_id_kind_effective_from_key`: `CREATE UNIQUE INDEX policies_facility_id_kind_effective_from_key ON ledger.policies USING btree (facility_id, kind, effective_from)`
- `ledger.policies` / `policies_pkey`: `CREATE UNIQUE INDEX policies_pkey ON ledger.policies USING btree (id)`
- `ledger.positions` / `positions_pkey`: `CREATE UNIQUE INDEX positions_pkey ON ledger.positions USING btree (lot_id, location_id)`
- `ledger.postings` / `postings_event_idx`: `CREATE INDEX postings_event_idx ON ledger.postings USING btree (event_id)`
- `ledger.postings` / `postings_lot_event_idx`: `CREATE INDEX postings_lot_event_idx ON ledger.postings USING btree (lot_id, event_id)`
- `ledger.postings` / `postings_pkey`: `CREATE UNIQUE INDEX postings_pkey ON ledger.postings USING btree (id)`
- `ledger.postings` / `postings_reverses_posting_id_key`: `CREATE UNIQUE INDEX postings_reverses_posting_id_key ON ledger.postings USING btree (reverses_posting_id)`
- `ledger.receipt_correction_blocks` / `receipt_correction_blocks_pkey`: `CREATE UNIQUE INDEX receipt_correction_blocks_pkey ON ledger.receipt_correction_blocks USING btree (correction_id)`
- `ledger.receipt_correction_blocks` / `receipt_correction_blocks_receipt_id_key`: `CREATE UNIQUE INDEX receipt_correction_blocks_receipt_id_key ON ledger.receipt_correction_blocks USING btree (receipt_id)`
- `ledger.receipt_reversals` / `receipt_reversals_correction_id_key`: `CREATE UNIQUE INDEX receipt_reversals_correction_id_key ON ledger.receipt_reversals USING btree (correction_id)`
- `ledger.receipt_reversals` / `receipt_reversals_pkey`: `CREATE UNIQUE INDEX receipt_reversals_pkey ON ledger.receipt_reversals USING btree (receipt_id)`
- `ledger.receiving` / `receiving_pkey`: `CREATE UNIQUE INDEX receiving_pkey ON ledger.receiving USING btree (procurement_id)`
- `ledger.receiving_blocks` / `receiving_blocks_blocked_lot_idx`: `CREATE INDEX receiving_blocks_blocked_lot_idx ON ledger.receiving_blocks USING btree (lot_id) WHERE blocked`
- `ledger.receiving_blocks` / `receiving_blocks_pkey`: `CREATE UNIQUE INDEX receiving_blocks_pkey ON ledger.receiving_blocks USING btree (discrepancy_id)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_pkey`: `CREATE UNIQUE INDEX receiving_threshold_default_pkey ON ledger.receiving_threshold_default USING btree (singleton)`
- `ledger.receiving_threshold_default` / `receiving_threshold_default_revision_key`: `CREATE UNIQUE INDEX receiving_threshold_default_revision_key ON ledger.receiving_threshold_default USING btree (revision)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_company`: `CREATE INDEX receiving_threshold_history_company ON ledger.receiving_threshold_history USING btree (company_id, recorded_at DESC, revision DESC)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_page`: `CREATE INDEX receiving_threshold_history_page ON ledger.receiving_threshold_history USING btree (recorded_at DESC, revision DESC)`
- `ledger.receiving_threshold_history` / `receiving_threshold_history_pkey`: `CREATE UNIQUE INDEX receiving_threshold_history_pkey ON ledger.receiving_threshold_history USING btree (revision)`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_pkey`: `CREATE UNIQUE INDEX receiving_threshold_overrides_pkey ON ledger.receiving_threshold_overrides USING btree (company_id)`
- `ledger.receiving_threshold_overrides` / `receiving_threshold_overrides_revision_key`: `CREATE UNIQUE INDEX receiving_threshold_overrides_revision_key ON ledger.receiving_threshold_overrides USING btree (revision)`

## Sequences (configuration only)

- `identity.audit_events_Id_seq`: data type `bigint`, start `1`, minimum `1`, maximum `9223372036854775807`, increment `1`, cycle `false`. Live last value intentionally omitted.
- `ledger.lot_code_serial`: data type `bigint`, start `1`, minimum `1`, maximum `9223372036854775807`, increment `1`, cycle `false`. Live last value intentionally omitted.
