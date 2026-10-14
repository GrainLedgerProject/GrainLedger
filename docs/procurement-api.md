# P3 online procurement API — Capture handoff

Capture owns `capture.procurements`, `capture.procurement_items`, and `capture.source_lots`.
The migration is `20261013180117_AddCaptureProcurements`. It has not been applied to the shared Supabase database by this change.

The API routes are:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/procurement-reference-data` | PADDY commodity, supported mass units, and eligible receiving options. |
| POST | `/api/procurements/commit` | Commit one receipt with one or more lines. |
| GET | `/api/procurements` | List receipts for the authenticated company. |
| GET | `/api/procurements/{id}` | Read the immutable receipt snapshot. |
| GET | `/api/source-lots/{id}` | Read source provenance and current Ledger stock. |

`POST /api/procurements/commit` uses the same operation envelope as other Capture writes. The client supplies UUIDs for the receipt, lines, lots, and source links. The server supplies company, actor, code, event, and kg conversion. A retry with the same operation ID and content returns the stored result. A changed payload with the same operation ID returns 409.

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

The UUIDs here are illustrative; use actual IDs from the current environment. Supply `measuredValue` and `moisturePercent` as decimal strings. Supported mass units are KG, G, and T, provided they exist as MASS units in the catalog. The server rejects a conversion that cannot fit `numeric(20,6)` kg exactly.

## Ledger integration required before production commit

`IProcurementLedgerPort` is the integration seam. The runtime currently registers `UnconfiguredProcurementLedgerPort`, which returns 503 `LEDGER_INTEGRATION_UNAVAILABLE`. This is intentional: no Capture row is committed unless Ledger has written every lot and initial stock posting. Tests inject a fake port to verify Capture logic and rollback; that fake is not production stock storage.

The Ledger teammate should implement and register the port. `PostReceiptAsync` receives the same open `NpgsqlConnection` and `NpgsqlTransaction` used by Capture. It must create exactly one RAW_PADDY lot and one positive initial posting per line, with the supplied IDs, owner company, commodity, receiving location, mass, and event link. It must not commit or dispose the transaction. On error, throw; Capture will roll back the whole receipt. `IsValidReceivingOptionAsync` and `ListReceivingOptionsAsync` must use the approved collection-site/receiving-location rules. `GetCurrentStockAsync` must return current stock for a lot owned by that company.

The present Ledger implementation still validates against `capture_mock`; it must be changed to use the three real Capture tables before registration. The cross-schema tenant FK from `capture.source_lots.(company_id,lot_id)` to Ledger lot is intentionally pending because the target Ledger schema is owned by that teammate. Do not create a second lot or stock table in Capture.

Once the adapter and FK are ready, apply migrations in the agreed order on the shared database, then test one multi-line receipt, retry, concurrent retry, and a forced Ledger failure. Do not use the test adapter or treat 503 as a successful receipt.
