# Container loading and closing plan

Status: planning only. No container workflow has been implemented by this plan. Export functionality is being built by a teammate and must not be modified as part of this work.

## 1. Approach and ownership

Implement container loading and closing within the existing Ledger and working web app. Reuse stock, protected commands, exact quantities, lineage, evidence and authorization patterns. Consume the teammate-owned export approval contract; do not recreate export orders, plans, approvals or compliance calculations.

The dependency is a stable integration contract, not completion of the entire export module. Container work can proceed once that contract and the blocking business rules are agreed. End-to-end acceptance requires the real export implementation; no fake approval, permissive fallback or production stub is acceptable.

### In scope

- Container identity, lifecycle, manifest and history.
- Recording actual loads with atomic stock accounting and source attribution.
- Checking approved-plan authorization, current stock restrictions and container HOLDs.
- Seal and closure records under explicitly agreed business rules.
- Container list/detail, loading and closing UI/API.
- Container-to-source and source-to-container traceability.
- Container-specific integration and regression coverage.

### Out of scope: teammate-owned export work

- Export orders, destination selection and commercial requirements.
- Blend-plan preparation, feasibility, optimization, approval and rejection.
- Residue propagation, destination assessments, legal-version selection and safety-margin calculations.
- Export documents, compliance dossiers, shipment dispatch, customs integration and buyer portals.

Do not modify the teammate's export files, contracts or migrations unilaterally. Agree any required integration changes with the teammate. Do not add a second export data model or separate service.

## 2. Requirements confirmed by the proposal

Source: `GrainLedger_V2.pdf` (September 30, 2026; supplied separately).

| Area | Confirmed requirement | Proposal reference |
| --- | --- | --- |
| Approval | Only a Customer Manager approves a plan; loading follows an approved plan. | pp. 6, 8–9; F15–F16 |
| Actor | Export Staff loads and closes containers. | pp. 7–8; F16 |
| Ownership | Each lot has one owner; customer companies cannot mix ownership or access each other's data. | pp. 3, 7, 9 |
| Stock | Do not exceed available mass or spend the same mass twice. | pp. 2, 5–6, 9 |
| Accounting | Exact decimal quantities, transactional validation and append-only history; corrections use compensating records with reasons. | pp. 1, 3–4, 9 |
| Compliance | Approved plans must meet destination MRL minus the Manager-configured safety margin using retained applicable legal version and sufficient evidence. The export owner supplies this decision. | pp. 9–10 |
| Traceability | Trace a container to plots and proportions, and a bad result to all affected containers and contributed mass. | pp. 5–7, 9; F18 |
| Restrictions | Lot/container HOLDs, notifications and event history are required. | p. 9; F19–F20 |

The proposal's 25,000 kg container is an example, not a universal capacity or closing threshold. It does not define state names, required seal fields, bag-only loading, reservations, shortage tolerance, reopening, unloading or closure-versus-dispatch semantics.

## 3. Existing implementation and reuse points

These are repository findings, not proof of deployed migrations or runtime availability.

- [Ledger endpoints](../../backend/src/GrainLedger.Modules.Ledger/LedgerEndpoints.cs) provide stock, operations, transfers, lot HOLDs, corrections and history, but no container/loading/closing routes.
- [Ledger schema](../../backend/src/GrainLedger.Modules.Ledger/Sql/Schema.sql) has lots, positions, postings and source contributions. Current HOLDs target lots, not containers.
- [Processing commands](../../backend/src/GrainLedger.Modules.Ledger/Sql/Operations.sql) implement atomic input debits, output credits and deterministic integer-micro-kg lineage allocation. They are not loading commands.
- [Architecture](../architecture.md) requires shared advisory lock `714928352`, `READ COMMITTED`, caller-owned transactions, persisted role/scope checks and mass as decimal strings normalized to `numeric(20,6)` kg.
- Current `ledger.blocked` checks lot HOLDs, receiving restrictions and correction restrictions. Lot HOLDs do not automatically cascade to descendants. HOLD responses do not release stock.
- [Permission grants](../../backend/src/GrainLedger.Modules.Identity/Authorization/Permissions.cs) include `containers.write` for ExportStaff and `containers.operate` for MillOperator, but neither currently has a container endpoint. Dormant grants do not settle the business workflow.
- [Working app](../../apps/web/src/App.tsx) has no export/container workspace. Container pages in the nested [prototype router](../../apps/web/grainledger/src/app/router/routes.tsx) are placeholders, not implementation or approved policy.
- [Laboratory contract](../api/sample-api.md#laboratory-report-routes) preserves immutable procurement evidence. Its current VN numeric baseline comparison is not a persisted destination assessment or authorization to load. Do not reuse it as a global PASS flag.
- No mass reservation exists. `lot_code_reservations` reserves human lot codes, not stock.

## 4. Integration contract with the export owner

The following is a proposed contract checklist, not an existing API. Agree its shape and ownership before implementation; reuse the teammate's established contract rather than inventing a parallel one.

The backend must be able to obtain authoritative:

- Approved plan ID, exact version and current approval/eligibility state.
- Owning company, export order, destination and the facility/location scope applicable to loading.
- Permitted actual lot IDs or an explicit mapping from approved plan inputs to the resulting finished lots. An upstream processing input must not be mistaken for loadable finished stock.
- Authorized quantities, units and agreed allocation across containers, including how cumulative loads exhaust the approval allowance.
- Approval actor/time and immutable assessment references or snapshots needed to explain why the load was authorized.
- Rules for plan revision, revocation, new evidence and revalidation between approval, loading and closing.

Browser-supplied owner, approval, actor or compliance flags are never authoritative. Backend verifies persisted current state.

Stock acceptance and approval validation must not race with plan changes. Agree a shared transaction/locking or equivalent version-consistency contract with the export owner. Sequential HTTP checks alone do not establish atomic authorization. Cross-module integration uses existing in-process contracts, never shared EF entities or unrestricted writes to another module's tables.

If the real approval contract is unavailable, loading cannot be enabled through a fallback. Do not create a temporary local export implementation.

## 5. Proposed container design

The choices in this section are engineering recommendations, not proposal-confirmed business rules. Resolve section 8 before finalizing behavior.

### Lifecycle

`DRAFT` → first accepted load → `LOADING` → successful closure → `CLOSED`.

Additional accepted loads remain `LOADING`. HOLD is an independent restriction, not a replacement lifecycle state. A closed container can acquire a later HOLD without rewriting its closure or historical authorization.

Do not add reopen, unload, reseal or cancellation transitions until their authority and append-only accounting rules are agreed. Closing does not imply shipment dispatch.

### Minimal records

- Container: internal ID, company, physical identifier, order, facility, approved plan/version, lifecycle and concurrency version.
- Load event: container, actual lot/location, original quantity/unit, normalized kg, operation ID, authenticated actor, occurrence time and recording time.
- Attribution: immutable source/plot contributions for accepted loads, derived from existing lineage.
- Closure: exact manifest/load set, total mass, approval/assessment references, required seal details, actor and timestamps.
- Container HOLD/history: explicit target, reason, actor and supporting evidence under the agreed handling policy.

Do not assume container-ID standards, seal uniqueness, bag counts, gross/tare/VGM fields or fixed capacity without a business requirement. Sample seal identity and container seal identity are separate records.

### Accounting

Recommended boundary: an accepted physical load moves mass out of usable source-position stock into accountable container custody. Loading must not make mass disappear from the ledger or count it twice. Choose the concrete custody/posting representation within Ledger's existing protected-command model.

Closing freezes the manifest and decision references; it causes no second stock debit.

Draft selections do not reserve stock. Approved plans are not guaranteed inventory. Recheck source availability and remaining plan allowance for every accepted load, including concurrent loads into different containers. Add mass reservations only if the agreed export contract requires them.

Reuse exact deterministic allocation conventions. Preserve original lineage and immutable events; repeated partial loads must conserve loaded and remaining mass without cumulative source over-allocation. Extend downstream-consumption/correction checks so loaded stock cannot later be reversed as unused stock.

### Permissions and restrictions

- ExportStaff: create and operate own-company containers according to F16 and authoritative approved plans.
- CustomerManager: existing plan approval remains export-owned; container HOLD creation and own-company tracing belong to the agreed container scope.
- MillManager: retain assigned-facility physical HOLD handling. Any container-specific handling extension needs explicit rules.
- Administrator: no wildcard business authority.
- MillOperator: do not infer a second confirmation step or loading authority from unused `containers.operate`. Reconcile this grant with the approved role contract before enabling endpoints.

Every load rechecks owner, applicable facility/location, plan/version/allowance, positive exact mass, available stock and effective restrictions under the stock concurrency boundary. Closing rechecks the current manifest and authorization/restrictions required by agreed policy. Do not require the already-loaded mass to remain available in its former source position.

No container action, report confirmation, HOLD response or correction implicitly releases a HOLD. Automatic descendant/container HOLD triggers and release remain undefined until explicitly approved.

## 6. Ordered action items

- [ ] Agree section 4 integration contract with the export owner and resolve section 8 blocking rules. Identify shared files and migration boundaries; do not modify export implementation.
- [ ] Define container-owned schema, versioned command/read DTOs, events, custody accounting and immutable attribution using existing Ledger patterns. Keep original source/stock/evidence history unchanged.
- [ ] Add protected container creation and load commands with persisted authorization, atomic approval/stock validation, cumulative allowance checks and replay/conflict handling. Extend stock/correction queries affected by container custody.
- [ ] Add closure and container HOLD/history commands under agreed rules. Preserve closure snapshots, prevent double debit, and deny unsupported reversal/reopen/release paths.
- [ ] Add complete forward/reverse container trace queries over load attribution and source lineage. Compute complete affected sets independently of UI pagination and distinguish historical loaded mass from remaining stock.
- [ ] Add scoped, paginated container APIs and working UI under `apps/web/src`: container list/detail, approved-plan selection, actual load entry, manifest review, close action, restrictions, trace and history. Keep export screens and nested prototype untouched.
- [ ] Verify focused integration regressions and the actual API/browser flow using real teammate export integration in an isolated test environment. Update container contracts and affected architecture/workflow documentation after implementation.

Proposed route family, subject to existing conventions and the agreed contract:

| Route | Purpose |
| --- | --- |
| `GET /api/ledger/containers` | Scoped paginated list |
| `POST /api/ledger/containers` | Create container against authoritative approved plan |
| `GET /api/ledger/containers/{id}` | Manifest, totals, restrictions and history |
| `POST /api/ledger/containers/{id}/loads` | Append accepted physical load |
| `POST /api/ledger/containers/{id}/close` | Append immutable closure |

Define container HOLD and trace routes alongside existing patterns after target/scope policy is agreed. These routes do not exist yet.

## 7. Acceptance and verification

- Reject cross-company identifiers, inactive/unauthorized actors and forged owner/approval/actor fields. Preserve scoped mill access and normal cookie CSRF requirements.
- Reject missing/rejected/revoked/stale approvals and unauthorized actual-lot substitutions or quantity deviations according to the agreed export contract.
- Reproduce the proposal example: loading 25,000 kg from 70,000 kg leaves 45,000 kg usable source stock and 25,000 kg accountable container contents. A subsequent 50,000 kg load is rejected.
- Race processing, transfers and multiple container loads against the same stock; no overspend. Race loads against remaining plan allowance; no over-fulfillment. Race plan changes against loading/closing; enforce the agreed consistency contract.
- Roll back all stock, custody, manifest and attribution writes on failure. Identical operation replay adds no duplicate load/closure; changed replay conflicts. Stale container versions cannot overwrite newer work.
- Verify decimal boundaries and deterministic partial-load allocation. Attribution sums exactly to accepted load mass; repeated loads and remaining balances conserve source mass.
- Block affected loading for lot/container HOLDs and existing receiving/correction restrictions. Exercise HOLD/load/close races. HOLD response, report intake and correction do not release restrictions.
- Prevent corrections from reversing stock already loaded without the explicitly approved compensation workflow.
- Do not allow VN baseline findings or browser flags to substitute for teammate-owned destination eligibility. Preserve the exact historical approval/assessment references.
- Enforce agreed closing completion, identifier and seal rules. Close once without a second stock debit; reject further ordinary loads/manifest edits after closure.
- Find every affected container from a late source result, and every source plot with exact attributable mass from a container. Later evidence/HOLDs do not overwrite the original manifest or decision.
- Exercise the working desktop/mobile UI through the real API: eligible stock selection, exact totals, visible server blockers, stale-state handling and scoped evidence/history. No mock approval or intercepted success counts as end-to-end acceptance.

Use isolated PostgreSQL Testcontainers and the repository's existing test/browser-host patterns. Shared Supabase migrations, seeds, schema/grant changes and write-based smoke scenarios require separate explicit database-owner approval. Never switch configured databases, elevate runtime credentials or migrate during routine startup.

## 8. Open business decisions

1. **Loading eligibility and execution:** Which product stages are loadable? Bagged or bulk? Exact plan quantities or permitted deviations? Partial containers? Does approval reserve stock or only authorize it? Who records physical loading, and is any separately authorized mill confirmation required? How are finished lots mapped to plan inputs and allowances shared across containers?
2. **Authorization freshness and late evidence:** The export owner must define when destination eligibility is valid, what invalidates approval, and what revalidation is required during loading/closing. Container HOLD target scope, descendant impact, manual/automatic triggers and any release authority need explicit policy; no automatic cascade or release is assumed.
3. **Closing and corrections:** Which container/seal identifiers and weight/completion rules are mandatory? Does closing mean sealing only or also dispatch? What evidence and authority permit unloading, reopening, resealing, cancellation or compensating corrections? No universal 25,000 kg threshold or invented shortage tolerance.

Unresolved rules are blockers for the affected commands, not permission to invent defaults. Finish the reachable container work without duplicating or modifying the teammate-owned export workflow.
