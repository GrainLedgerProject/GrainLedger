# Container preparation API and integration boundary

Implementation branch: `feat/container`, based on `feat/trace` at `f399aa7`. Implements container preparation and loading drafts while the order/approved blend/reservation workflow is delivered separately. **There is no inventory withdrawal, reservation consumption, successful CLOSE or dossier generation in this release.** Commit and close refuse on the backend, including direct HTTP requests.

See [implementation plan](container-loading-implementation-plan.md) for the full target and remaining gates. Migration `20261021000000_ContainerDrafts` adds Ledger-owned container/draft/audit/idempotency tables and the protected `ledger.container_action(uuid,text,jsonb)` entry routine. Normal startup does not install it. Missing feature migration returns 503 at the feature endpoint; deployment must use the approved owner migration/runtime-grant workflow, never an elevated runtime login. After the user explicitly approved deployment, this migration and the runtime grants were applied to the configured database; read-only postflight passed. See section 15 of the implementation plan. No seed or data-changing smoke was run there.

## Roles and scope

- ExportStaff with persisted `containers.write`: create preparation, save metadata and drafts, read and check own-company containers. Company/actor derive from persisted identity; request-supplied authority fields are rejected.
- CustomerManager: read/check own-company containers; no create/edit/commit/CLOSE.
- MillOperator/MillManager: read/check assigned-facility containers; no customer commercial mutation. `containers.operate` is not treated as customer approval.
- Administrator and PurchasingStaff: no container business access. Missing and out-of-scope container IDs both return 403. Role revocation applies on each request, including idempotent retries.

Cookie writes use the existing CSRF filter; bearer follows existing authentication. Responses use the Ledger no-store filter. Runtime can execute the finite entry routine and read Ledger tables under the repository contract; it cannot directly modify the new Ledger tables or execute the private JSON helper.

## Routes

All routes below are under `/api/ledger/containers`.

| Method/path | Behavior |
| --- | --- |
| GET `/` | Scoped list: `page`, `pageSize` (default 25, max 100), `search` by internal/container number. Returns the existing `items/page/pageSize/totalCount/hasMore` shape. |
| POST `/` | Create container preparation; returns the detail snapshot, HTTP 200. |
| GET `/{id}` | Detail with saved drafts; committed loaded kg is explicitly zero. |
| POST `/{id}/metadata` | Save optional order reference/container number/seal; version checked, audit appended. |
| POST `/{id}/loading-draft` | Create or replace one draft; version checked, exact lines and prior/new snapshots retained in audit. |
| POST `/{id}/loading-check` | Fresh scoped findings for a saved draft; no stock write or retained PASS. |
| POST `/{id}/close-check` | Closure readiness; drafts never count toward target. |
| POST `/{id}/commit-loading` | HTTP 409, `ready:false`, dependency findings. Cannot withdraw stock. |
| POST `/{id}/close` | HTTP 409, `ready:false`, dependency findings. Cannot create CLOSED/dossier. |
| GET `/{id}/history` | Scoped paged audit (default 25, max 100); create/metadata/draft edits, actor and recorded time. |

No arbitrary sort fields, owner overrides, approval flags, assessment IDs or actor/confirmer IDs are accepted. Loading drafts are capped at 100 per container and 100 lines per draft to bound preparation detail payloads. They are alternative preparations, not cumulative shipments, claims on stock or proof of physical loading. Do not use this workflow to reconcile goods already loaded physically.

## Write examples

UUIDs below are illustrative. Generate stable IDs once per intended operation and retain the exact request if the network result is uncertain.

Create preparation:

```json
{
  "id": "11111111-1111-4111-8111-111111111111",
  "requestId": "22222222-2222-4222-8222-222222222222",
  "code": "BOX-01",
  "facilityId": "33333333-3333-4333-8333-333333333333",
  "targetKg": "25000.000000"
}
```

The authenticated ExportStaff owns the preparation. Facility must exist and be active. Internal code is trimmed, 1–80 characters and unique within owner. Target must be positive, decimal string, up to 14 integer/6 fractional digits. Facility and target are fixed after create in this release; target revisions/cancellation need the agreed order integration policy.

Metadata (`POST /{id}/metadata`): `requestId`, current detail `version`, `orderId` (optional UUID/null), `containerNumber`, `sealNumber`. Blank number/seal becomes null; nonblank values max 80 characters. Omitted metadata fields clear their prior value, so callers must submit the complete metadata form. `orderId` is an **unverified external reference**, not a foreign key to a nonexistent order table. Saving it does not grant loading authority. Number/seal format checks beyond trimming/length require the team policy; no invented check digit or global reuse rule.

Draft (`POST /{id}/loading-draft`):

```json
{
  "requestId": "44444444-4444-4444-8444-444444444444",
  "version": "55555555-5555-4555-8555-555555555555",
  "draftId": "66666666-6666-4666-8666-666666666666",
  "loadedAt": "2026-10-20T15:00:00Z",
  "lines": [
    {
      "lotId": "77777777-7777-4777-8777-777777777777",
      "locationId": "88888888-8888-4888-8888-888888888888",
      "quantityKg": "10000.000001"
    }
  ]
}
```

`loadedAt` is a planned/prepared time in a DRAFT, not a committed physical observation. UI labels UTC explicitly. Every selected lot must have a recorded position in the container facility and belong to the container owner. Source location/facility must be active. Positive kg are normalized to six decimals; no JS floating-point mass accounting. Server sums lines and rejects draft total above target. Draft save does not certify product/specification, current availability or export eligibility; precheck reports current physical shortages/restrictions and all unconnected authority gates. Duplicate lot/location lines are summed before shortage checks.

`version` belongs to the aggregate container: any metadata/draft mutation issues a new UUID version. Concurrent same-version writes have one winner; reload on 409. A stable `(actor, requestId)` with exactly the same action/JSON payload replays its stored result without audit duplication; altered payload with that requestId conflicts. Old-company result replay is denied after an actor changes company. Replay is a write receipt, not a fresh detail: reload when needed. SQL mutation locks use existing `ledger.lock_writes()` and do not commit the caller transaction.

## Readiness and explicit refusal

Loading check accepts `draftId`; close check accepts `{}`. Commit-loading requires the route container, saved `draftId`, current `version` and `requestId`; close requires current `version` and `requestId`. Unknown fields are rejected, including attempts to supply `approved:true`/`ready:true`.

Check/refusal results contain `ready:false`, `complete:false`, container/version, `draftKg`, `committedLoadedKg`, `targetKg` and `findings[]` with code/message/optional lotId. They contain these unconditional integration gates:

- `ORDER_PLAN_AUTHORITY_UNAVAILABLE`: no current verified order, approved plan/review scope or actual output association.
- `OUTPUT_RESERVATION_UNAVAILABLE`: no real output-stock reservation/consumption contract.
- `EXPORT_ASSESSMENT_UNAVAILABLE`: no current LOADING/CLOSE assessment/freshness contract.
- `CONTAINER_RESTRICTIONS_UNAVAILABLE`: no container-level HOLD/risk provider.

Other findings: `ORDER_REFERENCE_MISSING`, `SEAL_METADATA_MISSING`, `INSUFFICIENT_PHYSICAL_STOCK`, `LOT_RESTRICTED`, `EXACT_TARGET_NOT_LOADED`. Metadata findings do not invent a rule that sealing is required before all loading; successful closure will require it. The response is deliberately incomplete with respect to full export policy; never present it as an exhaustive PASS-capable assessment.

Until integrations exist, every container remains OPEN, every loading remains a draft, committedLoadedKg is `0.000000`, and no dossier endpoint claims a successful closing snapshot. SQL contains no stock posting call or CLOSED update; disabling UI buttons is additional guidance, not the enforcement boundary.

## Handover required from order/blend team

The future integration must supply persisted, same-company/container-scoped references for:

1. Actual order/market/commodity/specification version, order required mass and this container target.
2. Approved plan version, current review and authorized ALL_REMAINING/OUTPUT_ONLY scope; actual output IDs and positions. A bare `plan.status=APPROVED` or client-supplied ID is insufficient.
3. Matching OUTPUT reservation, exact outstanding quota and an atomic consume operation sharing the caller connection/READ COMMITTED transaction/advisory lock. All existing stock writers must honor reservations first.
4. Current LOADING/CLOSE assessment, immutable basis and evidence/spec/MRL/margin/quality revision fence; container/lot HOLD and risk restrictions. Lab transcription confirmation alone cannot satisfy this.
5. Whole-container CLOSE across prior plan versions and order-cap checks; immutable dossier persistence in the same transaction.

These are requirements for a future concrete in-process port and SQL migration, **not implemented fake providers or trusted request flags**. Define DTOs jointly with the owning team when its model is ready. Replacing the refusal gate requires backend accounting/concurrency/freshness tests, including every affected stock writer, before enabling the UI actions. Do not create a public "approve" endpoint in this feature or populate placeholder approvals in production.

## Test on the web

After an approved migration/runtime-grant deployment, sign in as Export Staff → Ledger → Containers. Create preparation, select facility and exact target, save number/seal/order reference if available. Choose a lot/position, enter kg on one or more lines and save. Check loading/closure and inspect dependency findings/history. Refresh and reopen to verify persistence. Commit/close remain disabled; direct calls return 409. Customer Manager can inspect/check without edit controls.

For development verification use `BrowserWorkflowHost` and Testcontainers: do not create synthetic containers on shared Supabase. Current regression commands:

```bash
dotnet test backend/tests/GrainLedger.Api.IntegrationTests --filter 'FullyQualifiedName~ContainerTests|FullyQualifiedName~LotTraceabilityTests|FullyQualifiedName~CaptureCutoverMigrationTests'
npm test
npm run build
```
