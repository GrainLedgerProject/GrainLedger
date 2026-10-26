# F14 blend planning backend

Track the remaining backend implementation and acceptance gates in the
[F14 completion plan](f14-completion-plan.md). This document describes the
current API slices; it does not establish completion of F14.

## Implementation status (26 October 2026)

The backend now supports draft demand, scoped stock candidates and lineage,
published assessment/yield policies, confirmed finished-lot quality observations,
bounded greedy assessment, a ten-minute preview reference, and an immutable
proposal. Proposal persistence rechecks demand, stock, HOLD, pool, evidence and
policy under the shared writer lock. Exact operation retries replay; a changed
request with the same operation ID conflicts. The proposal does not reserve,
HOLD, post or deduct stock. `GET /proposal` reports CURRENT or STALE against
the saved snapshot. A new quality version or stock HOLD invalidates a preview.

Missing approved policy or evidence yields INCOMPLETE, so only a PASS preview
can be proposed. There is no F14 frontend, and the new owner migrations and
runtime grants have not been applied to the shared database. F15 must recheck
and reserve stock before approval or execution.
Finished-lot quality is confirmed explicitly by a Customer Manager against a
Ledger lot and evidence file; Capture source-sample results are not silently
promoted to finished-product measurements.

The sections below retain details of the candidate and diagnostic APIs.

The candidate read now separates physical eligibility from product compatibility.
A mapped input commodity can remain visible when it differs from the order's
output commodity. With a draft, direct match checks both commodity and product.
The GET route no longer returns a greedy mass selection. `candidateReadReady`
and `compatibilityReadReady` are separate; a plan-specific read checks draft
storage before accessing it.

`CandidateGroupValidator` is an internal backend service for future proposal
search and write-time rechecking. It validates selected positions, exact kg,
revisions and one Ledger compatibility set. A blend remains INCOMPLETE without
an approved output-yield rule. Capture now offers a company-scoped bulk read of
confirmed Lab evidence; Catalog offers the same point-in-time MRL resolver to
API and future assessment code. The validator rejects a blend whose selected
input mass is below the container target, since physical output cannot exceed
input mass. Sufficient input mass alone remains INCOMPLETE: Ledger's balance
tolerance is not a prospective output-yield rule.
Neither read supplies residue interpretation,
cohort/propagation bounds, quality observations, margin or an F14 verdict.

F14 stores versioned demand drafts for a planned export container and reads
physical stock candidates with mixed-bulk ancestry. A draft pins the order,
specification and target. Candidate reads alone do not supply a compliance
verdict or execution authority.

## Current HTTP contract

All routes use persisted active-role and customer-company scope. Reads use `export-orders.read` (ExportStaff and CustomerManager); draft creation and cancellation use the existing `blend.propose` permission (ExportStaff). Administrator is not a business-role wildcard. Cookie writes require CSRF. Cross-company resources return 404.

| Method and route | Behavior |
| --- | --- |
| GET /api/export-containers/{id}/planning-context | Order/container versions, storage and candidate-read readiness, canCreateDraft, canPropose and missing integration reasons. Does not require planning tables to be installed. |
| GET /api/export-containers/{id}/stock-preview?facilityId={id}&page=1&pageSize=25 | Current own-company Ledger lots at the selected facility, positions, available kg and restrictions; paged display only. Works with the existing Ledger schema. |
| GET /api/export-containers/{id}/inventory-scope?facilityId={id} | Scans the complete own-company/facility inventory in one repeatable-read transaction. Returns lot count, available mass and restricted-lot count. It explicitly reports `revisioned=false` and `complianceQualified=false`. |
| GET /api/export-containers/{id}/candidates?facilityId={id}&includeExcluded=true&page=1&pageSize=25&planId={id} | Owner/facility candidate positions, exact physical mass, pool revision, source count, reasons and scope/candidate fingerprints. Optional `planId` reads a current draft's target and effective Ledger blend policies to annotate product compatibility. `scopeRevision` may be sent on later pages; a changed scope returns 409. The summary covers the whole facility before paging. |
| GET /api/export-containers/{id}/candidates/{lotId}/lineage?facilityId={id}&locationId={id} | Full reachable custody/processing graph, source composition and Capture source mapping for one lot/location. Optional `candidateRevision` detects a changed snapshot. |
| GET /api/export-containers/{id}/candidates/{lotId}/residue-preview?facilityId={id}&locationId={id}&candidateRevision={revision}&substanceIds={comma-separated-UUIDs} | Read-only F12 residue intervals for a physically eligible candidate. Requires both `export-orders.read` and `residue.evaluate`. A changed candidate returns 409; F12 validates 1–200 distinct substances. The response is diagnostic and `assessmentStatus` remains `NOT_ASSESSED`. |
| POST /api/blend-plans/{id}/residue-preview | Reads 1–100 selected positions for a current draft and 1–200 substances. Validates physical selection, exact compatibility set and revisions, then computes input intervals and, for a blend with an effective output commodity, a hypothetical BLENDING interval in one F12 repeatable-read snapshot. Rechecks candidate scope after the F12 read. Returns fail-closed F14 assessment findings; absent approved policy remains `INCOMPLETE`. Does not save a proposal or reserve stock. Requires `residue.evaluate` plus own-company export read scope. |
| POST /api/export-containers/{id}/blend-plans/drafts | Creates an immutable demand snapshot and the next draft version. Body: operationId, orderVersion, containerVersion, targetProductIdentity. |
| GET /api/export-containers/{id}/blend-plans | Paged drafts, newest version first. |
| GET /api/blend-plans/{id} | Draft demand, status, ETag and canCancel. |
| GET /api/blend-plans/{id}/history | Paged actor-labelled history. |
| POST /api/blend-plans/{id}/preview | Runs bounded greedy search and fail-closed assessment. A PASS returns an expiring `previewReference`; no stock changes. |
| POST /api/blend-plans/{id}/propose | Body: `operationId`, `previewReference`. Rechecks the preview under the writer lock and atomically stores an immutable proposal and history. Stale inputs return 409. |
| GET /api/blend-plans/{id}/proposal | Saved assessment, allocations and CURRENT/STALE freshness result. Does not authorize execution. |
| POST /api/blend-plans/{id}/cancel | Body: operationId. Requires current plan ETag in If-Match. DRAFT becomes CANCELLED. |
| POST /api/blend-assessment-policies | Customer Manager publishes an immutable company/market/commodity policy with required substance IDs, explicit MRL margin fraction, residue basis, per-attribute quality calculation rules, effective period and review evidence. No default policy is seeded. |
| GET /api/blend-assessment-policies/applicable?marketId={id}&commodityId={id}&at={instant} | Read latest applicable own-company policy. `at` is optional; omission uses database time. |
| POST /api/ledger/planning-yield-rules | Assigned Mill Manager publishes an immutable prospective output fraction for one facility/compatibility rule, backed by Ledger evidence. No default yield is seeded. |
| GET /api/ledger/planning-yield-rules/applicable?ownerId={id}&facilityId={id}&compatibilityPolicyId={id}&at={instant} | Export reader resolves a rule for an owned company; `at` is optional. |
| POST /api/ledger/planning-quality-observations | Customer Manager confirms an immutable versioned observation for a finished Ledger lot, quality attribute, value/unit and Ledger evidence file. |
| GET /api/ledger/planning-quality-observations?ownerId={id}&facilityId={id}&lotIds={id}&at={instant} | Scoped point-in-time read of confirmed observations. |

The group residue preview accepts a body such as:

```json
{"facilityId":"6c656467-6572-4000-8000-000000000001","scopeRevision":"<from candidates read>","mode":"BLEND_INPUT_STOCK","compatibilityPolicyId":"<effective policy UUID>","selected":[{"lotId":"<lot UUID>","locationId":"<location UUID>","quantityKg":"1000","candidateRevision":"<from candidates read>"}],"substanceIds":["<substance UUID>"]}
```

The preview reads residue for each distinct selected lot. The input intervals,
policy, evidence and any hypothetical output share one F12 snapshot. The stock
and F12 reads remain separate snapshots; a proposal write must recheck both.
For `BLEND_INPUT_STOCK`, a policy read that includes `outputLedgerCommodityId`
also enables `hypotheticalBlend`: F12 evaluates a temporary BLENDING node using
the selected kg, approved F12 process factors and the policy's output commodity.
This is a read-only concentration calculation. It does not establish output kg,
market PASS or a physical Ledger operation. A missing approved rule produces an
unresolved F12 result. The output-commodity field is added by forward-only Ledger
migration `20261024000000_PlanningCompatibilityOutput`.
The response reports one `residueSnapshotAt`, `evidenceStatus` and per-lot
`unresolved` substances. `INPUT_BOUNDS_AVAILABLE` means only that all requested
input-lot intervals are finite; it is not a hypothetical blend result or MRL
decision. `INCOMPLETE` preserves F12 reasons instead of inventing a PASS.
It also resolves the published MRL version for the order's market and commodity
at `residueSnapshotAt`. `applicableMrl` contains its status, version, effective
period, legal source, published limit count and limits for the requested
substances. This is source information for a future assessment: client-selected
substances do not establish required panel coverage, and F12 basis-to-MRL
comparability and safety margin rules have not been approved. The response
therefore returns `assessmentStatus=INCOMPLETE` with explicit
`assessmentFindings` (including missing yield, coverage/margin and quality
rules). A proven violation is `FAIL` even when other checks remain incomplete.
`assessmentPolicy` and `outputYieldPolicy` identify the published rules used
when present. Neither rule is inferred from Ledger's posting tolerance or F12
concentration factors. This diagnostic endpoint cannot propose a plan.

Example draft body:

```json
{"operationId":"75176703-30fb-49e8-98b4-4a66a946bc82","orderVersion":1,"containerVersion":1,"targetProductIdentity":"HEAD_RICE"}
```

The server derives company, actor, target mass, specification and next version. `targetProductIdentity` states the intended product for this container's plan. It is required when creating a new draft and is pinned in the immutable `demandSnapshot` and exposed on draft reads. Supported Ledger identities are `RAW_PADDY`, `DRIED_PADDY`, `HEAD_RICE`, `BROKEN_RICE`, `BRAN`, `HUSK` and `BLENDED_RICE`. Bagging retains the underlying identity, so `BAGGED_RICE` is a stage, not a target product identity. Existing drafts without this field read as null and cannot establish a product target. Commodity remains on the F13 order; one order can have containers with different draft targets. The candidate read now reports product-match hints for a current draft, but does not approve any cross-product substitution.

`versionNo` is the draft sequence within a container; `version` is the concurrency token for that draft. A new request makes a new version, including after cancellation. There is no edit/delete endpoint for historical demand. `demandSnapshot` is a JSON string containing market/commodity, shipping dates, order/container versions, target product identity, target mass and quality specification. Mass is a decimal string with up to six fractional digits; quality values retain up to twelve.

The same operation ID and request replay the stored response, including its original status/ETag/Location. A changed payload or route with that operation ID returns 409. Replay occurs before current-state checks. After replay, GET detail for current status/capabilities. Missing or stale If-Match on cancellation returns 412; creation checks both expected parent versions. All writes use the existing per-company Export transaction advisory lock, including F13 writes.

Draft creation does not freeze F13 order, specification, or container-target edits. The draft's saved demand remains immutable; `demandCurrent` is computed from its saved order/container/specification versions and current target. A demand edit makes an older draft non-current. Creating a fresh draft requires the latest order and container versions. A proposal is immutable; cancellation of a proposed draft is rejected. Proposal freshness is checked on read and at creation.

The container planning-context `canPropose` remains false because it has no selected draft, facility or assessed candidate set. The plan preview response supplies the actionable `canPropose` value. `candidateReadReady` only confirms the reviewed Ledger routine and restricted grant exist; it does not mean any lot is eligible. There is no approval endpoint or F14 frontend screen yet.

`POST /api/blend-plans/{id}/preview` accepts `facilityId`, the current plan-specific
`scopeRevision` from `GET /api/export-containers/{containerId}/candidates?facilityId=...&planId=...`,
and `maxIterations` (1–256, default 32). It uses a bounded deterministic
candidate search, exact Ledger product-set validation, published yield and
assessment policies, F12 hypothetical residue bounds and applicable MRL.
For a 50% yield rule and a 10 kg container, the blend selector requests 20 kg
of input stock. The response includes `outcome`, `iterations`, `selection`,
`assessmentStatus`, findings and `predictedOutputKg`. A changed candidate or
demand revision returns 409. A PASS with all required confirmed evidence
returns a ten-minute `previewReference` and `canPropose=true`. Missing evidence
returns INCOMPLETE with no reference. Preview does not reserve stock.
The saved assessment includes `calculationVersion=F14_ASSESSMENT_V1`, selected
allocations, findings, applicable MRL version and limits, assessment/yield
policies, F12 residue bounds with provenance, and quality observation IDs and
versions. The proposal copies this assessment unchanged.

Example calls after creating a draft and loading its plan-specific candidates:

```json
POST /api/blend-plans/{planId}/preview
{"facilityId":"<facility UUID>","scopeRevision":"<candidates.scopeRevision>","maxIterations":32}

POST /api/blend-plans/{planId}/propose
{"operationId":"<new UUID>","previewReference":"<PASS preview.previewReference>"}
```

The proposal response contains `assessment.selection.allocations`,
`assessment.assessmentFindings`, `assessment.evidence`, `freshness` and
`stockReserved=false`. GET the proposal again for current freshness; a quality
revision, HOLD, stock or policy change can make the stored proposal stale.

## Candidate-read contract

`GET candidates` requires `facilityId`; `includeExcluded` defaults to false, and page/pageSize default to 1/25 (maximum 100). With `includeExcluded=true`, a HOLD or incomplete lineage is shown with a reason and `inventoryEligible=false`. Inventory eligibility only verifies physical location/owner, Catalog commodity mapping, available mass, current pool generation and proven source ancestry. A mapped input commodity different from the order commodity remains physically eligible: an effective Ledger policy may allow it as a blend input. Without `planId`, product compatibility is `NOT_EVALUATED`. With a current draft's `planId`, each item is `DIRECT_MATCH` only when its product identity and Catalog commodity match the target, `POSSIBLE_BLEND_INPUT` if it is an allowed input stage and at least one effective facility-scoped Ledger exact-set policy includes its commodity/product and outputs the order's Catalog commodity as blended rice, `BLEND_POLICY_MISSING` if it is a rice input stage but has no such policy, or `PRODUCT_MISMATCH` otherwise. The response includes `planId` and `targetProductIdentity`. A cancelled/stale draft returns 409; a legacy draft without a target returns 409. MRL and quality remain `NOT_ASSESSED`. `inventoryEligibleMassKg` and `inventoryShortfallKg` are physical-stock diagnostics across mapped commodities; `directProductAvailableMassKg` is direct-match stock only. None establishes feasible output. The read does not return a greedy selection.

`POSSIBLE_BLEND_INPUT` is only a per-lot hint: Ledger authorizes a physical blend against the **exact set of all input products**. It does not mean that any selected combination is valid. The policy read and candidate read share one repeatable-read Ledger transaction, and the policy snapshot participates in the plan-specific `scopeRevision`. A proposal must recheck the full selected set, physical quantities, demand, policy and compliance at write time. `canPropose` remains false.

The Ledger-owned `planning_candidates` read returns the complete owner/facility position scope, exact source contributions, Capture source IDs, custody and processing edges, and operation context in one read-only repeatable-read transaction. `(lotId,locationId)` is the candidate key. A current pool reports a string `poolRevision`; ordinary or withdrawn stock reports null. A withdrawn aliquot is not excluded merely because it is no longer the current pool. `location.cleared` is a sanitation state, not an active/inactive flag. Position attribution for a multi-source lot in multiple positive locations is not proven by lot-level contributions, so that candidate is excluded with `POSITION_ATTRIBUTION_UNRESOLVED`. Pre-mixed-bulk processing has an ancestry edge but may lack exact input-to-source allocation; it is explicitly unproven rather than silently treated as complete.

`scopeRevision` detects a changed candidate scope when reading subsequent pages. `candidateRevision` detects changes before reading a candidate's lineage. These fingerprints are not reservations or monotonic event counters. The proposal write rechecks current demand, eligibility, quantity and revisions before saving; F15 reservation must recheck under the Ledger write lock.

The candidate read has forward-only Ledger migration `20261021000000_PlanningCandidates`. Plan-specific product hints add forward-only migration `20261022000000_PlanningCompatibility`, which exposes only effective facility policies through an owner-scoped protected read. Missing routine/grant returns 503 `CANDIDATE_SCHEMA_NOT_READY`; missing draft storage with `planId` returns 503 `PLANNING_SCHEMA_NOT_READY`. F13 and the existing F14 draft routes remain separate. No shared database migration was applied. Apply reviewed owner migrations and rerun the restricted runtime-role configuration only after separate approval.

The stock preview calls Ledger's existing scoped `lots` read. It includes rows that have stock in the selected facility, even when HOLD or another restriction makes available mass zero. It does not filter by commodity, product identity, compatibility, laboratory evidence or quality. It is not a feasible candidate list and a paged preview cannot be used as solver input. No stock changes result from this read.

The backend inventory-scope adapter scans every Ledger page under one repeatable-read, read-only PostgreSQL transaction (maximum 10,000 lots). It enforces owner scope and retains exact decimal mass. If the scope exceeds the scan limit, it fails rather than silently using a partial set. This is a consistent inventory read, not a durable stock revision, product-compatible candidate list, lineage proof or compliance assessment. No proposal may treat it as such.

## Storage and deployment

Migration `20261021035744_BlendPlanningDrafts` adds `exports.blend_plan_drafts` and `exports.blend_plan_events`, cross-company/order/container/specification foreign keys and immutable history triggers. Cancellation is the only draft-row update allowed. It does not guard F13 demand edits. Downgrade is unsupported because it would erase plan history.

F13 remains usable before this optional migration. Planning mutations return 503 `PLANNING_SCHEMA_NOT_READY` until tables and runtime privileges exist. If planning tables exist but their read grants are missing, demand guards fail closed. The existing runtime-role configuration grants the new tables after migration.

Do not run a shared-database migration as part of startup or tests. Shared deployment requires separate explicit approval, owner migration and runtime-role configuration. No shared migration was applied as part of this slice.

## Remaining deployment and downstream work

1. Apply reviewed Catalog, Ledger and Export owner migrations and restricted runtime-role configuration to the shared database only with separate approval.
2. Add F14 frontend when that scope begins. The current backend can be tested through HTTP on isolated PostgreSQL.
3. F15 must synchronously recheck proposal freshness and atomically reserve approved allocations under the Ledger writer lock. F14 never grants execution authority.

Business inputs remain explicit reviewed policies. Unsupported combinations and absent evidence fail closed.

## Verification

Run `dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~ExportOrderTests|FullyQualifiedName~ExportRuleTests"` from the repo root. The fixture provisions disposable Docker PostgreSQL and a restricted runtime role. It does not use the configured shared database.

The new HTTP tests exercise draft replay/versioning, cancellation, history pagination, manager read-only access, cross-company isolation, F13 edits and derived draft staleness, concurrent target edit versus draft creation, immutable database history and unchanged stock. Frontend acceptance begins when the planning screen is implemented.

Verification on 21 October 2026 before removal of the selection endpoint: 40/40 ExportOrderTests and MixedBulkTests passed against disposable PostgreSQL. Candidate tests cover ordinary stock, exact source mapping, pool withdrawal and later top-up, stale HOLD, role/company scope, processing ancestry/loss context and unchanged stock. Shared database and production grants were not changed. Assessment/planner and broader acceptance tests remain pending.
