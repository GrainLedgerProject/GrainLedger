# F14 backend completion plan

Updated: 2026-10-26. Status: F14 backend implemented and verified on isolated PostgreSQL; shared deployment and frontend are separate gates.

## Current result

The backend now confirms versioned finished-lot quality observations through
Ledger, resolves the published assessment/yield policies and MRL, evaluates
F12 residue bounds, searches candidates with bounded deterministic greedy,
and issues a ten-minute reference for a PASS preview. The proposal endpoint
rechecks the complete preview under the shared writer lock and atomically saves
the immutable allocation/assessment snapshot, event and idempotent operation.
`GET /proposal` re-evaluates current inputs and reports stale reasons. Tests on
disposable PostgreSQL cover creation, exact retry, changed retry, quality
revision, HOLD after preview, and unchanged stock/postings/HOLD at proposal.
Two simultaneous proposals for one blend draft produce one 201 and one 409.
Unsupported or missing evidence still returns INCOMPLETE.
Finished-lot quality is an explicit Customer Manager confirmation backed by a
Ledger evidence file; it is not inferred from a Capture source-sample result.

Verification on 26 October: the focused F13/F14 suite passed 29/29; the
ResidueReportReview suite passed 24/24 after aligning confirmation with the
database clock. The full backend integration suite passed 493/495. Its two
remaining failures are in Identity concurrent admin demotion and Ledger
receiving timestamp assertions, outside F14. The F14 end-to-end API scenario
passed for direct stock and a two-lot BLENDED_RICE proposal, including migration
and restricted grants in the disposable database. The two-lot test exposed and
fixed the Ledger-to-Catalog commodity mapping and unstable synthetic lot ID in
the F12 hypothetical BLENDING read.
After those fixes, the focused F12/F13/F14 regression set passed 138/138.

No owner migration or runtime grant from this effort has been applied to the
shared database. F14 has no frontend. F15 approval and reservation must perform
its own atomic freshness check. The checklist below records the implementation
sequence and earlier status notes; this section is the current status.

## Goal and delivery boundary

Complete F14 backend in one implementation effort: F13 container demand →
candidate inventory → evidence and policy assessment → deterministic greedy
search → preview → fresh proposal persistence → detail, history and staleness.
The steps below are dependencies within that effort, not separate deliveries.
Do not mark F14 complete after adding only a read endpoint or a scaffold.

Frontend remains outside this backend effort. Approval, reservation and loading
belong to F15 onward. F14 preview/proposal must not change stock, postings or HOLD.
Shared-database migrations require separate explicit approval under AGENTS.md.

## Existing implementation to reuse

- F13 order/specification/container APIs and immutable demand drafts.
- Draft list, detail, cancellation and history.
- Company/facility inventory, candidate position revisions and lineage reads.
- CandidateGroupValidator: quantities, revisions, physical eligibility and exact
  compatibility set. Blend inputs below target mass are rejected; yield remains
  unresolved without an applicable rule.
- F12 residue calculator, reviewed evidence selection, versioned rules and
  multi-lot repeatable-read preview.
- Candidate and draft residue-preview routes expose input-lot bounds. The draft
  route can calculate hypothetical output bounds for a blend and resolve the
  published MRL version; it does not establish compliance.
- Existing greedy code is physical-stock scaffolding, not the finished planner.

Existing contracts: [F14 current API](f14-blend-planning.md),
[F13 API](export-order-api.md), [F12 scope](residue-bounds-plan.md).

## Module responsibilities

| Module | Required work |
| --- | --- |
| Exports | Orchestrate assessment/search; preview and propose; allocations; immutable findings and evidence snapshots; stale detection and history. |
| Ledger | Coherent scoped planning inputs; hypothetical BLENDING calculation through F12; versioned output-mass rules; dependency recheck coordinated with persistence. |
| Catalogs | Versioned assessment policies: required substance coverage, safety margin semantics and applicability; supported per-attribute quality calculation rules. Reuse published MRL resolution. |
| Lab/Capture | Confirm available quality observations; add an owning-module read contract if absent, preserving subject, units, confirmation, provenance and versions. Reuse residue evidence. |
| Identity | Enforce existing planning permissions and explicit policy-management permissions; maintain company/facility scope. |

Do not create competing Lab/Ledger models. Review owner contracts before extending
them. A quality attribute catalog entry is not a measured quality observation.

## Historical implementation checklist

The unchecked items below record the original work breakdown and intermediate
states. The current implementation and verification result is at the top of
this document; this checklist is retained for design traceability.

### 1. Define missing policy and evidence contracts

- [x] Inspect existing owner contracts and document exact gaps before changing schema.
- [x] Define output-mass/yield policy with facility, operation, input/output products,
  formula or supported parameters, effective period, immutable version and evidence.
- [x] Define assessment policy with market/commodity, required substances, explicit
  margin units/semantics, effective period and version.
- [ ] Define quality observations with measured subject, attribute, value/interval,
  unit, time, confirmation and evidence revision.
- [ ] Define supported quality propagation rules; distinguish weighted attributes
  from those requiring another rule or finished-product measurement.
- [ ] Provide authorized management/read contracts and migrations for missing data.
- [ ] Create explicitly synthetic policy fixtures for tests; no operational defaults.

No implicit yield of 100%, zero margin, missing-substance PASS or universal quality
averaging. Ledger mass-balance tolerance is not a prospective output-yield rule.
Residue concentration factors and mass yield are separate concepts. Unsupported
or missing policies produce INCOMPLETE rather than invented values.

### 2. Read coherent planning inputs

- [ ] Materialize current demand/specification/container versions.
- [ ] Read complete owner/facility positions, available kg, HOLD, pool revisions,
  proven lineage and exact-set compatibility policies.
- [ ] Read residue and quality evidence with applicable policy versions.
- [x] Resolve published MRL for market/commodity and server decision time in the
  diagnostic preview. Proposal persistence must re-resolve it atomically.
- [ ] Retain dependency identifiers, versions, snapshot time and calculation version.
- [ ] Avoid repeated reads of shared ancestry/evidence across candidate lots.
- [ ] Document snapshot boundaries; independent candidate and residue reads alone
  are insufficient to authorize proposal persistence.

### 3. Assess one explicit allocation

- [ ] Validate selected (lotId, locationId, quantityKg) positions and exact product set.
- [ ] Compute input and predicted output mass using applicable versioned rules.
- [ ] Build an in-memory hypothetical BLENDING node; create no physical lot/operation.
- [ ] Reuse F12 calculation with selected kg, applicable process conditions and
  correct output commodity/product identity. Preserve ancestry and evidence.
- [ ] Compute supported quality attributes with their own rules and units.
- [ ] Compare residue upper bounds and quality results against resolved thresholds.
- [ ] Return per-check values, limits, margin, rule/evidence references and reasons.
- [ ] Define aggregation: PASS requires all mandatory checks; FAIL records proven
  violations and also preserves missing-data findings; otherwise INCOMPLETE.
- [ ] Treat stale inputs separately from assessment outcomes.

### 4. Complete deterministic greedy search

- [ ] Replace physical-only selection with assessment-aware allocation search.
- [ ] Use stable ordering, exact kg, configured bounds on runtime/iterations and
  explicit compatible product sets.
- [ ] Evaluate and adjust allocations using the assessment service from step 3.
- [ ] Return allocations, predicted output, assessment and search diagnostics.
- [ ] SEARCH_EXHAUSTED must not claim global infeasibility.
- [ ] Remove or consolidate obsolete alternate planner paths.

### 5. Expose preview and persist proposals

- [ ] POST /api/blend-plans/{id}/preview: server loads demand/policies, runs search
  and assessment, returns a server-verifiable preview reference and findings.
- [ ] Define server-owned preview storage/expiry or equivalent integrity mechanism;
  never trust client-submitted assessment results.
- [ ] POST /api/blend-plans/{id}/propose: operationId plus preview reference;
  server rechecks dependencies and saves only an eligible result.
- [ ] Add Exports-owned schema for allocations, output mass, findings, policy/MRL/
  evidence versions, calculation version, provenance and immutable history.
- [ ] Coordinate final reads and save against concurrent stock, demand, evidence
  and policy writes; a separate compare-then-write without coordination is insufficient.
- [ ] Save proposal, history and retry result atomically. Exact retries replay;
  reused operation IDs with different requests fail.
- [ ] Expand existing detail/history responses to expose saved proposal evidence.
- [ ] Review residue-preview routes as diagnostics sharing the same core services.
  Do not restore the removed standalone candidate-selection validate endpoint.

### 6. Staleness and downstream boundaries

- [ ] Detect changed demand/specification/container, position quantity/location,
  HOLD, pool, evidence selections, residue rules, MRL and assessment policies.
- [ ] Preserve historical snapshots; return current freshness and reasons separately.
- [ ] Define how F13 edits interact with an existing proposed plan atomically.
- [ ] Ensure F15 must recheck before approval/reservation.
- [ ] Keep all F14 operations free of stock/posting/HOLD mutations.

### 7. Verification and handoff

- [ ] Happy path: F13 → draft → candidates → passing greedy preview → propose → history.
- [ ] Missing policy/evidence, unknown residue, MRL exceedance, failed quality,
  insufficient stock, loss/yield, tiny quantities and mixed pools.
- [ ] Late report, policy/MRL change, stale demand, HOLD and concurrent stock writes.
- [ ] Role/company/facility isolation, malformed inputs, exact retry and concurrent proposals.
- [ ] Prove stock, postings and HOLD are unchanged before/after F14 calls.
- [ ] Run relevant F13/F12 regressions and actual API flow using isolated PostgreSQL.
- [ ] Complete backend build and diff checks. Record exact passed/blocked tests.
- [ ] Test migrations/runtime grants on isolated DB; prepare shared deployment instructions.
- [ ] Update API documentation with request/response examples and reproducible test steps.

## Completion criteria

F14 backend is complete only when the full synthetic happy path and failure paths
work through real API calls, proposal persistence is race-safe and replay-safe,
history explains the decision, and no stock is mutated. Configuration gaps in
operational data must be reported separately from missing software capability.
Build success or isolated validator tests alone do not establish completion.

## Progress discipline

Update this checklist during implementation. Mark an item complete only with code
and relevant verification. Add a concise dated entry below with files/contracts
changed, checks actually run and remaining gaps. Do not replace the approved goal
with repeated small endpoint deliveries.

### 2026-10-24 baseline

Six files currently contain uncommitted F14 work: candidate/draft residue previews,
multi-lot F12 read, additional mass validation, tests and current-contract docs.
Backend build and two CandidateGroupValidator tests passed previously. Full new
HTTP paths and coherent concurrency behavior have not been integration-tested.
Docker was unavailable at the last integration-test attempt; check availability
before the final verification gate. This plan does not mark those gates complete.

### 2026-10-24 implementation update

The Ledger compatibility read now has a forward-only migration exposing its
output Ledger commodity to F14. F12 can evaluate an in-memory hypothetical
BLENDING node for selected lot quantities with approved process rules. The F14
group residue preview includes that calculation when the policy field is ready.
This addresses part of checklist step 3. It does not supply a mass-yield rule,
required-substance coverage, margin, quality assessment or proposal persistence.
Integration verification is pending; do not mark checklist step 3 complete.

### 2026-10-24 continued verification and read-contract audit

The F12 selection preview now reads all selected graphs, approved evidence and
rules once in a read-only repeatable-read transaction. Input intervals and the
hypothetical BLENDING output therefore share one snapshot. A forward-only
Ledger migration exposes the compatibility policy's output Ledger commodity;
the original migration is unchanged. An isolated PostgreSQL API test confirms
the blend remains INCOMPLETE without yield/evidence, and a direct-selection
test confirms preview leaves candidate inventory unchanged. The dormant greedy
selector now requires the direct lot's catalog commodity to match the order;
a unit test covers a wrong-commodity lot with the same product identity.

Contract audit: `capture.lab_results` stores report labels, raw values and
optional substance IDs, but has no structured quality-attribute association
or approved quality propagation rule. `catalog.quality_attributes` contains
code/name/unit only. F12 residue evidence and published MRL resolution exist,
but an F14-specific required-substance/margin policy and output-mass/yield
policy do not. These are missing software contracts, not mere missing demo
rows. Do not claim a PASS assessment or save a proposal until they exist.

Checks: API build succeeded; isolated Export suite passed 13/13 before the
additional tests, and the expanded Export/F14 set passed 18/18. One isolated
F12 test passed. Another isolated F12 regression fails during Lab report
confirmation with `Report binding cannot become effective in the future`,
before entering F14 code; the F12 suite is therefore not green. Shared DB was
not changed.

The draft preview now consumes the plan-specific `scopeRevision` returned by
`GET candidates?planId=...`, rechecks the draft/demand after the F12 read and
returns 409 for a stale revision. This is covered by the Export integration
test. The team has confirmed that output yield, quality calculation and MRL
safety-margin rules are not approved yet. Keep F14 fail-closed: no PASS or
proposal persistence until versioned rules and evidence contracts are defined.

The preview now includes the applicable published MRL version and requested
limits for the order's market/commodity at the F12 snapshot time. It makes no
PASS/FAIL comparison because mandatory substance coverage, comparable material
basis and margin semantics are not approved. An isolated PostgreSQL API test
with a synthetic published version confirmed the version, legal source and
limit in the preview. The test also confirmed that preview did not change
candidate inventory. Docker Desktop was restarted for this verification.

### 2026-10-25 fail-closed assessment core

Added a pure assessment core for explicit versioned yield, required-substance
coverage/MRL margin and quality propagation inputs. It reports per-check
PASS/FAIL/INCOMPLETE findings and never substitutes a default for an absent
rule or observation. The existing draft residue diagnostic now reports
`assessmentStatus=INCOMPLETE` and missing-dependency findings instead of
`NOT_ASSESSED`. Synthetic unit cases cover a passing configured calculation,
missing inputs, MRL failure with missing quality evidence, and conflicting
quality observations. The focused Export/F14 set passed 21/21 in isolated
PostgreSQL. This is not a complete F14 implementation: owner-module policy and
quality-evidence storage/read APIs, assessment-aware greedy integration, preview
reference, coordinated proposal persistence and full stale-path tests remain.
No shared database change was made.

The search core now retries deterministic physical allocations by excluding
one unsuccessful position at a time, with an explicit iteration cap. Synthetic
tests confirm replacement of a physically large but failed lot and distinguish
`SEARCH_EXHAUSTED` from proven infeasibility. This core is not yet connected to
the HTTP preview or owner-module policy/evidence reads, so checklist step 4
remains open. Its output cannot authorize proposal persistence.

Catalog now has an immutable, versioned F14 assessment policy and protected
publish/applicable-read API for a Customer Manager. Ledger has an immutable,
versioned prospective-yield rule tied to a facility, compatibility policy and
Ledger evidence, with Mill Manager publish and owner-scoped read. Both are
forward-only owner migrations; runtime grants expose only protected Ledger
functions, not direct Ledger-table writes. The draft residue diagnostic now
resolves these policies at its F12 snapshot time. Isolated API tests cover
policy publishing, role restrictions, owner isolation, migration/grants and
reading. No rule is seeded, and no shared database was migrated.
The focused Export/F14/policy/yield test set passed 26/26 after these changes;
the Catalog and Ledger migrations were applied only inside disposable test DBs.

The quality-evidence gap remains structural: confirmed Capture Lab results
have no quality-attribute ID or defensible binding to a finished Ledger lot.
The synthetic assessment engine accepts an explicit observation, but no
production read can provide one. Do not invent such a binding from a test
label or a source sample. Accordingly, quality-backed PASS, assessed HTTP
search, proposal storage/recheck and F14 completion are still open.

### 2026-10-26 status and next implementation sequence

The current backend has draft CRUD/history, scoped candidate and lineage reads,
an F12-based diagnostic residue preview, published Catalog assessment policies,
published Ledger yield rules, a fail-closed assessment core and a bounded,
deterministic assessed search core. The search core is internal; no assessed
HTTP preview or proposal endpoint exists. `canPropose` remains false. F14 has
no frontend. The focused Export/F14/policy/yield set last passed 26/26 on an
isolated PostgreSQL database; this is not an end-to-end F14 acceptance result.
No F14 migration has been applied to the shared database.

Remaining work, in dependency order:

1. **Lab/Capture evidence contract (cross-module prerequisite).** Define a
   confirmed quality observation with quality-attribute ID, measured subject
   (including finished Ledger lot when applicable), value/interval, unit,
   measurement/confirmation time, provenance and immutable revision. Define
   which attributes may propagate through blending and which require a new
   finished-product measurement. Expose an authorized, scoped read to F14.
   Never infer finished-lot quality from a test label or source-lot sample.
2. **Coherent planning read and one-allocation assessment (Exports with Ledger,
   Catalog and Lab).** Load demand, stock, HOLD/pool revisions, lineage,
   compatibility, yield, residue/quality evidence, assessment policy and
   published MRL under a documented consistency boundary. Validate the entire
   allocation group, run hypothetical F12 blending and quality calculations,
   and return per-check values, limits, evidence versions and PASS/FAIL/
   INCOMPLETE findings. Missing required evidence remains INCOMPLETE.
3. **Assessed preview API (Exports).** Wire the bounded greedy search to the
   assessment service and expose `POST /api/blend-plans/{id}/preview`. Return
   allocations, predicted output mass, findings and search diagnostics. Store
   an expiring, server-verifiable preview reference only for a result eligible
   for proposal. `SEARCH_EXHAUSTED` means the bounded search did not find a
   solution, not that none exists.
4. **Atomic proposal API and history (Exports plus owner-module coordination).**
   Expose `POST /api/blend-plans/{id}/propose` with operation ID and preview
   reference. Re-read and re-assess dependencies under a lock/transaction
   coordinated with stock and policy/evidence writers. Persist allocations,
   predicted output, findings, policy/MRL/evidence revisions and immutable
   history atomically with the idempotent response. Reject stale references;
   replay exact retries and reject an operation ID reused with changed input.
   F14 must not reserve, HOLD, post or deduct stock.
5. **Freshness and verification.** Report why a historical proposal is stale
   when demand, stock, location, HOLD, pool, Lab evidence, residue rules, MRL
   or assessment policy changes. Test the full F13-to-F14 API flow and failure
   paths against isolated PostgreSQL, including concurrent stock writes,
   access scope, exact retries and proof that stock/postings/HOLD are unchanged.
   Update API examples and deployment instructions. Shared-database migration
   remains a separate approval gate.

Work that can proceed before the Lab contract is available: design and test
the preview-reference, idempotent proposal storage and coordinated recheck
infrastructure, plus synthetic assessment/search tests. Keep production
proposal eligibility fail-closed until the owning modules provide confirmed
quality evidence and approved propagation rules. F15 approval/reservation and
loading are downstream work, not F14 completion requirements.

### 2026-10-26 implementation attempt

Added `POST /api/blend-plans/{id}/preview` to run the bounded selector with
candidate group checks, applicable assessment/yield policies, hypothetical F12
residue calculation and MRL lookup. It rechecks candidate and demand revisions
before returning. Because confirmed quality observations for finished Ledger
lots are still unavailable, it returns no preview reference and
`canPropose=false`. The blend selector now requests enough input mass for an
explicit published yield fraction instead of always stopping at the output
target mass. Backend build and eight isolated logic tests passed. The API
integration suite could not start: Docker's engine pipe was unavailable; its
reported failures were fixture startup failures, not F14 assertions. The new
API test remains to be rerun when Docker is available. No shared DB changed.

This is diagnostic preview progress, not completion of steps 2–7. Proposal
persistence, authoritative Lab quality binding, full stale checks and end-to-end
verification remain required.
