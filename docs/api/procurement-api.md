# P3 online procurement API — Capture handoff

Capture owns procurement source records; Ledger owns accepted stock and positions. `POST /api/procurements` registers immutable source expectation only: no lot, position, posting or usable stock is created. One procurement represents one complete arrival; create separate procurement per load. There is no truck model, customer settlement workflow, partial arrival or surplus acceptance. Receiving loss thresholds control escalation only and do not establish loss cause or settle payments.

Assigned MillOperator records one complete actual measurement set covering every line. Exact normalized match confirms arrival and posts measured actual mass. A complete positive set with at least one shortage and every line independently within the owning company's effective threshold posts measured actual mass as `RECEIVED_WITHIN_TOLERANCE`; any surplus or over-threshold shortage remains pending with no usable stock for manager review. Assigned MillManager resolves discrepancies with required reason and evidence scoped to procurement owner and assigned facility: `APPROVE_SHORTAGE` posts actual mass only if all actuals are at or below expected and at least one is short; `REQUEST_REMEASUREMENT` appends request and subsequent observations; `KEEP_BLOCKED` leaves unexplained shortage/surplus without stock.

Existing historical procurement evidence and Ledger stock are grandfathered unchanged. Migration `20261014190000_PhysicalReceiving` applies workflow rules only to new records; it does not retroactively freeze or reinterpret legacy stock. `receiving_workflow IS NULL` identifies legacy committed procurement; new registration uses `PHYSICAL_ARRIVAL_V1`. Legacy/synthetic evidence is historical only; no automatic conversion or deletion.

Forward Ledger migrations add the protected Capture/Ledger integration and package validation (`20261014010000_UseOfficialCaptureBoundary`) and receiving-threshold configuration/snapshots (`20261019000000_ReceivingLossThreshold`, resource `ReceivingLossThreshold.sql`). The threshold migration initializes 2% with migration-origin history, leaves overrides absent and historical measurement snapshots absent. Preserve generated migration history and existing stock. Shared database migration requires explicit owner approval; no shared database migration was applied for threshold verification.

After `20261017000000_UnifiedLotCodes` and the threshold migration, owner-applied forward migration **`20261019010000_RepairReceivingLotCodeBoundary`** (`RepairReceivingLotCodeBoundary.sql`) is required. Previously installed threshold functions can still expect `LL-…` codes while schema-version-2 Capture snapshots reserve `RAW-…` codes, causing `Pending source package incomplete`. The repair replaces only `ledger.procurement_package_complete` and `ledger.receiving_detail`, retaining threshold behavior and all historical stock, source evidence and applied snapshots. Changing historical migration files does not repair databases that already recorded them; retain migration history and apply the forward migration through the approved owner job. No shared deployment or live fix is claimed.

The API routes are:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/procurement-reference-data?collectionSiteId=&search=&page=&pageSize=&sortBy=&sortDirection=` | PADDY commodity, supported mass units and eligible receiving options. |
| POST | `/api/procurements` | Register source procurement and expected line masses; creates no stock. |
| GET | `/api/ledger/receiving-tasks?facilityId={id}&page=1&pageSize=25` | Read assigned-facility arrivals needing operator or manager action. |
| GET | `/api/ledger/receiving/{id}` | Read scoped arrival task and append-only observations/reviews. |
| POST | `/api/ledger/receiving/{id}/measurements` | Submit complete operator actual-mass set for one arrival. |
| POST | `/api/ledger/receiving/{id}/reviews` | Submit evidence-backed manager action bound to version and measurement set. |
| GET | `/api/procurements?farmerId=&from=&to=&search=&page=&pageSize=&sortBy=&sortDirection=` | List receipts for authenticated company. |
| GET | `/api/procurements/{id}` | Read immutable source snapshot and receiving status. |
| GET | `/api/source-lots/{id}` | Read source provenance and current stock; pending has zero usable stock. |

## Receiving loss threshold configuration API

All routes below use persisted authorization. Default and override management require Administrator `platform.configure`; company override targets are validated against persisted customer organizations. Versions are UUID strings. New override creation is `POST` and has no caller-supplied version; updates/deletion require the current version. Every mutation requires a nonempty reason. A stale version conflicts; concurrent creates cannot replace an existing override. Deletion removes only the active override; history remains.

| Method | Path | Purpose / access |
| --- | --- | --- |
| GET | `/api/ledger/receiving-thresholds` | Administrator Setup read: `{systemDefault:{percentage,version},companyOverrides:[{companyId,companyName,percentage,version}]}`. |
| GET | `/api/ledger/receiving-thresholds/companies?search=&page=1&pageSize=25` | Administrator authorized customer-company lookup; `{items:[{id,name}],page,pageSize,totalCount,hasMore}`; bounded search, page size 1–100. |
| GET | `/api/ledger/receiving-thresholds/history?scope=&companyId=&page=1&pageSize=25` | Administrator bounded history; optional `scope=SYSTEM_DEFAULT` or `COMPANY_OVERRIDE` and company ID. Paged `{items,page,pageSize,totalCount,hasMore}`; entries include revision, scope/action, old/new percentage, actor, server timestamp, reason and migration ID. |
| GET | `/api/ledger/receiving-thresholds/own-company` | Active persisted `CustomerManager`, `ExportStaff` or `PurchasingStaff`; own-company effective setting only: `{companyId,percentage,source,revision}`. |
| PUT | `/api/ledger/receiving-thresholds/default` | Administrator: `{percentage,version,reason}`. |
| POST | `/api/ledger/receiving-thresholds/companies/{companyId}` | Administrator creates override: `{percentage,reason}`. |
| PUT | `/api/ledger/receiving-thresholds/companies/{companyId}` | Administrator updates override: `{percentage,version,reason}`. |
| DELETE | `/api/ledger/receiving-thresholds/companies/{companyId}` | Administrator removes override: `{version,reason}`. |

`percentage` MUST be a JSON string in finite base-10 decimal form, with at most six fractional digits and `0 <= T < 100`; JSON numbers, null, exponent notation, malformed values and excess precision are rejected without rounding. Values persist as `numeric(8,6)`. An explicit `"0"` override means exact-match-only acceptance, not absent override.

Company users can read only their persisted own-company effective setting; they cannot read overrides for another company or configuration history. Company roles do not gain broad Setup access. Operators/managers receive effective threshold only in assigned-facility receiving tasks.

Every successful configuration change appends immutable Ledger threshold history atomically with the setting change: scope/action, before/after state, fresh non-reused revision, authenticated actor, server timestamp and reason. Initial 2% is migration-origin history, not a fabricated human action. No generic audit contract is implied.

Every newly submitted complete measurement set stores immutable effective percentage, owning company, source (`SYSTEM_DEFAULT` or `COMPANY_OVERRIDE`) and configuration revision. The server resolves current policy under the receiving write lock at submission; `capturedAt` does not select policy. Pending arrivals use policy current at measurement submission; remeasurement uses its new submission's policy and retains earlier sets. History displays stored snapshots, never today's recomputed setting. Configuration changes do not reinterpret pending decisions or historical measurements. Successful idempotent replay returns saved result before reading current policy.

Procurement and receiving-option lists default to page 1 with page size 25; maximum page size is 100. Responses include `totalCount` and `hasMore`; `search` also accepts legacy `q`. Procurement sort fields are `receivedAt`, `code`, and outward `status`; receiving-option sort fields are `collectionSiteName` and `receivingLocationCode`. Direction is `asc` or `desc`. Search/filter/count/order happen before paging. Outward status sort uses the current Ledger receiving projection (`COALESCE(receiving.status, procurement.status)`); the immutable Capture source status remains `COMMITTED`.

**States:** `PENDING_ARRIVAL` after registration; `AWAITING_MANAGER_REVIEW` after any surplus or over-threshold shortage; `REMEASUREMENT_REQUESTED` after manager request; `RECEIVED_MATCHED` after exact operator confirmation; `RECEIVED_WITHIN_TOLERANCE` after a complete positive operator set with at least one strict shortage and every line at or below expected and within its snapshotted threshold; `RECEIVED_SHORTAGE_APPROVED` after eligible manager approval; `BLOCKED_UNRESOLVED` after manager keeps discrepancy blocked. Remeasurement appends a new complete set; prior sets/reviews remain historical. Pending or blocked procurements expose zero usable stock.

`GET /api/ledger/receiving-tasks` returns `{items,page,pageSize,totalCount,hasMore}` and requires assigned `facilityId`; task/detail DTOs include owner/facility/location display names, expected and actual mass, status/version and measurement/review history, never farmer contact data. Detail is `GET /api/ledger/receiving/{id}`.

Operator measurement payload: `{operationId,expectedReceivingVersion,capturedAt,observations,items:[{procurementItemId,measuredValue,measuredUnit}]}`. Each procurement line occurs exactly once. Actual masses are positive exact decimal strings normalized to kg with no more than six fractional digits. Exact match posts actual as `RECEIVED_MATCHED`; a set with at least one strict shortage, every actual at or below expected, and every line within current effective threshold posts actual as `RECEIVED_WITHIN_TOLERANCE`; any surplus or over-threshold shortage leaves stock unavailable for manager review. Threshold snapshot is returned with measurement history.

Detail's `effectiveThreshold` is the current advisory `{companyId,percentage,source,revision}`. Each immutable `measurementSets[].threshold` uses that same shape for the policy actually applied; pre-feature sets return `null`, not a backfilled 2%. Measurement items contain `{procurementItemId,measuredValue,measuredUnit,actualKg}`; source expectations remain in the procurement lines. Missing or invalid required default configuration fails closed even when an override exists. Configuration changes never revalidate accepted historical packages against current percentages.

Manager review payload: `{operationId,expectedReceivingVersion,measurementSetId,action,reason,evidenceId}`. `APPROVE_SHORTAGE` requires all actual masses at or below expected and at least one strict shortage; it posts actuals only. `REQUEST_REMEASUREMENT` appends request; next complete set is added without erasing history. `KEEP_BLOCKED` leaves shortage/surplus blocked without stock. Reviews require assigned manager, nonempty reason and evidence scoped to procurement owner and facility. Blocked discrepancies may request remeasurement. Zero/full-loss actual is unsupported and rejected; surplus acceptance remains unsupported.

Receiving version starts at `0`, increments on each mutation; stale version or measurement-set binding returns `409`. Same operation ID, canonical payload and actor replays saved result even after later state changes; changed content returns `409`. Source-lot responses add `receivingStatus` and `usableStockKg`. Pending returns `currentStock: []` and `"0"`; accepted/historical `currentStock` entries contain `{locationId,onHandKg,availableKg}`, and usable stock sums `availableKg` so HOLD/restrictions remain effective. Missing historical positions still fail with `503`.

Migration `20261014190000_PhysicalReceiving` adds nullable `capture.procurements.receiving_workflow`: `NULL` marks existing historical committed procurement unchanged; new registrations use `PHYSICAL_ARRIVAL_V1`. Source status remains immutable `COMMITTED`; workflow projection supplies outward receiving status. Historical evidence and stock are grandfathered; migration does not retroactively freeze, rewrite or reinterpret legacy stock.

The OpenAPI document exposes registration request records. Example:
{
  "deviceId": "web-test-01",
  "clientOperationId": "44444444-4444-4444-8444-444444444444",
  "capturedAt": "2026-10-14T03:05:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "55555555-5555-4555-8555-555555555555",
    "farmerId": "6114dc9c-f08c-4b25-a90f-91c7c88a05b0",
    "collectionSiteId": "11111111-1111-4111-8111-111111111111",
    "receivingLocationId": "22222222-2222-4222-8222-222222222222",
    "commodityId": "33333333-3333-4333-8333-333333333333",
    "receivedAt": "2026-10-14T03:00:00Z",
    "items": [{
      "itemId": "66666666-6666-4666-8666-666666666666",
      "lineNo": 1,
      "plotSeasonId": "06d238e3-d388-4a91-b142-24b90b5dba25",
      "measuredValue": "600",
      "measuredUnit": "KG",
      "moisturePercent": "22.5",
      "lotId": "77777777-7777-4777-8777-777777777777",
      "sourceLotId": "88888888-8888-4888-8888-888888888888"
    }]
  }
}
```

The UUIDs here are illustrative; use actual IDs from the current environment. `collectionSiteId` is a Ledger facility UUID, and `receivingLocationId` is a Ledger receiving-location UUID. `commodityId` is the Catalog commodity UUID; Ledger resolves it through persisted Catalog-to-Ledger mapping. Supply `measuredValue` and `moisturePercent` as decimal strings. Supported mass units are KG, G, and T, provided they exist as MASS units in the catalog. The server rejects conversion that cannot fit `numeric(20,6)` kg exactly.

`IProcurementLedgerPort` uses the same open `NpgsqlConnection` and `NpgsqlTransaction` as Capture. Pending registration writes source records and receiving marker only; it creates no Ledger lot or posting. Accepted posting uses actual measurement in the shared transaction. Incomplete historical source lots without Ledger stock fail closed with `503 LEDGER_STOCK_UNAVAILABLE`; pending source lots return `currentStock: []` and zero usable stock instead. Receipt timestamps are normalized to UTC microsecond precision before hashing, snapshotting, and persistence.

Mill receiving UI exposes assigned tasks and actual measurements; purchaser UI registers source expectation only. Historical evidence is not converted, dropped, or inferred as arrival measurement.

Migration order: Identity → Catalog → Capture → Ledger. Review forward migrations, preserve generated migration history, back up, and obtain explicit shared database owner approval before migrations, seeds, grants, schema changes or data-changing verification. Use isolated PostgreSQL Testcontainers for write-based tests.


## Mixed-bulk stock and source attribution

When an enabled eligible Ledger location receives accepted stock, Capture procurement registration contract is unchanged: `POST /api/procurements` records expectation only and creates no pool/stock. Later source-lot reads preserve immutable source identity and include current stock positions. `ProcurementStockPosition` fields are `sourceLotId`, `locationId`, `currentStockLotId`, `currentStockLotCode`, `onHandKg`, `availableKg`, `poolTotalKg`, `isMixedBulk`, and nullable `poolRevision`. `onHandKg`/`availableKg` are this source's attributed shares; `poolTotalKg` is whole physical pool, not additive source stock. `ProcurementSummary.TotalNetMassKg` and original expected mass remain unchanged; remaining and available stock include current pool and descendant allocations without counting historical generations twice. `lineageLinks` link source lot to scoped current pool/descendant lot with `lotId`, `lotCode`, `locationId`, `isCurrentPool`, `poolRevision`, and `quantityKg`.

Ledger setup routes: `POST /api/ledger/locations` and versioned `PUT /api/ledger/locations/{id}` accept boolean `mixedBulk`, default false. `POST /api/ledger/mixed-bulk-policies` requires `id`, `locationId`, `ownerId`, `commodityId`, `stage`, `productIdentity`, `effectiveFrom`, `evidenceId`, `identityScopeConfirmed: true`; requires Administrator `platform.configure`. Policy scope is exact location/kind + identity tuple, has no default or seed, and is append-only. Kinds `STORE`, `SILO`, `COMPARTMENT` supported; `ISOLATION` is not.

Ledger mixed-bulk operations use `POST /api/ledger/transfers`, operation draft `POST /api/ledger/operations`, versioned draft `PUT /api/ledger/operations/{id}`, and commit `POST /api/ledger/operations/{id}/commit`; accepted arrival uses the existing receiving measurement/review routes above. Mixed operation input allocations and transfer requests bind `poolRevision`. Stale generation returns conflict; reload stock, reselect and save a new draft explicitly. Reads expose current-generation flags, source composition and custody links; composition is attribution, not separately available stock. Custody is exact integer micro-kg with largest-remainder allocation/UUID tie-breaks. HOLD follows positive custody and processing ancestry; new top-up cannot taint prior aliquot. Lab reports remain source-level `NOT_ASSESSED`, no automatic HOLD or stock effect. Ambiguous multi-position source allocation and mixed-custody correction/compensation fail closed. No reset/backfill/conversion or shared deployment; explicit owner approval before shared migrations, grants or seeds.

