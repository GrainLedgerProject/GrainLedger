# Export orders (F13)

The running web application uses `apps/web/src/ExportOrders.tsx` and the Exports module. ExportStaff creates and edits own-company orders, specifications and planned container targets. CustomerManager reads own-company orders. F13 does not select stock, evaluate compliance, reserve mass or approve a blend plan.

The order detail pages historical specification versions, container targets and audit events. History includes the actor name and a request snapshot; the UI summarizes the changed fields. Quality bounds retain up to 12 decimal places in API responses; mass remains limited to six places in kilograms. After an ambiguous network, server or response-reading failure, the web form stores the exact request and operation ID in session storage for the current user and only offers a replay of that request. This includes invalid JSON, an empty create response, a create response without an order ID, and HTTP 408/5xx. A successful replay or a definitive API rejection clears it. Mass selectors load configured, active MASS units supported by the export conversion contract (KG, G and T).

## HTTP contract

All writes require the existing browser CSRF token or Bearer authentication. The actor and company come from active persisted Identity records. `operationId` is a UUID stable across retries of the **same** request. Reusing it with a different request returns 409. The server stores the original response and ETag in the same transaction as the write.

| Route | Behavior |
| --- | --- |
| `GET /api/export-availability` | `{ready: boolean}` after schema and restricted-runtime grant checks. The web navigation appears only when ready. |
| `GET /api/export-orders` | Own-company list. `q`, `marketId`, `commodityId`, `from`, `to`, `sortBy`, `sortDirection`, `page`, `pageSize`. |
| `POST /api/export-orders` | Atomically creates header, specification v1, initial container targets, event and retry record. |
| `GET /api/export-orders/{id}` | Own-company header, current specification, allocation totals and ETag (`"1"`, `"2"`, …). Container rows are read through the paged endpoint. |
| `PATCH /api/export-orders/{id}` | Updates header before a downstream plan exists; requires `If-Match` and `operationId`. |
| `GET/POST /api/export-orders/{id}/specifications` | Read immutable versions with `page` and `pageSize` / add a new version with `If-Match` and `operationId`. GET returns the standard page envelope. |
| `GET/POST /api/export-orders/{id}/containers` | Paged targets / add targets with `If-Match` and `operationId`. |
| `GET /api/export-containers/{id}` | Own-company target and ETag. |
| `PATCH /api/export-containers/{id}` | Edit a PLANNED target with `If-Match` and `operationId`. |
| `GET /api/export-orders/{id}/history` | Paged own-company audit events with actor name and immutable request snapshot. |

Example create body:

```json
{
  "operationId": "68630989-79f8-4118-9ef4-c5080207be63",
  "marketId": "40ce386f-8388-407f-8134-ce79232c5869",
  "commodityId": "f83662c4-ef87-4f84-982c-eed50c69e0ba",
  "requiredMass": "48",
  "unit": "T",
  "shippingFrom": "2026-11-10",
  "shippingTo": "2026-11-20",
  "limits": [{ "attributeId": "4a408eb3-24b4-4500-8a8b-056b9f8c74af", "minValue": null, "maxValue": "14" }],
  "containers": [{ "targetMass": "24", "unit": "T" }, { "targetMass": "24", "unit": "T" }]
}
```

Mass inputs are decimal strings in configured KG/G/T. Normalized kg uses `numeric(20,6)` and rejects rounding. Quality bounds use `numeric(24,12)`. Shipping dates are calendar dates, not timestamps. Sum of targets may be less than or equal to required mass. New order codes use `EXP-YYMMDD-NNNNNN` (for example, `EXP-261020-000001`); container codes use `CTN-YYYYMMDD-NNNNNN`. Both are issued on the backend using Asia/Ho_Chi_Minh time, with separate per-company counters that never reset. Existing order codes retain their original format. UUIDs remain the API identifiers.

Validation returns field paths such as `specification.limits[0].maxValue` and `containers[1].targetMass`. A stale ETag returns 412; duplicate operation ID with changed payload returns 409. Read of another company's resource returns 404. The [F14 planning backend](f14-blend-planning.md) does not lock F13 edits: when header, specification or target changes, an older draft remains in history but its `demandCurrent` becomes false. Creating a new draft requires current parent versions. An F14 proposal is a reviewed planning snapshot; it does not reserve stock or authorize execution.

## Migration and deployment

The forward-only migration is `GrainLedger.Modules.Exports/Migrations/20261019000000_InitialExports.cs`. It passed isolated PostgreSQL tests and was applied to the configured shared database on 19 October 2026 at the user's request. Restricted runtime grants were configured separately and verified with a read-only privilege query; a new API process passed runtime validation. Normal API startup does not migrate. The existing API `--migrate` command does not include Exports.

For another environment, an owner reviews the migration and applies it with the Exports design-time factory using `ConnectionStrings__GrainLedgerMigration`. Configure the restricted runtime role separately after that migration, then deploy/restart the new API. Availability stays false until schema and runtime grants are present. Do not use the runtime connection to migrate, and do not seed real shared data for a smoke check.

For a separately approved deployment in another environment, use the owner connection in the current PowerShell session and run from the repository root:

```powershell
$env:ConnectionStrings__GrainLedgerMigration = '<approved owner connection>'
dotnet ef database update 20261019000000_InitialExports --project backend/src/GrainLedger.Modules.Exports --startup-project backend/src/GrainLedger.Modules.Exports --context ExportDbContext
dotnet run --project backend/src/GrainLedger.Api -- --configure-runtime-role
```

The second command only configures the restricted runtime role; it does not apply other modules' migrations. Neither command is part of normal API startup.

Verification on an isolated database: `dotnet test GrainLedger.slnx --filter FullyQualifiedName~ExportOrderTests`; frontend: `npm exec --workspace @grainledger/web vitest run src/ExportOrders.test.tsx` and `npm run build`.

For browser acceptance after deployment, sign in as ExportStaff and open Export orders. Create an order with two container targets, reopen it, edit the header, add a specification version, select the older version, add a target within the remaining mass, and verify an over-allocation is rejected at the relevant input. Page through targets and history at desktop and 375–390 px viewport widths. Disconnect the network immediately after submitting a create or edit request, return to the form, and retry the saved request; the operation must produce only one order/change. Compare Ledger positions and postings before and after the F13 actions. Use an isolated environment or approved disposable records; do not create test orders on the shared database without separate approval.

The isolated browser acceptance completed on 20 October 2026. See [F13 acceptance results](f13-acceptance.md) for the scenarios, database checks and repeatable test-host setup.
