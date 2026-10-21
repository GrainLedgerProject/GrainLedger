# Mixed bulk-storage pools — implementation plan

Status: Mixed-bulk storage implemented and verified locally on 2026-10-20 through isolated PostgreSQL, authenticated HTTP and the working browser. Final check results are recorded below. Not deployed to shared Supabase or production. Separately approved business-data cleanup completed on 2026-10-20 after private backup, isolated restore/cleanup and rollback verification. No reset/backfill/conversion is part of this implementation.

## Approach

Use explicitly enabled mixed-bulk locations with one current pool generation per physical contents. Accepted arrivals and other eligible ingress consume incoming stock plus existing pool's **remaining** balance into a new immutable lot; partial withdrawals split off an aliquot and retain an exact-source remainder. Reuse protected postings, scope checks, exact quantities and write serialization; never rewrite procurement sources or historical lot composition.

### Scope

- **In:** location designation; accepted arrival pooling; incoming/outgoing transfers; processing withdrawals and output ingress; stale drafts; stock/source projections; sample/report links; effective HOLDs; correction safety; history; isolated verification.
- **Out:** procurement registration changes, historical-stock backfill/conversion, migration squashing, cross-company mixing, ownership transfer, inferred loss allowances, residue/compliance assessment, automatic lab-result disposition, HOLD release, export/container implementation, new services/caches/dependencies.
- Business data was cleared by separately approved maintenance on 2026-10-20: 41 business tables verified empty; accounts, organizations, catalogs, facility setup, configuration and migration history retained, including evidence supporting retained policies. No historical-stock backfill/conversion is required for that cleared state. Preserve schema contracts; do not add a reset or conversion job to deployment.
- Ordinary non-mixed receiving, transfers and processing stay unchanged. Pool-derived stock retains its restrictions after leaving mixed storage; unrelated non-pooled stock does not acquire new pooling semantics.

## Proposal basis

Source: [GrainLedger_V2.pdf](GrainLedger_V2.pdf), especially pp. 3–6 and 9.

- p. 6 explicitly describes multiple farmers' stock combined in one silo **for one customer**, never across customers. Example starts with contributions of 50, 30 and 20 tonnes.
- pp. 4–5 demonstrate immutable transformations retaining proportional origin; later withdrawals reduce remaining balance, and a bad source can be traced into downstream stock.
- pp. 3–4 and 9 require exact mass, no overspending, transactional rejection, append-only corrections and storage-level enforcement.
- Proposal's 0.5% tolerance and quantities are illustrative, not production policy. Its example creates stock at procurement; current GrainLedger deliberately creates stock only at accepted arrival. Keep current registration/receiving boundary.
- Proposal does not define automatic top-up mechanics, detailed compatibility, empty-silo clearance, or correction/HOLD cascade authority. Local contracts below implement bounded behavior only; they do not settle these business rules beyond explicit evidence-backed policy.

## Implemented contracts and preserved boundaries

| Existing contract | Local implementation |
| --- | --- |
| Accepted arrival preserves source receipt package and original lot. | Mixed-bulk ingress runs atomically after accepted receipt; source identity remains intact and source position is consumed only at selected location/quantity. |
| Lot contributions do not represent live source balance; independent rounding can drift. | Successor pool generations and withdrawal allocations use integer micro-kg, largest remainder and UUID tie-break; remainder is exact source-wise complement. |
| Ordinary transfer keeps same lot identity; source can have multiple positions. | Ordinary-to-ordinary transfer unchanged. Mixed ingress consumes only selected position; ambiguous multiposition source residual is rejected. |
| Procurement summary expected mass differs from live available stock. | Source response keeps expected total and exposes attributed remaining/available shares separately from entire `poolTotalKg`; `CaptureSourceUUID` remains original source, never Ledger root. |
| Samples and reports refer to Capture source. | Scoped lineage links current pools/descendants without reassignment or pool-level test claims; reports stay `NOT_ASSESSED`. |
| HOLD and correction behavior must retain source ancestry and avoid automatic unwinding. | Positive ancestry drives HOLD through custody/processing; top-up cannot taint earlier aliquot. Mixed-custody correction, unmix and downstream cascade fail closed. |
| Shared write lock and `READ COMMITTED` protect stock transactions. | Mixed writes reuse caller transaction and advisory lock `714928352`; no per-location lock redesign. |

See [architecture](architecture.md#mixed-bulk-storage-implemented-locally-not-deployed), [procurement API](procurement-api.md#mixed-bulk-stock-and-source-attribution), and [lot-code guide](unified-lot-code-guide.md#mixed-bulk-custody-generations) for current route/read contracts.

## Implemented accounting and contract

One current generation/revision per opted-in location. Policy exact match required across current location kind, owner, commodity, stage, product identity, effective time and evidence. Arrival/transfer/processing ingress and withdrawal are atomic; source-share data represents attribution only. Draft/transfer writes carry current revision and reject stale generation. HOLD follows positive contribution ancestry through custody and processing. Full drain retains identity until explicit clearance.

This implementation does not add factory default policies, infer compatibility attributes, convert historical records, or broaden correction authority. See [Policy and safety boundaries](#policy-and-safety-boundaries).

## Route and read contract

`POST /api/ledger/locations` and versioned `PUT /api/ledger/locations/{id}` accept `mixedBulk` boolean (default false). `POST /api/ledger/mixed-bulk-policies` requires `id`, `locationId`, `ownerId`, `commodityId`, `stage`, `productIdentity`, `effectiveFrom`, `evidenceId`, and `identityScopeConfirmed: true`. `GET /api/ledger/setup` returns `mixedBulkPolicies`; location reads include `mixedBulk`, `currentPoolLotId`, and authorized `poolRevision`.

Ledger stock/detail reads include `mixedBulk`, `poolLocationId`, `poolRevision`, `isCurrentPool`, `custodyDerived`, `sourceComposition`, and `custodyLinks`. Composition is source attribution, never a second stock balance. Transfer input carries `poolRevision`; operation input allocation carries per-allocation `poolRevision`. Stale inputs reject and require reload/reselect/resave. Procurement stock projection and source lineage fields are documented in [procurement API](procurement-api.md#mixed-bulk-stock-and-source-attribution).

Private custody tables, protected helpers and runtime grants are defined by the new forward migration. Direct runtime mutation remains denied. No legacy SQL/migration edits.

## Ordered implementation checklist

1. [x] **Activation contract**: opt-in location flag defaults false; eligible kinds are `STORE`, `SILO`, `COMPARTMENT` (not `ISOLATION`); exact evidence-backed location-kind/owner/commodity/stage/product/effective policy required with `identityScopeConfirmed: true`; no default policy.
2. [x] **Forward migration and protected SQL**: `20261020000000_MixedBulkStorage` loads `MixedBulkStorage.sql`, `MixedBulkCommands.sql`, `MixedBulkSafety.sql`, `MixedBulkReads.sql`. Historical migration/SQL untouched; no reset/backfill/conversion.
3. [x] **Custody ingress/split accounting**: new current generations/revisions, exact micro-kg largest-remainder aliquot and source-wise complement, immutable custody allocation/events and command-bound idempotency.
4. [x] **Command integration**: accepted arrivals, selected-position transfer, processing input/output and supported ingress use protected custody path; procurement registration payload/source expectation unchanged. Unsupported ambiguous multiposition attribution fails closed.
5. [x] **Restrictions/history/correction safety**: positive-source HOLD follows custody/processing ancestry, late top-up does not retroactively affect earlier aliquot; generation-bound stale drafts fail closed; no mixed-custody correction/unmix/downstream cascade.
6. [x] **Read contracts and web callers**: setup, stock, picker, procurement source summary and history expose current generation/revision and source attribution; lab remains source-level `NOT_ASSESSED`.
7. [x] **Complete isolated verification**: solution build, 330 backend tests, 112 web tests, frontend production build and authenticated HTTP/browser scenarios passed. This does not establish factory-policy approval or shared-deployment readiness.

### Affected files and contracts

Paths below identify existing integration points, not permission to rewrite historical migration scripts.

| Area | Files/contracts |
| --- | --- |
| Ledger persistence/commands | `backend/src/GrainLedger.Modules.Ledger/`: migration `20261020000000_MixedBulkStorage`; embedded `Sql/MixedBulkStorage.sql`, `MixedBulkCommands.sql`, `MixedBulkSafety.sql`, `MixedBulkReads.sql`; endpoint/service and read integration. |
| Capture integration | `backend/src/GrainLedger.Modules.Capture/ProcurementLedgerPort.cs`: `IProcurementLedgerPort`, `ProcurementStockPosition`; Ledger `ProcurementLedgerPort.cs` implementation; Capture `ProcurementEndpoints.cs`: `SourceLotResponse`/stock summaries. Preserve `ProcurementSummary`, registration/source persistence, `SampleEndpoints.cs` and `LabReportEndpoints.cs` identity contracts; touch lab read projections only as needed. |
| Working web | `apps/web/src/LedgerSetup.tsx`, `LedgerStock.tsx`, `LedgerReceiving.tsx`, `LedgerOperations.tsx`, `LedgerLotPicker.tsx`, `ledger-ui.tsx` (`Lot`, `Operation`, lookup shapes), `Procurement.tsx`, `procurementTypes.ts`; `Samples.tsx`/`LabReports.tsx` only for scoped lineage links. No prototype/mobile implementation. |
| Verification | `backend/tests/GrainLedger.Api.IntegrationTests/`: existing `ApiFactory.cs`, `BrowserWorkflowHost.cs`; focused cases alongside `ReceivingTests.cs`, `ReceivingThresholdTests.cs`, `LedgerAcceptanceTests.cs`, `LedgerSafetyTests.cs`, `LedgerSmokeTests.cs`, `LabReportTests.cs`; existing affected web behavioral tests. |
| Documentation | `architecture.md`, `ledger-implementation-plan.md`, `procurement-api.md`, `unified-lot-code-guide.md`, `lab-flow-guide.md`, `setup-and-deployment.md`, and this plan. Historical proposal and deployed-schema snapshots remain untouched. |

## Acceptance criteria

| Scenario | Required result |
| --- | --- |
| Opt-in and ordinary storage | Non-mixed arrivals retain separate source lots; ordinary transfer retains lot ID. Only explicitly enabled eligible location pools. Cross-owner, wrong stage/commodity/product, missing policy and unauthorized facility/configuration requests fail server-side. |
| Proposal baseline | Accepted arrivals of 50,000 / 30,000 / 20,000 kg yield one 100,000 kg pool with those exact root shares. Original source records, receipt events and sample subjects remain independently readable. Registration alone still creates no stock. |
| Remaining balance, not lifetime receipts | Withdraw 40,000 kg from baseline: aliquot shares 20,000 / 12,000 / 8,000; remainder 30,000 / 18,000 / 12,000. Add new source D of 40,000 kg: current 100,000 kg pool has 30,000 / 18,000 / 12,000 / 40,000. Earlier aliquot never acquires D. |
| Precision and processing | Repeated micro-kg withdrawals/top-ups conserve every root share and total, without negatives or duplicate roots. Processing input/tolerance uses withdrawn mass only; existing stage, shortage, compatibility and policy guards still reject invalid commands. |
| Transfers and all ingress | Partial transfer into pool consumes exactly selected position/quantity; other positions remain unchanged. Pool-to-pool movement updates both generations atomically. Processing output in mixed storage joins current compatible pool. Incompatible/ambiguous residual attribution fails without partial movement. |
| Stale/concurrent work | Old generation drafts fail until explicitly refreshed and reconfirmed where required. Two compatible arrivals serialize into one current pool; competing withdrawal/top-up/HOLD/correction produces valid serialized result or explicit conflict, never overspend/lost arrival. |
| Atomicity/retries | Failure after root receipt or during pool postings leaves no half-accepted arrival, orphan generation or pointer change. Same command retry makes no extra stock/events; conflicting retry fails. |
| Summaries and samples/lab | Per-procurement/source summary shows only its remaining attributed share; expected mass remains distinct. Late confirmed lab report still resolves original source and affected pools/descendants; no copied pool-level PASS, automatic HOLD or evidence reassignment. |
| HOLD and history | Root/intermediate HOLD blocks affected pool and pool-derived descendants, including later stock after transfer. New-source HOLD does not affect earlier withdrawals lacking that source. Availability agrees with commands; authorized isolation preserves restrictions. History includes merge/split/processing edges under existing scope/redaction. |
| Corrections | Mixed-custody correction unsupported; changed generation or downstream custody prevents compensation. Original untouched-package correction is not enabled without approved policy. No unmix, cascade, history mutation or HOLD release. |
| Empty lifecycle | Full drain leaves no active generation. Refill creates fresh generation; previous sources do not leak into it. Owner/clearance checks remain required. Occupied mode changes are rejected. |
| Migration safety | Full historical migration chain plus new forward migration works on isolated fresh DB and prior-schema DB with cleared business data. Prior history entries remain intact; old migrations/resources unchanged; restricted runtime cannot mutate protected tables directly. |


## Policy and safety boundaries

Pooling requires an explicit append-only policy for exact persisted location kind, owner, commodity, stage and product identity, effective time, evidence, and administrator identity-scope confirmation. Policy does not assert unlisted properties such as variety, moisture or grade; callers must not broaden eligibility beyond recorded scope. No location kind implies pooling and no factory/default policy exists.

Supported pool locations are `STORE`, `SILO` and `COMPARTMENT`; `ISOLATION` is reserved for HOLD isolation. Full drain retains identity until recorded clearance. Multi-position source residuals that cannot be proven fail closed. HOLD follows only positive source ancestry; isolation preserves HOLD; lab confirmation does not assess or disposition stock.

Custody correction is unsupported; downstream custody or changed generation prevents compensation. Original untouched-package correction is also not enabled without approved policy. No implicit unmix, source removal, cascade or HOLD release.

## Isolated runtime verification

Observed isolated real-cookie/CSRF HTTP and browser workflow: enabled policy-backed location; accepted arrivals 50,000 + 30,000 + 20,000 kg → current 100,000 kg pool. Withdrawal 40,000 kg produced 20/12/8k aliquot and 30/18/12k remainder; later 40,000 kg top-up yielded 30/18/12/40k current shares without changing earlier aliquot. Stale revision-3 draft returned conflict; explicit refresh/reselect/save revision 5 then 10,000 kg dry commit yielded raw 90,000 kg revision 6 + dry 10,000 kg; source A total reconciled 50,000 kg = 20,000 aliquot + 27,000 raw + 3,000 dry.

Late confirmed source-A report preserved original source association and `NOT_ASSESSED`, with no automatic stock change. Explicit late source-D HOLD blocked current raw 90,000 kg and dry 10,000 kg while earlier 40,000 kg aliquot remained available; ordinary transfer rejected. Manager isolation transfer of 4,000 kg held pool using matching root-D HOLD created isolated aliquot and remainder; isolated aliquot availability was zero and HOLD persisted. Historical generation revision 5 remained deep-link readable with zero physical stock and original 30/18/12/40k composition; current selector exposed only positive current generation. At 375px document width stayed 375px with no browser errors.

Final-build smoke repeated selected-position ingress through authenticated HTTP: 10 kg ordinary stock became 6 kg ordinary remainder plus one 4 kg current pool, revision 1; source-attributed usable mass remained 10 kg. Browser showed both positions and exact composition with no errors.

Final checks:

| Check | Observed result |
| --- | --- |
| `dotnet build GrainLedger.slnx` | Passed; existing CS9113 unused `access` parameter warning. |
| `dotnet test GrainLedger.slnx --no-build` | 330 passed, 0 failed, 0 skipped; includes all 29 mixed-bulk cases, fresh/prior-schema migrations, runtime grants and ordinary-workflow regressions. |
| `npm test` | 112 passed across 20 files. |
| `npm run build` | Passed; existing Vite chunk-size warning. |
| Affected documentation links | Seven documents checked; no missing relative file targets. |

Browser tabs and temporary smoke hosts were closed; synthetic session/evidence artifacts removed. No shared database writes or deployment occurred. Historical migrations and SQL resources remain unchanged.
