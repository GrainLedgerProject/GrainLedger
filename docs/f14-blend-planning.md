# F14 blend planning: draft and candidate-read slices

## Implementation status (22 October 2026)

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
API and future assessment code. Neither read supplies residue interpretation,
cohort/propagation bounds, quality observations, margin or an F14 verdict.

There is still no proposal endpoint, assessment, reservation or F14 frontend.
The dormant `GreedyStockSelection` source is not called by HTTP routes. Do not
publish an F14 plan from physical stock alone. No shared database migration was
performed for this implementation slice.

F14 stores versioned demand drafts for a planned export container and reads physical stock candidates with mixed-bulk ancestry. A draft pins the order, specification and target. The candidate reads do not supply a compliance verdict, reservation or execution authority. F12 bounds and the planner are subsequent slices.

## Current HTTP contract

All routes use persisted active-role and customer-company scope. Reads use `export-orders.read` (ExportStaff and CustomerManager); draft creation and cancellation use the existing `blend.propose` permission (ExportStaff). Administrator is not a business-role wildcard. Cookie writes require CSRF. Cross-company resources return 404.

| Method and route | Behavior |
| --- | --- |
| GET /api/export-containers/{id}/planning-context | Order/container versions, storage and candidate-read readiness, canCreateDraft, canPropose and missing integration reasons. Does not require planning tables to be installed. |
| GET /api/export-containers/{id}/stock-preview?facilityId={id}&page=1&pageSize=25 | Current own-company Ledger lots at the selected facility, positions, available kg and restrictions; paged display only. Works with the existing Ledger schema. |
| GET /api/export-containers/{id}/inventory-scope?facilityId={id} | Scans the complete own-company/facility inventory in one repeatable-read transaction. Returns lot count, available mass and restricted-lot count. It explicitly reports `revisioned=false` and `complianceQualified=false`. |
| GET /api/export-containers/{id}/candidates?facilityId={id}&includeExcluded=true&page=1&pageSize=25&planId={id} | Owner/facility candidate positions, exact physical mass, pool revision, source count, reasons and scope/candidate fingerprints. Optional `planId` reads a current draft's target and effective Ledger blend policies to annotate product compatibility. `scopeRevision` may be sent on later pages; a changed scope returns 409. The summary covers the whole facility before paging. |
| GET /api/export-containers/{id}/candidates/{lotId}/lineage?facilityId={id}&locationId={id} | Full reachable custody/processing graph, source composition and Capture source mapping for one lot/location. Optional `candidateRevision` detects a changed snapshot. |
| POST /api/export-containers/{id}/blend-plans/drafts | Creates an immutable demand snapshot and the next draft version. Body: operationId, orderVersion, containerVersion, targetProductIdentity. |
| GET /api/export-containers/{id}/blend-plans | Paged drafts, newest version first. |
| GET /api/blend-plans/{id} | Draft demand, status, ETag and canCancel. |
| GET /api/blend-plans/{id}/history | Paged actor-labelled history. |
| POST /api/blend-plans/{id}/cancel | Body: operationId. Requires current plan ETag in If-Match. DRAFT becomes CANCELLED. |

Example draft body:

```json
{"operationId":"75176703-30fb-49e8-98b4-4a66a946bc82","orderVersion":1,"containerVersion":1,"targetProductIdentity":"HEAD_RICE"}
```

The server derives company, actor, target mass, specification and next version. `targetProductIdentity` states the intended product for this container's plan. It is required when creating a new draft and is pinned in the immutable `demandSnapshot` and exposed on draft reads. Supported Ledger identities are `RAW_PADDY`, `DRIED_PADDY`, `HEAD_RICE`, `BROKEN_RICE`, `BRAN`, `HUSK` and `BLENDED_RICE`. Bagging retains the underlying identity, so `BAGGED_RICE` is a stage, not a target product identity. Existing drafts without this field read as null and cannot establish a product target. Commodity remains on the F13 order; one order can have containers with different draft targets. The candidate read now reports product-match hints for a current draft, but does not approve any cross-product substitution.

`versionNo` is the draft sequence within a container; `version` is the concurrency token for that draft. A new request makes a new version, including after cancellation. There is no edit/delete endpoint for historical demand. `demandSnapshot` is a JSON string containing market/commodity, shipping dates, order/container versions, target product identity, target mass and quality specification. Mass is a decimal string with up to six fractional digits; quality values retain up to twelve.

The same operation ID and request replay the stored response, including its original status/ETag/Location. A changed payload or route with that operation ID returns 409. Replay occurs before current-state checks. After replay, GET detail for current status/capabilities. Missing or stale If-Match on cancellation returns 412; creation checks both expected parent versions. All writes use the existing per-company Export transaction advisory lock, including F13 writes.

Draft creation does not freeze F13 order, specification, or container-target edits. The draft's saved demand remains immutable; `demandCurrent` is computed from its saved order/container/specification versions and current target. A demand edit makes an older draft non-current. Creating a fresh draft requires the latest order and container versions. Cancellation retains history but does not lock F13. A future `PROPOSED` plan will require a separate atomic edit guard and stale/freshness rules.

`canPropose` is always false in this slice. `candidateReadReady` only confirms the reviewed Ledger routine and restricted grant exist; it does not mean any lot is eligible. Missing dependencies still identify Compliance and the planner. No search or assessment is fabricated. There is no POST proposal/approval endpoint and no new frontend screen yet.

## Candidate-read contract

`GET candidates` requires `facilityId`; `includeExcluded` defaults to false, and page/pageSize default to 1/25 (maximum 100). With `includeExcluded=true`, a HOLD or incomplete lineage is shown with a reason and `inventoryEligible=false`. Inventory eligibility only verifies physical location/owner, Catalog commodity mapping, available mass, current pool generation and proven source ancestry. A mapped input commodity different from the order commodity remains physically eligible: an effective Ledger policy may allow it as a blend input. Without `planId`, product compatibility is `NOT_EVALUATED`. With a current draft's `planId`, each item is `DIRECT_MATCH` only when its product identity and Catalog commodity match the target, `POSSIBLE_BLEND_INPUT` if it is an allowed input stage and at least one effective facility-scoped Ledger exact-set policy includes its commodity/product and outputs the order's Catalog commodity as blended rice, `BLEND_POLICY_MISSING` if it is a rice input stage but has no such policy, or `PRODUCT_MISMATCH` otherwise. The response includes `planId` and `targetProductIdentity`. A cancelled/stale draft returns 409; a legacy draft without a target returns 409. MRL and quality remain `NOT_ASSESSED`. `inventoryEligibleMassKg` and `inventoryShortfallKg` are physical-stock diagnostics across mapped commodities; `directProductAvailableMassKg` is direct-match stock only. None establishes feasible output. The read does not return a greedy selection.

`POSSIBLE_BLEND_INPUT` is only a per-lot hint: Ledger authorizes a physical blend against the **exact set of all input products**. It does not mean that any selected combination is valid. The policy read and candidate read share one repeatable-read Ledger transaction, and the policy snapshot participates in the plan-specific `scopeRevision`. A proposal must recheck the full selected set, physical quantities, demand, policy and compliance at write time. `canPropose` remains false.

The Ledger-owned `planning_candidates` read returns the complete owner/facility position scope, exact source contributions, Capture source IDs, custody and processing edges, and operation context in one read-only repeatable-read transaction. `(lotId,locationId)` is the candidate key. A current pool reports a string `poolRevision`; ordinary or withdrawn stock reports null. A withdrawn aliquot is not excluded merely because it is no longer the current pool. `location.cleared` is a sanitation state, not an active/inactive flag. Position attribution for a multi-source lot in multiple positive locations is not proven by lot-level contributions, so that candidate is excluded with `POSITION_ATTRIBUTION_UNRESOLVED`. Pre-mixed-bulk processing has an ancestry edge but may lack exact input-to-source allocation; it is explicitly unproven rather than silently treated as complete.

`scopeRevision` detects a changed candidate scope when reading subsequent pages. `candidateRevision` detects changes before reading a candidate's lineage. These fingerprints are not reservations or monotonic event counters. The future proposal write must check current demand, eligibility, quantity and revisions again before saving; F15 reservation must recheck under the Ledger write lock.

The candidate read has forward-only Ledger migration `20261021000000_PlanningCandidates`. Plan-specific product hints add forward-only migration `20261022000000_PlanningCompatibility`, which exposes only effective facility policies through an owner-scoped protected read. Missing routine/grant returns 503 `CANDIDATE_SCHEMA_NOT_READY`; missing draft storage with `planId` returns 503 `PLANNING_SCHEMA_NOT_READY`. F13 and the existing F14 draft routes remain separate. No shared database migration was applied. Apply reviewed owner migrations and rerun the restricted runtime-role configuration only after separate approval.

The stock preview calls Ledger's existing scoped `lots` read. It includes rows that have stock in the selected facility, even when HOLD or another restriction makes available mass zero. It does not filter by commodity, product identity, compatibility, laboratory evidence or quality. It is not a feasible candidate list and a paged preview cannot be used as solver input. No stock changes result from this read.

The backend inventory-scope adapter scans every Ledger page under one repeatable-read, read-only PostgreSQL transaction (maximum 10,000 lots). It enforces owner scope and retains exact decimal mass. If the scope exceeds the scan limit, it fails rather than silently using a partial set. This is a consistent inventory read, not a durable stock revision, product-compatible candidate list, lineage proof or compliance assessment. No proposal may treat it as such.

## Storage and deployment

Migration `20261021035744_BlendPlanningDrafts` adds `exports.blend_plan_drafts` and `exports.blend_plan_events`, cross-company/order/container/specification foreign keys and immutable history triggers. Cancellation is the only draft-row update allowed. It does not guard F13 demand edits. Downgrade is unsupported because it would erase plan history.

F13 remains usable before this optional migration. Planning mutations return 503 `PLANNING_SCHEMA_NOT_READY` until tables and runtime privileges exist. If planning tables exist but their read grants are missing, demand guards fail closed. The existing runtime-role configuration grants the new tables after migration.

Do not run a shared-database migration as part of startup or tests. Shared deployment requires separate explicit approval, owner migration and runtime-role configuration. No shared migration was applied as part of this slice.

## Contracts to implement next

1. **Candidate follow-up:** the read-only candidate adapter, composition, reachable ancestry and per-lot product-match hints are implemented. Add exact selected-set compatibility and a true write-time freshness check before any proposal. Candidates with unproven position allocation or legacy processing attribution remain excluded.
2. **Lab evidence adapter:** confirmed source associations, raw results, linked amendments/retests, revisions and change notifications. Lab's read-time VN comparison is not the export assessment.
3. **Compliance/F12:** versioned interpretation/cohort/propagation policies, conservative residue bounds through operations, quality measurements and per-attribute propagation, manager-owned margin and coverage policy. Missing policy/data returns INCOMPLETE. No default margin or implicit concentration reduction.
4. **Assessment:** resolve Catalog MRL for market/commodity/server decision time; preserve the published version, limits, policies, evidence and exact calculations in a snapshot. Return PASS/FAIL/INCOMPLETE with reasons.
5. **Planner:** single-container bounded deterministic search; exact mass allocations, final assessment and recheck of dependency revisions before saving a PROPOSED version. SEARCH_EXHAUSTED does not prove global infeasibility.
6. **Invalidation/Trace:** complete affected plans from changed stock/evidence/policies; durable change events and synchronous freshness recheck. F15 adds atomic approval/reservation. F14 never grants execution authority.

Do not introduce competing Lab/Ledger models before reviewing the owning teammate's branch. Business inputs still needed are evidence conflict/freshness rules, required substance coverage, cohort applicability, drying/milling propagation, per-attribute calculations and commodity/product compatibility. They are explicit policy inputs, not guessed constants.

## Verification

Run `dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj --filter "FullyQualifiedName~ExportOrderTests|FullyQualifiedName~ExportRuleTests"` from the repo root. The fixture provisions disposable Docker PostgreSQL and a restricted runtime role. It does not use the configured shared database.

The new HTTP tests exercise draft replay/versioning, cancellation, history pagination, manager read-only access, cross-company isolation, F13 edits and derived draft staleness, concurrent target edit versus draft creation, immutable database history and unchanged stock. Frontend acceptance begins when the planning screen is implemented.

Verification on 21 October 2026 before removal of the selection endpoint: 40/40 ExportOrderTests and MixedBulkTests passed against disposable PostgreSQL. Candidate tests cover ordinary stock, exact source mapping, pool withdrawal and later top-up, stale HOLD, role/company scope, processing ancestry/loss context and unchanged stock. Shared database and production grants were not changed. Assessment/planner and broader acceptance tests remain pending.
