# P4: Sample and handover API

The Purchasing Staff records a physical sample against one accepted source lot. A sample can exist before its first handover. Handovers are appended as they occur. These records do not create a lab result, change stock, or establish MRL compliance.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/samples` | Record a sample. Requires `purchases.write`. |
| GET | `/api/samples?sourceLotId={id}&page=1&pageSize=20` | List company samples. Requires `purchases.write` or `farms.read`. |
| GET | `/api/samples/{id}` | Read sample details and current handover count. Same read permissions. |
| POST | `/api/samples/{id}/handovers` | Append a handover. Requires `purchases.write`. |
| GET | `/api/samples/{id}/handovers?page=1&pageSize=20` | Read handover history. Same read permissions. |

All requests are scoped to the authenticated customer company. `sourceLotId` is the ID of `capture.source_lots`, not the Ledger `lotId`. The source must have a committed procurement and its acceptance event. The app creates `entityId`, `clientOperationId`, `deviceId`, and `capturedAt`. The web form also generates a sample code in the form `SMP-YYYYMMDD-<16 hex characters>` from the local collection date and sample ID. The employee records the code printed on the physical seal. The generated sample code must be written or printed on the sample bag.

## Create sample

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "ad5182d9-2188-45b2-bb52-9d1ab8e5bd30",
  "capturedAt": "2026-10-14T03:05:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "71f17c62-99c8-4f65-9016-6bf889095251",
    "sourceLotId": "88888888-8888-4888-8888-888888888888",
    "code": "SAMPLE-001",
    "sealCode": "SEAL-001",
    "collectedAt": "2026-10-14T03:00:00Z"
  }
}
```

Replace the UUIDs with new IDs and an accepted source lot in the current environment. The sample code must be unique per company. The seal code is required but is not unique by database rule. The server records `collectedBy` from the authenticated user. A successful POST returns 201, a `Location` header, the sample, and `handoverCount: 0`.

## Record handover

```json
{
  "deviceId": "web-test-01",
  "clientOperationId": "5620b24b-6fa6-45f0-af3e-c339205512c0",
  "capturedAt": "2026-10-14T07:05:00Z",
  "payloadSchemaVersion": 1,
  "payload": {
    "entityId": "9be9e71d-243d-4942-a95c-ed6db9dd0107",
    "fromParty": "Collection agent",
    "toParty": "Laboratory",
    "handedOverAt": "2026-10-14T07:00:00Z",
    "sealCondition": "Intact",
    "evidenceRef": null
  }
}
```

Send this body to `POST /api/samples/{sampleId}/handovers`. `recordedBy` comes from the authenticated user. The handover time cannot precede collection or follow capture time. A successful POST returns 201; later handovers are separate records.

Each POST stores the entity, a business event, and an offline operation in one transaction. Retrying the same device, operation ID, and content returns the prior result with 200. Reusing the operation ID with different content returns 409 `OPERATION_CONFLICT`. A source lot or parent sample that has not been accepted returns 409 `SOURCE_RECORD_NOT_READY`. A duplicate sample code returns 409 `SAMPLE_CODE_EXISTS`. GET on a sample outside the company returns 404.

The Capture migration `20261014044446_AddCaptureSamplesAndHandovers` creates `capture.samples` and `capture.sample_handovers` with the company-scoped source and sample references. It depends on the earlier P3 `capture.source_lots` table. On the shared Supabase database P3 remains pending, so do not apply P4 alone. The P4 implementation can be tested with PostgreSQL Testcontainers after the local Docker Engine is running.

```powershell
dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj -c Release --filter FullyQualifiedName~SampleTests
```
