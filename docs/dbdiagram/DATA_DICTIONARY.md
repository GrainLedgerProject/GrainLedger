# Data dictionary — GrainLedger V2

NN = NOT NULL; NULL = optional. Mass uses numeric(20,6) kg. Identity tables below match the current EF migrations and retain their PascalCase column names. Other tables remain planned V2 design. FK deletion policy is RESTRICT unless stated otherwise.

## rice_varieties

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK, default gen_random_uuid() | Variety identifier |
| code | varchar(40) | NN | UK | Stable catalog code |
| name | text | NN | | Variety name, e.g. ST25 |

Shared F03 catalog. Existing plot_seasons.rice_variety stores the captured name as text, without a mandatory FK. Catalog edits do not rewrite those historical values.

## measurement_units

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK, default gen_random_uuid() | Unit identifier |
| code | varchar(30) | NN | UK | Stable unit code, e.g. kg, ha, mg/kg |
| name | text | NN | | Unit display name |
| quantity_kind | varchar(32) | NN | | Physical quantity, e.g. MASS, AREA, RESIDUE_CONCENTRATION, APPLICATION_RATE, FRACTION |

Shared F03 catalog. Existing unit/rate_unit values remain text snapshots, without new FKs. Each consumer must validate supported units, quantity kind and field length. A catalog entry alone does not define a conversion formula or authorize a unit for every field.

## identity.organizations

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| Id | uuid | NN | PK |  |
| Name | varchar(160) | NN | UK | Organization name |
| Kind | varchar(20) | NN |  | Mill or Customer; at most one Mill |
| CreatedAt | timestamptz | NN |  |  |

Business `company_id` references this table. The FK alone does not prove `Kind = Customer`; each business write must check that condition. Customer records must not be assigned to the Mill organization.

## identity.users

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| Id | uuid | NN | PK |  |
| Provider | varchar(30) | NN | UK with ProviderSubject | Current provider: Google |
| ProviderSubject | varchar(255) | NN | UK with Provider | Provider user ID |
| Email | varchar(320) | NN | Index, not unique |  |
| DisplayName | varchar(120) | NN |  |  |
| PhoneNumber | varchar(32) | NULL |  |  |
| Role | varchar(40) | NULL |  | One role per user; permissions in Identity code |
| OrganizationId | uuid | NULL | FK identity.organizations.Id | Mill or Customer; pending user may be unassigned |
| IsActive | boolean | NN |  |  |
| Version | uuid | NN | Concurrency token |  |
| CreatedAt | timestamptz | NN |  |  |
| UpdatedAt | timestamptz | NN |  |  |

Unique: `(Provider, ProviderSubject)`.

## identity.sessions

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| Id | uuid | NN | PK |  |
| UserId | uuid | NN | FK identity.users.Id | ON DELETE CASCADE |
| CreatedAt | timestamptz | NN |  |  |
| LastSeenAt | timestamptz | NN |  |  |
| ExpiresAt | timestamptz | NN |  |  |
| RevokedAt | timestamptz | NULL |  |  |
| IpAddress | varchar(64) | NULL |  |  |
| UserAgent | varchar(512) | NULL |  |  |

## identity.audit_events

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| Id | bigint | NN | PK | Identity-generated |
| ActorUserId | uuid | NULL | No FK in current migration |  |
| SubjectUserId | uuid | NULL | No FK in current migration |  |
| Action | varchar(100) | NN |  |  |
| DetailsJson | jsonb | NN |  |  |
| OccurredAt | timestamptz | NN |  |  |
| IpAddress | text | NULL |  |  |

## facilities



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| code | varchar(40) | NN | UK |  |
| name | text | NN |  |  |
| kind | varchar(32) | NN |  | MILL, COLLECTION_SITE |
| address | text | NN |  |  |
| is_active | boolean | NN |  |  |

## user_facilities

Assigned operating scope of mill staff; customer access still requires company ownership.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| user_id | uuid | NN | FK identity.users.Id |  |
| facility_id | uuid | NN | FK facilities.id |  |

Unique: `(user_id, facility_id)`.

## locations



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| facility_id | uuid | NN | FK facilities.id |  |
| code | varchar(40) | NN |  |  |
| kind | varchar(32) | NN |  | RECEIVING, DRYING, SILO, MILLING, PACKING, QUARANTINE |
| is_active | boolean | NN |  |  |

Unique: `(facility_id, code)`.

## equipment

F02 equipment and capacity. Capacity unit distinguishes kg per hour from static storage kg. Guard: when location_id exists, location.facility_id must equal equipment.facility_id.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| facility_id | uuid | NN | FK facilities.id |  |
| location_id | uuid | NULL | FK locations.id |  |
| code | varchar(40) | NN |  |  |
| kind | varchar(32) | NN |  | DRYER, MILLING_LINE, PACKING_LINE, SCALE |
| capacity_value | numeric(24,12) | NULL |  |  |
| capacity_unit | varchar(30) | NULL |  |  |
| is_active | boolean | NN |  |  |

Unique: `(facility_id, code)`.

- CHECK `(capacity_value IS NULL) = (capacity_unit IS NULL)`
- CHECK `capacity_value IS NULL OR capacity_value > 0`

## silos



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| facility_id | uuid | NN | FK facilities.id |  |
| code | varchar(40) | NN |  |  |

Unique: `(facility_id, code)`.

## silo_compartments

A silo without physical partitions has one compartment. Guard: location.kind = SILO and location.facility_id must equal silo.facility_id.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| silo_id | uuid | NN | FK silos.id |  |
| location_id | uuid | NN | UK; FK locations.id |  |
| code | varchar(40) | NN |  |  |
| capacity_kg | numeric(20,6) | NN |  |  |

Unique: `(silo_id, code)`.

- CHECK `capacity_kg > 0`

## compartment_occupancies

Partial unique index: one row per compartment with cleared_at IS NULL. Clearance requires zero stock and Mill Operator confirmation; no overlapping occupancy periods.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| compartment_id | uuid | NN | FK silo_compartments.id |  |
| started_at | timestamptz | NN |  |  |
| cleared_at | timestamptz | NULL |  |  |
| cleared_by | uuid | NULL | FK identity.users.Id |  |
| clearance_note | text | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `(cleared_at IS NULL) = (cleared_by IS NULL)`
- CHECK `cleared_at IS NULL OR cleared_at >= started_at`

## farmers



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| code | varchar(40) | NN |  |  |
| name | text | NN |  |  |
| contact | text | NULL |  |  |
| created_at | timestamptz | NN |  |  |

Unique: `(company_id, code)`; `(company_id, id)`.

## plots

WGS84 coordinates; GeoJSON geometry validation is required. Avoid assuming PostGIS is installed.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| farmer_id | uuid | NN | FK farmers.id |  |
| code | varchar(40) | NN |  |  |
| boundary_geojson | jsonb | NN |  |  |
| area_ha | numeric(24,12) | NN |  |  |

Unique: `(farmer_id, code)`; `(company_id, id)`.

- CHECK `area_ha > 0`

## crop_seasons



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| name | text | NN |  |  |
| starts_on | date | NN |  |  |
| ends_on | date | NN |  |  |

Unique: `(company_id, id)`.

- CHECK `ends_on >= starts_on`

## plot_seasons



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plot_id | uuid | NN | FK plots.id |  |
| season_id | uuid | NN | FK crop_seasons.id |  |
| rice_variety | text | NN |  |  |

Unique: `(plot_id, season_id)`; `(company_id, id)`.

## spray_declarations



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plot_season_id | uuid | NN | FK plot_seasons.id |  |
| applied_on | date | NN |  | Ngày phun do nông dân khai báo; không tự suy diễn giờ phun. |
| recorded_by | uuid | NN | FK identity.users.Id |  |
| recorded_at | timestamptz | NN |  |  |
| evidence_ref | text | NULL |  |  |

Unique: `(company_id, id)`.

## spray_items



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| declaration_id | uuid | NN | FK spray_declarations.id |  |
| product_name | text | NULL |  |  |
| substance_id | uuid | NULL | FK active_substances.id |  |
| rate | numeric(24,12) | NN |  |  |
| rate_unit | varchar(40) | NN |  |  |

Unique: `(company_id, id)`.

- CHECK `rate > 0`
- CHECK `product_name IS NOT NULL OR substance_id IS NOT NULL`

## procurements

COMMITTED requires an event and at least one line; each line creates exactly one source lot.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| code | varchar(40) | NN |  |  |
| farmer_id | uuid | NN | FK farmers.id |  |
| collection_site_id | uuid | NN | FK facilities.id |  |
| received_by | uuid | NN | FK identity.users.Id |  |
| received_at | timestamptz | NN |  |  |
| status | varchar(32) | NN |  | DRAFT, COMMITTED |
| event_id | uuid | NULL | UK; FK business_events.id |  |

Unique: `(company_id, code)`; `(company_id, id)`.

## procurement_items

Plot farmer must equal procurement farmer. PlotSeason gives plot and season; do not duplicate inconsistent source IDs.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| procurement_id | uuid | NN | FK procurements.id |  |
| line_no | integer | NN |  |  |
| plot_season_id | uuid | NN | FK plot_seasons.id |  |
| net_mass_kg | numeric(20,6) | NN |  |  |
| measured_value | numeric(24,12) | NN |  |  |
| measured_unit | varchar(20) | NN |  |  |
| moisture_percent | numeric(24,12) | NN |  |  |

Unique: `(procurement_id, line_no)`; `(company_id, id)`.

- CHECK `net_mass_kg > 0`
- CHECK `measured_value > 0`
- CHECK `moisture_percent BETWEEN 0 AND 100`

## source_lots

Source extension of a lot, not a second stock balance. Exactly one source plot via procurement item.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| lot_id | uuid | NN | UK; FK lots.id |  |
| procurement_item_id | uuid | NN | UK; FK procurement_items.id |  |

Unique: `(company_id, id)`.

## offline_operations

Same key/hash returns prior result. Same key with different payload produces conflict; no duplicate intake. Payload includes client IDs for linked offline records.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| submitted_by | uuid | NN | FK identity.users.Id |  |
| device_id | varchar(100) | NN |  |  |
| client_operation_id | uuid | NN |  |  |
| payload_hash | varchar(128) | NN |  |  |
| payload | jsonb | NN |  |  |
| captured_at | timestamptz | NN |  |  |
| capture_geojson | jsonb | NULL |  |  |
| received_at | timestamptz | NN |  |  |
| status | varchar(32) | NN |  | PENDING, APPLIED, CONFLICT, REJECTED |
| applied_event_id | uuid | NULL | UK; FK business_events.id |  |
| result_payload | jsonb | NULL |  |  |
| conflict_reason | text | NULL |  |  |

Unique: `(company_id, device_id, client_operation_id)`; `(company_id, id)`.

## lots

Created by source_lots or conversion_outputs, exactly one origin. No AVAILABLE/RESERVED lot status.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| code | varchar(60) | NN |  |  |
| commodity_id | uuid | NN | FK commodities.id |  |
| stage | varchar(32) | NN |  | RAW_PADDY, DRIED_PADDY, HEAD_RICE, BROKEN_RICE, BRAN, HUSK, BLENDED_RICE, BAGGED_RICE |
| created_at | timestamptz | NN |  |  |

Unique: `(company_id, code)`; `(company_id, id)`.

## stock_positions

Projection; available = on_hand - reserved only if not held and otherwise eligible. Loading stock no longer counts here.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| lot_id | uuid | NN | FK lots.id |  |
| location_id | uuid | NN | FK locations.id |  |
| on_hand_kg | numeric(20,6) | NN |  | Default 0 |
| reserved_kg | numeric(20,6) | NN |  | Default 0 |
| updated_at | timestamptz | NN |  |  |

Unique: `(lot_id, location_id)`; `(company_id, id)`.

- CHECK `on_hand_kg >= 0`
- CHECK `reserved_kg >= 0`
- CHECK `reserved_kg <= on_hand_kg`

## balance_policies

Percent input only: 1 means 1 percent; allowed variance = input kg * tolerance_value / 100. Operational values require mill confirmation.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| conversion_type | varchar(40) | NN |  |  |
| version_no | integer | NN |  |  |
| tolerance_mode | varchar(32) | NN |  | PERCENT_INPUT |
| tolerance_value | numeric(20,6) | NN |  |  |
| configured_by | uuid | NN | FK identity.users.Id |  |
| effective_from | timestamptz | NN |  |  |
| evidence_ref | text | NN |  |  |

Unique: `(conversion_type, version_no)`.

- CHECK `version_no > 0`
- CHECK `tolerance_value >= 0`

## conversions

Committed records immutable. Compensation reverses accounting postings, never adds reverse production lineage. Same-customer inputs and valid stage transitions required. Compensation and replacement commit atomically after physical reconciliation; never expose fictitious restored inputs. Downstream use blocks direct correction. Nonzero committed variance requires reason and Mill Manager confirmation. Variance = input - output - declared loss, within pinned percent tolerance; no invented loss.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| code | varchar(60) | NN |  |  |
| kind | varchar(32) | NN |  | DRYING, MILLING, MERGE, BLENDING, BAGGING, COMPENSATION |
| status | varchar(32) | NN |  | DRAFT, COMMITTED |
| balance_policy_id | uuid | NN | FK balance_policies.id |  |
| equipment_id | uuid | NULL | FK equipment.id |  |
| plan_id | uuid | NULL | FK blend_plans.id |  |
| original_conversion_id | uuid | NULL | FK conversions.id |  |
| declared_loss_kg | numeric(20,6) | NN |  | Default 0 |
| declared_loss_value | numeric(24,12) | NULL |  |  |
| declared_loss_unit | varchar(20) | NULL |  |  |
| loss_reason | text | NULL |  |  |
| measurement_variance_kg | numeric(20,6) | NULL |  |  |
| variance_reason | varchar(32) | NULL |  | SCALE_ROUNDING, MEASUREMENT_ERROR, MOISTURE_ESTIMATION, OTHER_CONFIRMED |
| variance_confirmed_by | uuid | NULL | FK identity.users.Id |  |
| recorded_by | uuid | NN | FK identity.users.Id |  |
| occurred_at | timestamptz | NN |  |  |
| event_id | uuid | NULL | UK; FK business_events.id |  |
| committed_at | timestamptz | NULL |  |  |

Unique: `(company_id, code)`; `(company_id, id)`.

- CHECK `declared_loss_kg >= 0`
- CHECK `(declared_loss_value IS NULL) = (declared_loss_unit IS NULL)`
- CHECK `(kind = 'COMPENSATION') = (original_conversion_id IS NOT NULL)`

## conversion_inputs

mass_kg is normalized ledger mass. When an operator records a physical measurement, retain the original value and unit.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| conversion_id | uuid | NN | FK conversions.id |  |
| lot_id | uuid | NN | FK lots.id |  |
| mass_kg | numeric(20,6) | NN |  |  |
| measured_value | numeric(24,12) | NULL |  |  |
| measured_unit | varchar(20) | NULL |  |  |

Unique: `(conversion_id, lot_id)`; `(company_id, id)`.

- CHECK `mass_kg > 0`
- CHECK `(measured_value IS NULL) = (measured_unit IS NULL)`
- CHECK `measured_value IS NULL OR measured_value > 0`

## conversion_outputs

mass_kg is normalized ledger mass. When an operator records a physical measurement, retain the original value and unit.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| conversion_id | uuid | NN | FK conversions.id |  |
| lot_id | uuid | NN | UK; FK lots.id |  |
| mass_kg | numeric(20,6) | NN |  |  |
| measured_value | numeric(24,12) | NULL |  |  |
| measured_unit | varchar(20) | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `mass_kg > 0`
- CHECK `(measured_value IS NULL) = (measured_unit IS NULL)`
- CHECK `measured_value IS NULL OR measured_value > 0`

## mass_contributions

Both ends must be in the same conversion. For every output, contribution(input, output) = output mass × input mass / total input mass, with a deterministic rounding residual. Sum by output = output mass. Derived attribution, not physical identity of grains.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| input_id | uuid | NN | FK conversion_inputs.id |  |
| output_id | uuid | NN | FK conversion_outputs.id |  |
| output_mass_kg | numeric(20,6) | NN |  |  |

Unique: `(input_id, output_id)`; `(company_id, id)`.

- CHECK `output_mass_kg > 0`

## stock_postings

Signed immutable ledger. Transfer uses paired postings with same lot/event; processing uses input/output refs; loading decrements stock once. Sum per position equals on_hand. Reserved consumption must match plan, position, input/loading and ReservationEvent exactly; reversal links original negative posting.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| event_id | uuid | NN | FK business_events.id |  |
| line_no | integer | NN |  |  |
| position_id | uuid | NN | FK stock_positions.id |  |
| delta_kg | numeric(20,6) | NN |  |  |
| reservation_id | uuid | NULL | FK stock_reservations.id |  |
| input_id | uuid | NULL | FK conversion_inputs.id |  |
| output_id | uuid | NULL | FK conversion_outputs.id |  |
| loading_item_id | uuid | NULL | FK loading_items.id |  |
| reverses_posting_id | uuid | NULL | UK; FK stock_postings.id |  |

Unique: `(event_id, line_no)`; `(company_id, id)`.

- CHECK `delta_kg <> 0`
- CHECK `num_nonnulls(input_id, output_id, loading_item_id) <= 1`

## correction_requests

Pending/blocked correction locks related output use independently of HOLD. Mill Manager verifies physical stock. Compensation, replacement, postings and reservation accounting commit atomically. Block direct correction after downstream consumption/loading. All conversions distinct and same company; compensation.original_conversion_id matches request. CANCELLED_VERIFIED needs evidence original is correct and never releases HOLD.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| original_conversion_id | uuid | NN | FK conversions.id |  |
| compensation_id | uuid | NULL | UK; FK conversions.id |  |
| replacement_id | uuid | NULL | UK; FK conversions.id |  |
| requested_by | uuid | NN | FK identity.users.Id |  |
| reconciled_by | uuid | NULL | FK identity.users.Id |  |
| reconciliation_ref | text | NULL |  |  |
| requested_at | timestamptz | NN |  |  |
| resolved_at | timestamptz | NULL |  |  |
| reason | text | NN |  |  |
| outcome | varchar(32) | NN |  | PENDING_RECONCILIATION, BLOCKED_DOWNSTREAM, COMPLETED, CANCELLED_VERIFIED |
| handling_note | text | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `compensation_id IS NULL OR compensation_id <> original_conversion_id`
- CHECK `replacement_id IS NULL OR replacement_id <> original_conversion_id`
- CHECK `replacement_id IS NULL OR replacement_id <> compensation_id`
- CHECK `outcome <> 'COMPLETED' OR (compensation_id IS NOT NULL AND replacement_id IS NOT NULL AND reconciled_by IS NOT NULL AND reconciliation_ref IS NOT NULL AND resolved_at IS NOT NULL)`

## samples

One sample represents one source paddy lot; no composite sample in MVP.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| code | varchar(60) | NN |  |  |
| source_lot_id | uuid | NN | FK source_lots.id |  |
| seal_code | varchar(100) | NN |  |  |
| collected_by | uuid | NN | FK identity.users.Id |  |
| collected_at | timestamptz | NN |  |  |

Unique: `(company_id, code)`; `(company_id, id)`.

## sample_handovers



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| sample_id | uuid | NN | FK samples.id |  |
| recorded_by | uuid | NN | FK identity.users.Id |  |
| from_party | text | NN |  |  |
| to_party | text | NN |  |  |
| handed_over_at | timestamptz | NN |  |  |
| seal_condition | text | NN |  |  |
| evidence_ref | text | NULL |  |  |

Unique: `(company_id, id)`.

## lab_reports

Laboratory is stored as an evidence source, not an added system actor. REGISTERED requires registered_at. Registered evidence immutable. Supersession stays in same sample/report context, no branch or cycle; registration time records when evidence became known.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| sample_id | uuid | NN | FK samples.id |  |
| certificate_no | varchar(100) | NN |  |  |
| laboratory_name | text | NN |  |  |
| issued_at | timestamptz | NN |  |  |
| registered_by | uuid | NN | FK identity.users.Id |  |
| registered_at | timestamptz | NULL |  |  |
| supersedes_report_id | uuid | NULL | UK; FK lab_reports.id |  |
| evidence_ref | text | NN |  |  |
| status | varchar(32) | NN |  | DRAFT, REGISTERED |

Unique: `(company_id, id)`.

## lab_results

EQ needs a value; LT needs a reporting threshold; ND never means numeric zero. Interpret through a documented bound policy.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| report_id | uuid | NN | FK lab_reports.id |  |
| substance_id | uuid | NN | FK active_substances.id |  |
| qualifier | varchar(32) | NN |  | EQ, LT, ND |
| value | numeric(24,12) | NULL |  |  |
| lod | numeric(24,12) | NULL |  |  |
| loq | numeric(24,12) | NULL |  |  |
| unit | varchar(20) | NN |  |  |

Unique: `(report_id, substance_id)`; `(company_id, id)`.

- CHECK `value IS NULL OR value >= 0`
- CHECK `lod IS NULL OR lod >= 0`
- CHECK `loq IS NULL OR loq >= 0`

## active_substances

An Administrator may delete an unused active substance with a catalog audit event. Business references must use restrictive foreign keys and block deletion of a substance already in use.



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| code | varchar(60) | NN | UK |  |
| name | text | NN |  |  |

## commodities



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| code | varchar(40) | NN | UK |  |
| name | text | NN |  |  |

## markets



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| code | varchar(40) | NN | UK |  |
| name | text | NN |  |  |

## quality_attributes



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| code | varchar(60) | NN | UK |  |
| name | text | NN |  |  |
| unit | varchar(30) | NN |  |  |

## catalog_events

Append-only audit for F03 changes. The catalog write and its audit row commit in one transaction. For a successor MRL publication, the event records the old and new `effective_to` of the preceding version and identifies the successor in `after_state`. `correlation_id` links events from one publication.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| actor_id | uuid | NN | FK identity.users.Id | User who performed the change |
| event_type | varchar(60) | NN |  | Action name |
| resource_type | varchar(60) | NN |  | Catalog or MRL resource name |
| resource_id | uuid | NN |  | ID of the changed resource; not a polymorphic FK |
| occurred_at | timestamptz | NN |  | Server time |
| correlation_id | uuid | NN |  | Groups events from one operation |
| before_state | jsonb | NULL |  | Prior state; null on creation |
| after_state | jsonb | NULL |  | New state; null on deletion |

- CHECK `before_state IS NOT NULL OR after_state IS NOT NULL`
- Index `(resource_type, resource_id, occurred_at)` and `correlation_id`.

## mrl_versions

Published effective intervals use `[effective_from, effective_to)` and must not overlap for one market/commodity. PUBLISHED requires `published_by` and `published_at`. Source and limit rows of a published version remain unchanged. When a successor is published, the only allowed update to its open-ended predecessor is setting `effective_to` to the successor's `effective_from`; write an audit event in the same transaction. A successor cannot be published with a backdated effective start. Each assessment keeps its selected version and evidence snapshot.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| market_id | uuid | NN | FK markets.id |  |
| commodity_id | uuid | NN | FK commodities.id |  |
| version_no | integer | NN |  |  |
| status | varchar(32) | NN |  | DRAFT, PUBLISHED |
| effective_from | timestamptz | NN |  |  |
| effective_to | timestamptz | NULL |  |  |
| published_by | uuid | NULL | FK identity.users.Id |  |
| published_at | timestamptz | NULL |  |  |
| source_ref | text | NN |  |  |

Unique: `(market_id, commodity_id, version_no)`.

- CHECK `effective_to IS NULL OR effective_to > effective_from`

## mrl_limits



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| mrl_version_id | uuid | NN | FK mrl_versions.id |  |
| substance_id | uuid | NN | FK active_substances.id |  |
| limit_value | numeric(24,12) | NN |  |  |
| unit | varchar(20) | NN |  |  |

Unique: `(mrl_version_id, substance_id)`.

- CHECK `limit_value >= 0`

## safety_margin_policies

Customer Manager configures versioned safety margin for its company/market/commodity. Decision stores a snapshot and this policy reference. Effective intervals must not overlap for the same company, market and commodity.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| market_id | uuid | NN | FK markets.id |  |
| commodity_id | uuid | NN | FK commodities.id |  |
| version_no | integer | NN |  |  |
| margin_value | numeric(24,12) | NN |  |  |
| margin_mode | varchar(32) | NN |  | RELATIVE_FRACTION, ABSOLUTE_MG_KG |
| effective_from | timestamptz | NN |  |  |
| effective_to | timestamptz | NULL |  |  |
| evidence_ref | text | NN |  |  |
| configured_by | uuid | NN | FK identity.users.Id |  |
| configured_at | timestamptz | NN |  |  |

Unique: `(company_id, market_id, commodity_id, version_no)`; `(company_id, id)`.

- CHECK `margin_value >= 0`
- CHECK `margin_mode <> 'RELATIVE_FRACTION' OR margin_value < 1`
- CHECK `effective_to IS NULL OR effective_to > effective_from`

## cohort_policies



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| version_no | integer | NN |  |  |
| window_rule | text | NN |  |  |
| sampling_rule | text | NN |  |  |
| evidence_ref | text | NN |  |  |
| confirmed_by | uuid | NN | FK identity.users.Id |  |
| confirmed_at | timestamptz | NN |  |  |

Unique: `(company_id, version_no)`; `(company_id, id)`.

## cohorts



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| policy_id | uuid | NN | FK cohort_policies.id |  |
| collection_site_id | uuid | NN | FK facilities.id |  |
| season_id | uuid | NN | FK crop_seasons.id |  |
| window_start | timestamptz | NN |  |  |
| window_end | timestamptz | NN |  |  |

Unique: `(company_id, policy_id, collection_site_id, season_id, window_start, window_end)`; `(company_id, id)`.

- CHECK `window_end > window_start`

## cohort_members

At most one cohort per source lot and policy version; enforce membership rule through DB guard.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| cohort_id | uuid | NN | FK cohorts.id |  |
| source_lot_id | uuid | NN | FK source_lots.id |  |

Unique: `(cohort_id, source_lot_id)`; `(company_id, id)`.

## cohort_bounds



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| cohort_id | uuid | NN | FK cohorts.id |  |
| substance_id | uuid | NN | FK active_substances.id |  |
| version_no | integer | NN |  |  |
| bound_value | numeric(24,12) | NN |  |  |
| unit | varchar(20) | NN |  |  |
| evidence_ref | text | NN |  |  |
| immutable_membership_ref | text | NN |  | Immutable content contract: IMMUTABLE_CONTENT.md. Frozen member IDs and source lot IDs, cohort/policy version, substance coverage, quantities/units, actor and knowledge time; later members never extend this bound. |
| confirmed_by | uuid | NN | FK identity.users.Id |  |
| confirmed_at | timestamptz | NN |  |  |

Unique: `(cohort_id, substance_id, version_no)`; `(company_id, id)`.

- CHECK `bound_value >= 0`

## propagation_policies

One version per commodity/input-stage/output-stage/conversion-type tuple. code is a label; optional substance_id narrows applicability but does not create an independently versioned namespace. Milling processing factors need evidence, not invented defaults.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| code | varchar(60) | NN |  |  |
| version_no | integer | NN |  |  |
| substance_id | uuid | NULL | FK active_substances.id |  |
| commodity_id | uuid | NN | FK commodities.id |  |
| conversion_type | varchar(40) | NN |  |  |
| input_stage | varchar(32) | NN |  |  |
| output_stage | varchar(32) | NN |  |  |
| rule_definition | jsonb | NN |  |  |
| evidence_ref | text | NN |  |  |
| confirmed_by | uuid | NN | FK identity.users.Id |  |
| confirmed_at | timestamptz | NN |  |  |

Unique: `(commodity_id, input_stage, output_stage, conversion_type, version_no)`.

## residue_bounds

Missing valid bound blocks approval; no fake zero. CONVERSION needs policy and input evidence. Earlier bound versions stay available. A new source, lot or rule creates a new version; a relied-on bound is immutable.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| lot_id | uuid | NN | FK lots.id |  |
| substance_id | uuid | NN | FK active_substances.id |  |
| version_no | integer | NN |  |  |
| bound_value | numeric(24,12) | NN |  |  |
| unit | varchar(20) | NN |  |  |
| source_kind | varchar(32) | NN |  | LAB, COHORT, CONVERSION |
| conversion_id | uuid | NULL | FK conversions.id |  |
| propagation_policy_id | uuid | NULL | FK propagation_policies.id |  |
| calculation_basis_ref | text | NN |  | Immutable content contract: IMMUTABLE_CONTENT.md. Exact source IDs/versions, substances, input masses, units, conversion, rule/factors, rounding, actor and knowledge time; includes missing-evidence outcomes. |
| calculated_at | timestamptz | NN |  |  |

Unique: `(lot_id, substance_id, version_no)`; `(company_id, id)`.

- CHECK `bound_value >= 0`

## bound_evidence

Source type must match bound.source_kind; bound chains acyclic and substance-compatible. Guard: LAB result must trace through its sample to bound.lot; COHORT bound must contain that source lot under the applicable company/site/season/window policy; CONVERSION bound must use actual input lots of bound.conversion.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| bound_id | uuid | NN | FK residue_bounds.id |  |
| lab_result_id | uuid | NULL | FK lab_results.id |  |
| cohort_bound_id | uuid | NULL | FK cohort_bounds.id |  |
| input_bound_id | uuid | NULL | FK residue_bounds.id |  |
| input_mass_kg | numeric(20,6) | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `num_nonnulls(lab_result_id, cohort_bound_id, input_bound_id) = 1`
- CHECK `input_bound_id IS NULL OR input_bound_id <> bound_id`

## lot_quality_values

Append measurements; assessments reference exact rows. Propagation is attribute-specific.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| lot_id | uuid | NN | FK lots.id |  |
| attribute_id | uuid | NN | FK quality_attributes.id |  |
| value | numeric(24,12) | NN |  |  |
| unit | varchar(30) | NN |  |  |
| measured_at | timestamptz | NN |  |  |
| evidence_ref | text | NN |  |  |
| recorded_by | uuid | NN | FK identity.users.Id |  |

Unique: `(company_id, id)`.

## export_orders



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| code | varchar(60) | NN |  |  |
| market_id | uuid | NN | FK markets.id |  |
| commodity_id | uuid | NN | FK commodities.id |  |
| required_mass_kg | numeric(20,6) | NN |  |  |
| shipping_from | date | NN |  |  |
| shipping_to | date | NN |  |  |
| created_by | uuid | NN | FK identity.users.Id |  |
| created_at | timestamptz | NN |  |  |

Unique: `(company_id, code)`; `(company_id, id)`.

- CHECK `required_mass_kg > 0`
- CHECK `shipping_to >= shipping_from`

## order_specifications



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| order_id | uuid | NN | FK export_orders.id |  |
| version_no | integer | NN |  |  |
| recorded_at | timestamptz | NN |  |  |

Unique: `(order_id, version_no)`; `(company_id, id)`.

## specification_limits



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| specification_id | uuid | NN | FK order_specifications.id |  |
| attribute_id | uuid | NN | FK quality_attributes.id |  |
| min_value | numeric(24,12) | NULL |  |  |
| max_value | numeric(24,12) | NULL |  |  |

Unique: `(specification_id, attribute_id)`; `(company_id, id)`.

- CHECK `num_nonnulls(min_value, max_value) >= 1`
- CHECK `min_value IS NULL OR max_value IS NULL OR min_value <= max_value`

## containers

Represents one shipment use of a physical container; physical_number is not unique forever.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| order_id | uuid | NN | FK export_orders.id |  |
| internal_code | varchar(60) | NN |  |  |
| physical_number | varchar(40) | NULL |  |  |
| seal_number | varchar(60) | NULL |  |  |
| target_mass_kg | numeric(20,6) | NN |  |  |
| status | varchar(32) | NN |  | PLANNED, LOADING, CLOSED |
| closed_at | timestamptz | NULL |  |  |

Unique: `(company_id, internal_code)`; `(company_id, id)`.

- CHECK `target_mass_kg > 0`

## blend_plans

At most one execution_authorized plan per container via partial unique index, including EXECUTED awaiting loading. Authorization changes require audit event and container lock. STALE/REJECTED/CANCELLED cannot be authorized. Specification must belong to the container order. A committed plan item is immutable; changed order, specification, input stock, evidence or rule makes the plan STALE. SEARCH_EXHAUSTED is not a proof of global infeasibility.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| container_id | uuid | NN | FK containers.id |  |
| specification_id | uuid | NN | FK order_specifications.id |  |
| version_no | integer | NN |  |  |
| status | varchar(32) | NN |  | DRAFT, PROPOSED, APPROVED, REJECTED, IN_EXECUTION, EXECUTED, STALE, CANCELLED |
| execution_authorized | boolean | NN |  | Default false |
| search_outcome | varchar(32) | NN |  | PENDING, FOUND, SEARCH_EXHAUSTED, INVALID_INPUT |
| algorithm_version | varchar(100) | NN |  |  |
| requested_by | uuid | NN | FK identity.users.Id |  |
| created_at | timestamptz | NN |  |  |

Unique: `(container_id, version_no)`; `(company_id, id)`.

## plan_items



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plan_id | uuid | NN | FK blend_plans.id |  |
| position_id | uuid | NN | FK stock_positions.id |  |
| planned_mass_kg | numeric(20,6) | NN |  |  |

Unique: `(plan_id, position_id)`; `(company_id, id)`.

- CHECK `planned_mass_kg > 0`

## plan_findings



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plan_id | uuid | NN | FK blend_plans.id |  |
| assessment_id | uuid | NULL | FK assessments.id |  |
| substance_id | uuid | NULL | FK active_substances.id |  |
| attribute_id | uuid | NULL | FK quality_attributes.id |  |
| constraint_code | varchar(60) | NN |  |  |
| explanation | text | NN |  |  |

Unique: `(company_id, id)`.

## finding_lots



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| finding_id | uuid | NN | FK plan_findings.id |  |
| lot_id | uuid | NN | FK lots.id |  |

Unique: `(finding_id, lot_id)`; `(company_id, id)`.

## assessments

The policy reference and margin fields snapshot the margin used. INCOMPLETE supports rejecting stale/missing evidence. PASS requires all regulated substances and specifications, current limits and no HOLD. CLOSE has no plan/loading FK and covers ALL committed container loadings across historical plans. Immutable basis pins loading IDs, mass, positions, lots, historical authorizations, specification and current evidence. Other purposes require matching plan/container; LOADING also matches loading plan/container.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| container_id | uuid | NN | FK containers.id |  |
| plan_id | uuid | NULL | FK blend_plans.id |  |
| loading_id | uuid | NULL | FK loadings.id |  |
| mrl_version_id | uuid | NULL | FK mrl_versions.id |  |
| safety_margin_policy_id | uuid | NN | FK safety_margin_policies.id |  |
| purpose | varchar(32) | NN |  | PROPOSE, REVIEW, LOADING, CLOSE |
| verdict | varchar(32) | NN |  | PASS, FAIL, INCOMPLETE |
| safety_margin | numeric(24,12) | NN |  |  |
| margin_mode | varchar(32) | NN |  | RELATIVE_FRACTION, ABSOLUTE_MG_KG |
| assessed_by | uuid | NULL | FK identity.users.Id |  |
| assessed_at | timestamptz | NN |  |  |
| basis_snapshot | jsonb | NN |  | Immutable inline content under IMMUTABLE_CONTENT.md: schema version, scope, sources/versions, masses/units, substance coverage, rules, actor and knowledge time. CLOSE includes every committed loading and historical authorization. |
| basis_hash | varchar(128) | NN |  | SHA-256 of canonical UTF-8 JSON basis_snapshot; verify on retrieval. Retain immutable content and digest for full linked-record lifetime. |
| explanation | text | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `(purpose = 'CLOSE' AND plan_id IS NULL AND loading_id IS NULL) OR (purpose IN ('PROPOSE','REVIEW') AND plan_id IS NOT NULL AND loading_id IS NULL) OR (purpose = 'LOADING' AND plan_id IS NOT NULL AND loading_id IS NOT NULL)`
- CHECK `safety_margin >= 0`
- CHECK `margin_mode <> 'RELATIVE_FRACTION' OR safety_margin < 1`
- CHECK `verdict <> 'PASS' OR mrl_version_id IS NOT NULL`

## assessment_bounds



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| assessment_id | uuid | NN | FK assessments.id |  |
| bound_id | uuid | NN | FK residue_bounds.id |  |
| evaluated_mass_kg | numeric(20,6) | NN |  |  |

Unique: `(assessment_id, bound_id)`; `(company_id, id)`.

- CHECK `evaluated_mass_kg > 0`

## assessment_quality

Snapshot of evaluated quality value and attribute-specific calculation basis; quality_value_id identifies the source measurement.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| assessment_id | uuid | NN | FK assessments.id |  |
| quality_value_id | uuid | NN | FK lot_quality_values.id |  |
| evaluated_value | numeric(24,12) | NN |  |  |
| unit | varchar(30) | NN |  |  |
| calculation_basis | jsonb | NN |  |  |

Unique: `(assessment_id, quality_value_id)`; `(company_id, id)`.

## plan_reviews

Only Customer Manager for this company. Guard: assessment.plan_id must equal plan_id and assessment purpose must be REVIEW. APPROVE requires that plan assessment to PASS and reserves stock atomically; REJECT can use INCOMPLETE. Fresh assessment/review may reapprove unchanged stale plan. OUTPUT_ONLY evaluates actual remaining outputs and cannot authorize production. Changed inputs need a new plan for unproduced remainder, never relabel old outputs.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plan_id | uuid | NN | FK blend_plans.id |  |
| assessment_id | uuid | NN | FK assessments.id |  |
| reviewed_by | uuid | NN | FK identity.users.Id |  |
| decision | varchar(32) | NN |  | APPROVE, REJECT |
| review_scope | varchar(32) | NN |  | ALL_REMAINING, OUTPUT_ONLY |
| reason | text | NULL |  |  |
| reviewed_at | timestamptz | NN |  |  |

Unique: `(company_id, id)`.

## plan_output_allocations

Maps PlanOutputAllocation. Output conversion belongs to this plan; position lot equals output lot. Net consumed plus outstanding OUTPUT reservations cannot exceed assigned_kg; total assignments cannot exceed produced output.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plan_id | uuid | NN | FK blend_plans.id |  |
| position_id | uuid | NN | FK stock_positions.id |  |
| output_id | uuid | NN | FK conversion_outputs.id |  |
| assigned_kg | numeric(20,6) | NN |  |  |

Unique: `(plan_id, position_id, output_id)`; `(company_id, id)`.

- CHECK `assigned_kg > 0`

## stock_reservations

Direct plan/position FK avoids ambiguous reservation target. Input references selected item; output references plan_output_allocations. Position and plan match source. Projections: reserved=sum RESERVE; consumed=sum CONSUME minus sum REVERSE_CONSUME; released=sum RELEASE. Across all INPUT reservations for one item, net consumed plus outstanding cannot exceed planned_mass_kg.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| plan_id | uuid | NN | FK blend_plans.id |  |
| position_id | uuid | NN | FK stock_positions.id |  |
| plan_item_id | uuid | NULL | FK plan_items.id |  |
| plan_output_id | uuid | NULL | FK plan_output_allocations.id |  |
| purpose | varchar(32) | NN |  | INPUT, OUTPUT |
| reserved_kg | numeric(20,6) | NN |  |  |
| consumed_kg | numeric(20,6) | NN |  | Default 0 |
| released_kg | numeric(20,6) | NN |  | Default 0 |
| created_at | timestamptz | NN |  |  |

Unique: `(company_id, id)`.

- CHECK `reserved_kg > 0`
- CHECK `consumed_kg >= 0`
- CHECK `released_kg >= 0`
- CHECK `consumed_kg + released_kg <= reserved_kg`
- CHECK `(purpose = 'INPUT' AND plan_item_id IS NOT NULL AND plan_output_id IS NULL) OR (purpose = 'OUTPUT' AND plan_output_id IS NOT NULL AND plan_item_id IS NULL)`

## reservation_events

Append-only. REVERSE_CONSUME undoes one original committed INPUT CONSUME, same reservation/mass, once only; no partial reversal or reversal-of-reversal. Positive movement reverses original negative movement. Original, compensation and replacement movements match correction conversion IDs. Replacement CONSUME carries correction_request_id. Posting, reservation, position, event and mass match atomically. RELEASE removes outstanding quota, never undoes consumption; movement_id and reverses_event_id stay NULL. Correction RELEASE requires correction_request_id and shared correction correlation, committed in the same package. Surplus restored quota is released unless physically present and still authorized. event_id/line_no provides idempotency.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| reservation_id | uuid | NN | FK stock_reservations.id |  |
| event_id | uuid | NN | FK business_events.id |  |
| line_no | integer | NN |  |  |
| reverses_event_id | uuid | NULL | UK; FK reservation_events.id |  |
| movement_id | uuid | NULL | UK; FK stock_postings.id |  |
| correction_request_id | uuid | NULL | FK correction_requests.id |  |
| kind | varchar(32) | NN |  | RESERVE, CONSUME, RELEASE, REVERSE_CONSUME |
| mass_kg | numeric(20,6) | NN |  |  |

Unique: `(event_id, line_no)`; `(company_id, id)`.

- CHECK `mass_kg > 0`
- CHECK `line_no > 0`
- CHECK `reverses_event_id IS NULL OR reverses_event_id <> id`
- CHECK `(kind IN ('CONSUME','REVERSE_CONSUME')) = (movement_id IS NOT NULL)`
- CHECK `(kind = 'REVERSE_CONSUME') = (reverses_event_id IS NOT NULL)`
- CHECK `kind <> 'REVERSE_CONSUME' OR correction_request_id IS NOT NULL`
- CHECK `kind <> 'RESERVE' OR correction_request_id IS NULL`

## loadings

Export Officer records/commits loading. Container must match approved plan and evidence must be rechecked. CLOSE requires committed loading total = container target mass; sum of CLOSED container quantities may not exceed the export order required mass. Any shipment tolerance requires an explicit, separately versioned rule.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| container_id | uuid | NN | FK containers.id |  |
| plan_id | uuid | NN | FK blend_plans.id |  |
| recorded_by | uuid | NN | FK identity.users.Id |  |
| status | varchar(32) | NN |  | DRAFT, COMMITTED |
| loaded_at | timestamptz | NN |  |  |
| event_id | uuid | NULL | UK; FK business_events.id |  |

Unique: `(company_id, id)`.

## loading_items

Consumes matching OUTPUT reservation and warehouse position exactly once; completed conversion lineage must match plan.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| loading_id | uuid | NN | FK loadings.id |  |
| position_id | uuid | NN | FK stock_positions.id |  |
| reservation_id | uuid | NN | FK stock_reservations.id |  |
| mass_kg | numeric(20,6) | NN |  |  |
| bag_count | integer | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `mass_kg > 0`
- CHECK `bag_count IS NULL OR bag_count > 0`

## dossiers

Immutable exact references to actual loadings, all historical plans/specifications, limits, bound/quality evidence versions and decision times. Finalize only with CLOSE PASS for the same container (assessment plan_id and loading_id are null) after the target-mass check.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| container_id | uuid | NN | UK; FK containers.id |  |
| closing_assessment_id | uuid | NN | UK; FK assessments.id |  |
| created_by | uuid | NN | FK identity.users.Id |  |
| finalized_at | timestamptz | NN |  |  |
| snapshot | jsonb | NN |  |  |
| snapshot_hash | varchar(128) | NN |  |  |

Unique: `(company_id, id)`.

## dossier_supplements



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| dossier_id | uuid | NN | FK dossiers.id |  |
| added_by | uuid | NN | FK identity.users.Id |  |
| added_at | timestamptz | NN |  |  |
| reason | text | NN |  |  |
| evidence_ref | text | NN |  |  |

Unique: `(company_id, id)`.

## shipment_documents



| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| container_id | uuid | NN | FK containers.id |  |
| kind | varchar(60) | NN |  |  |
| version_no | integer | NN |  |  |
| file_ref | text | NN |  |  |
| file_hash | varchar(128) | NN |  |  |
| issued_by | uuid | NN | FK identity.users.Id |  |
| issued_at | timestamptz | NN |  |  |

Unique: `(container_id, kind, version_no)`; `(company_id, id)`.

## holds

Exactly one target, no silo target. No release/archive flag that silently restores eligibility in MVP. Customer Manager orders hold.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| lot_id | uuid | NULL | FK lots.id |  |
| container_id | uuid | NULL | FK containers.id |  |
| lab_result_id | uuid | NULL | FK lab_results.id |  |
| ordered_by | uuid | NN | FK identity.users.Id |  |
| ordered_at | timestamptz | NN |  |  |
| reason | text | NN |  |  |

Unique: `(company_id, id)`.

- CHECK `num_nonnulls(lot_id, container_id) = 1`

## hold_actions

Mill Manager handling does not clear HOLD. Quarantine moves still recorded as stock postings.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| hold_id | uuid | NN | FK holds.id |  |
| recorded_by | uuid | NN | FK identity.users.Id |  |
| kind | varchar(32) | NN |  | ACKNOWLEDGE, ISOLATE, RECORD_RESPONSE |
| note | text | NN |  |  |
| evidence_ref | text | NULL |  |  |
| recorded_at | timestamptz | NN |  |  |

Unique: `(company_id, id)`.

## notifications

F19 in-app notification, visibility limited to authorized company or assigned mill staff. No external messaging integration added.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| hold_id | uuid | NN | FK holds.id |  |
| recipient_id | uuid | NN | FK identity.users.Id |  |
| message | text | NN |  |  |
| created_at | timestamptz | NN |  |  |
| read_at | timestamptz | NULL |  |  |

Unique: `(hold_id, recipient_id)`; `(company_id, id)`.

## business_events

Append-only envelope, global company null only for platform events. Structured target IDs supplement but do not replace business FKs. Failed validation and traceback queries are audited here.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NULL | FK identity.organizations.Id |  |
| actor_id | uuid | NULL | FK identity.users.Id |  |
| event_type | varchar(60) | NN |  |  |
| occurred_at | timestamptz | NN |  |  |
| recorded_at | timestamptz | NN |  |  |
| correlation_id | uuid | NN |  |  |
| payload | jsonb | NN |  |  |

## state_changes

Target company and allowed state transition checked at DB boundary.

| Column | PostgreSQL type | Required | Key / Reference | Meaning / allowed values |
| --- | --- | --- | --- | --- |
| id | uuid | NN | PK |  |
| company_id | uuid | NN | FK identity.organizations.Id |  |
| event_id | uuid | NN | FK business_events.id |  |
| plan_id | uuid | NULL | FK blend_plans.id |  |
| conversion_id | uuid | NULL | FK conversions.id |  |
| container_id | uuid | NULL | FK containers.id |  |
| from_state | varchar(32) | NULL |  |  |
| to_state | varchar(32) | NN |  |  |
| reason | text | NULL |  |  |

Unique: `(company_id, id)`.

- CHECK `num_nonnulls(plan_id, conversion_id, container_id) = 1`
