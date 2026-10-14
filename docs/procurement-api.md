# P3 online procurement API — Capture handoff

Capture owns `capture.procurements`, `capture.procurement_items`, and `capture.source_lots`. The route contract below describes official Capture; it has no arrival-observation or receipt-correction endpoints. No synthetic-to-official conversion is automatic, and existing Ledger stock/history remains unchanged.

Forward Ledger migration `20261014010000_UseOfficialCaptureBoundary` adds protected Capture/Ledger integration, Catalog commodity mapping, and package validation. Preserve generated migration history and existing Ledger stock. Shared database migration requires explicit owner approval; documentation owner applied no migration.

The API routes are:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/procurement-reference-data` | PADDY commodity, supported mass units, and eligible receiving options. |
| POST | `/api/procurements/commit` | Commit one receipt with one or more lines. |
| GET | `/api/procurements` | List receipts for the authenticated company. |
| GET | `/api/procurements/{id}` | Read the immutable receipt snapshot. |
| GET | `/api/source-lots/{id}` | Read source provenance and current Ledger stock. |

`POST /api/procurements/commit` uses the Capture operation envelope. Client supplies UUIDs for receipt (`entityId`), lines (`itemId`), lots and source links. Server derives company, actor, code, event and kg conversion. A retry with same operation ID and same content returns prior result; changed content with same ID returns 409.

The OpenAPI document exposes the request records. Example:

```json
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
`IProcurementLedgerPort` is registered by the Ledger module. It receives the same open `NpgsqlConnection` and `NpgsqlTransaction` as Capture, uses authoritative Capture provenance, resolves Catalog commodity IDs through persisted Catalog-to-Ledger mapping, and posts stock without committing or disposing the caller transaction. Receiving options use facility UUID as `collectionSiteId` and receiving-location UUID. Source-lot `currentStock[].onHandKg` values are invariant decimal strings, not JSON numbers. Incomplete historical Capture source lots without Ledger stock fail closed with 503 `LEDGER_STOCK_UNAVAILABLE`; they are not treated as zero stock. Receipt timestamps are normalized to UTC microsecond precision before hashing, snapshotting, and persistence; kilogram quantities retain exact six-place precision.

Official Capture does not expose APIs for arrival observation, receiving discrepancy correction/investigation, or procurement receipt correction. Do not infer those endpoints from Ledger history or synthetic legacy records. Existing historical source records and Ledger stock remain; no conversion into official Capture records is performed automatically.

Migration order: Identity → Catalog → Capture → Ledger. Review forward migrations, preserve generated migration history, back up, and obtain explicit shared database owner approval before migrations, seeds, grants, schema changes or data-changing verification. Use isolated PostgreSQL Testcontainers for write-based tests.

