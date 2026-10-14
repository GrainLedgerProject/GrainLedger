# P3 online procurement API — Capture handoff

Capture owns procurement source records; Ledger owns accepted stock and positions. `POST /api/procurements` registers immutable source expectation only: no lot, position, posting or usable stock is created. One procurement represents one complete arrival; create separate procurement per load. There is no truck model, customer settlement workflow, tolerance, partial arrival or surplus acceptance.

Assigned MillOperator records one complete actual measurement set covering every line. Exact normalized match confirms arrival and posts measured actual mass. Any mismatch remains pending with no usable stock. Assigned MillManager resolves mismatch with required reason and evidence scoped to procurement owner and assigned facility: `APPROVE_SHORTAGE` posts actual mass only if all actuals are at or below expected and at least one is short; `REQUEST_REMEASUREMENT` appends request and subsequent observations; `KEEP_BLOCKED` leaves unexplained shortage/surplus without stock.

Existing historical procurement evidence and Ledger stock are grandfathered unchanged. Migration `20261014190000_PhysicalReceiving` applies workflow rules only to new records; it does not retroactively freeze or reinterpret legacy stock. `receiving_workflow IS NULL` identifies legacy committed procurement; new registration uses `PHYSICAL_ARRIVAL_V1`. Legacy/synthetic evidence is historical only; no automatic conversion or deletion.

Forward Ledger migration `20261014010000_UseOfficialCaptureBoundary` adds protected Capture/Ledger integration and package validation. Preserve generated migration history and existing stock. Shared database migration requires explicit owner approval; documentation owner applied no migration.

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

Procurement and receiving-option lists default to page 1 with page size 25; maximum page size is 100. Responses include `totalCount` and `hasMore`; `search` also accepts legacy `q`. Procurement sort fields are `receivedAt`, `code`, and outward `status`; receiving-option sort fields are `collectionSiteName` and `receivingLocationCode`. Direction is `asc` or `desc`. Search/filter/count/order happen before paging. Outward status sort uses the current Ledger receiving projection (`COALESCE(receiving.status, procurement.status)`); the immutable Capture source status remains `COMMITTED`.

**States:** `PENDING_ARRIVAL` after registration; `AWAITING_MANAGER_REVIEW` after any mismatch; `REMEASUREMENT_REQUESTED` after manager request; `RECEIVED_MATCHED` after exact operator confirmation; `RECEIVED_SHORTAGE_APPROVED` after eligible manager approval; `BLOCKED_UNRESOLVED` after manager keeps discrepancy blocked. Remeasurement appends a new complete set; prior sets/reviews remain historical. Pending or blocked procurements expose zero usable stock.

`GET /api/ledger/receiving-tasks` returns `{items,page,pageSize,hasMore}` and requires assigned `facilityId`; task/detail DTOs include owner/facility/location display names, expected and actual mass, status/version and measurement/review history, never farmer contact data. Detail is `GET /api/ledger/receiving/{id}`.

Operator measurement payload: `{operationId,expectedReceivingVersion,capturedAt,observations,items:[{procurementItemId,measuredValue,measuredUnit}]}`. Each procurement line occurs exactly once. Actual masses are positive exact decimal strings normalized to kg with no more than six fractional digits. Exact match posts actual; mismatch leaves stock unavailable.

Manager review payload: `{operationId,expectedReceivingVersion,measurementSetId,action,reason,evidenceId}`. `APPROVE_SHORTAGE` requires all actual masses at or below expected and at least one strict shortage; it posts actuals only. `REQUEST_REMEASUREMENT` appends request; next complete set is added without erasing history. `KEEP_BLOCKED` leaves shortage/surplus blocked without stock. Reviews require assigned manager, nonempty reason and evidence scoped to procurement owner and facility. Blocked discrepancies may request remeasurement. Zero/full-loss actual is unsupported and rejected; no surplus approval.

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


