# Ledger implementation and verification

Status: Ledger supports protected stock, processing, history and evidence; official Capture farmer and procurement workflows are integrated with Ledger stock. Capture route contract is in [procurement-api.md](procurement-api.md). No automatic migration or conversion of prior synthetic source records/history occurs; existing Ledger stock/history remains. Official Capture has no arrival-observation, receiving-correction or receipt-correction endpoints. Shared-database migrations, seeds and schema/role/grant changes require explicit database-owner approval.

## 1. Scope and sources

Implemented baseline follows source documents listed below. Source diagrams inform business semantics; schema, transaction, immutable payload and locking are engineering decisions, not claims source diagrams mandate those mechanisms.

Confirmed decisions:
- Deliver usable ledger with real prerequisites and basic working UI.
- Adopt qualified stage matrix as implementation baseline, not factory approval.
- Keep operational tolerance unconfigured until confirmed; synthetic values only for clearly labeled tests/demo.
- Record and block receiving discrepancies only where approved Capture endpoints exist; current official Capture has no arrival-observation or receiving-correction API.
- Correct unused procurement receipts with immutable evidence and provenance only when correction endpoint and approved workflow exist; current Capture has no receipt-correction API.
- Give Mill Operators task-scoped reads in assigned facilities, never customer-wide access.
- Fail closed where approved business rules or API contracts are absent.
- Preserve accepted deferred reservation-correction contract: correction `RELEASE` requires correction correlation but no movement linkage; only `CONSUME` and `REVERSE_CONSUME` require movement linkage. Reservation implementation remains excluded from this milestone.

Sources in `/home/asemika/Documents/ObsidianVault/MyVault/captone/`:
- `docs/GrainLedger_V2.pdf`, pp. 3–9: balance, immutable history, ownership, F08–F10.
- `diagrams/erd_v2/rev_3/BUSINESS_RULES.md`: transaction, lineage, correction and HOLD semantics.
- `diagrams/dbdiagram/rev_1/grainledger.dbml`, `REVIEW.md`: PostgreSQL mapping and mapping discrepancies.
- `diagrams/usecase/rev_3/plantuml/NOTES.md`: actor responsibilities/workflow boundaries.

These source revisions were inspected. Diagram design is not proof of factory/supervisor approval. Physical schema is mapping reference, not instruction to replace Identity implementation.

### Included

- Facilities, locations, equipment, commodities, occupancy/clearance, staff assignments, evidence-backed policy and compatibility setup.
- Catalog-backed farmer, plot, season, procurement and source-lot creation; processing, transfer, mass balance and lineage.
- HOLD handling, notifications, immutable evidence, and processing corrections.
- Scoped stock, composition, history and operational UI.

### Separate milestones

Not included: offline capture, pesticide declarations, sampling/lab workflows, residue propagation/compliance, export orders, blend approval, reservation, loading/container traceability, and settlement of genuine transport shortage/gain. Physical blending records processing; it does not certify export compliance. No factory approval claimed.

## 2. Architecture and database boundary

See `docs/architecture.md` for module structure, role provisioning, UI workflow map, runtime/migration deployment steps, exact numeric and lineage guarantees, and explicit exclusions.

- Identity, Catalog, Capture and Ledger are host modules with owned schemas/migrations. No EF entities cross module boundaries. Official Capture procurement is integrated with Ledger; no synthetic Capture module, route or seed command is active.
- Capture procurement and Ledger lot/posting/package writes share one PostgreSQL transaction; module routines never commit caller transaction.
- Migration order is Identity → Catalog → Capture → Ledger. Preserve generated migration history and historical Ledger data. No automatic conversion of legacy synthetic records. Migration/seed on shared database requires explicit owner approval.
- Runtime login is pre-created, restricted and non-owner; ordinary startup never migrates. Never use postgres superuser as runtime or point Development migration at production/a teammate database.
- Ledger mutation goes through finite protected PostgreSQL commands; runtime cannot directly mutate Ledger tables/projections. Protected routines enforce persisted actor checks and accounting invariants.
- Source/stock writes acquire advisory lock `714928352` before source/stock rows; `READ COMMITTED` required. One global lock intentionally serializes operations.
- No broker, separate service, generic workflow engine or frontend framework.

## 3. Implemented workflow contracts

### Setup and permissions

Setup configures facilities, locations, equipment, commodities, staff assignments, compartment occupancy/clearance, evidence-backed effective tolerance policies and blend compatibility. Customer/staff safe lookups expose only needed non-sensitive IDs/names. Authorization checks persisted actor, permission, owner and facility; UI gating is not security.

Administrator manages platform/accounts, but has no inherited processing, variance approval, processing correction, HOLD response or other business authority. Mill Manager and Mill Operator require explicit permission and assigned facility. Operator data excludes farmer contacts and unrelated sensitive source/evidence fields. Purchasing Staff owns own-company Capture farmer and procurement data.

### Receipt and receiving

Official Capture source routes include `/api/farmers`, `/api/plots`, `/api/crop-seasons`, `/api/plot-seasons`; procurement contract is `/api/procurement-reference-data`, `/api/procurements/commit`, `/api/procurements`, `/api/procurements/{id}`, and `/api/source-lots/{id}`. Procurement commit persists Capture source records and writes corresponding Ledger stock atomically using persisted Catalog-to-Ledger commodity mapping. Historical synthetic records are not converted; existing Ledger stock/history remains.

- Procurement lines commit one RAW_PADDY source lot and matching protected initial stock per accepted line, atomically with source records.
- Persist measurement as decimal strings with supported catalog mass units KG, G, T and exact positive `numeric(20,6)` kg normalization; inexact conversion, precision excess and overflow fail.
- Farmer, plot, season, owner, plot-season, commodity and lot provenance agree. Receipt items and normalized rows stay coherent at commit.
- Same operation ID and content returns prior result; changed content returns conflict.
- Official Capture currently has no arrival-observation, receiving-correction/investigation or procurement-receipt-correction endpoints. No corresponding UI/API workflow is available; fail closed.

### Operations, variance, units and lineage

- Supported: drying, milling actual fractions, merge, compatible blend, bagging preserving product identity, and unchanged-lot transfer. Stage matrix below is adopted implementation baseline, not factory approval.
- Operation draft supports full edit. Commit checks persisted ownership/permission, facility, positive inputs/outputs, stock, stage, HOLD/correction/receiving restriction, effective policy, compatibility and transfer constraints.
- Exact signed `variance_kg = input − output − declared_loss`; allowed tolerance = input × confirmed percentage / 100. Missing applicable policy rejects even exactly balanced commit. No silent zero/PDF illustrative percentage.
- Nonzero variance needs manager confirmation bound to reviewed exact draft version/content and evidence; edit invalidates confirmation. Client `confirmedBy` never authorizes. Declared loss is separate from signed variance; neither variance nor loss hides byproduct stock.
- Quantities use exact decimal and `numeric(20,6)` kg. Original units/measure remain available. Invalid precision/conversion fails instead of rounding silently.
- Largest-remainder lineage uses integer micro-kg: for each output allocate across inputs, then each input share across its source contributions; UUID tie-break at each allocation; combine equal source rows. Contributions exactly sum output mass. Transfer adds no lineage.

### Corrections, HOLD and evidence

- Customer Manager creates own-company HOLD. Manager assigned facility can acknowledge, isolate, respond with scoped evidence; notification creation/read does not release. No HOLD release action exists.
- Operator requests processing correction; request blocks original output. Manager reconciliation evidence binds exact current physical positions and reviewed complete replacement draft version/content/output locations, including transferred positions. Commit rechecks; editing replacement invalidates reconciliation. Transfer history is preserved and explicitly compensated, not auto-unwound. Original postings remain; each reversal is unique. Downstream consumption rejects direct correction; cancellation needs evidence that original was correct.
- Independent HOLD/restrictions cannot be cleared by correction. Held original cannot be corrected as a release path; replacement retains applicable HOLD.
- Evidence bytes, committed source receipt/header/items and postings remain durable and immutable; downloads require current owner/facility scope. Official Capture does not expose arrival observations or receipt corrections.

Exact routes/DTO implementations: [Capture module](../backend/src/GrainLedger.Modules.Capture/), [Catalog module](../backend/src/GrainLedger.Modules.Catalogs/), and [Ledger module](../backend/src/GrainLedger.Modules.Ledger/). See [procurement API](procurement-api.md). Retired `/api/capture-mock/*` routes return 404.

## 4. Stage matrix — implementation baseline

Enforced implementation baseline; not externally approved factory rules.

| Operation | Inputs | Outputs |
| --- | --- | --- |
| Receipt | Registered procurement line | RAW_PADDY |
| Drying | RAW_PADDY | DRIED_PADDY |
| Milling | DRIED_PADDY | Actual produced HEAD_RICE, BROKEN_RICE, BRAN, HUSK fractions |
| Merge | Same commodity, stage and product identity | Same commodity, stage and product identity, new lot |
| Blending | Explicitly compatible products | BLENDED_RICE |
| Bagging | Head/broken/blended rice | BAGGED_RICE preserving product identity |
| Transfer | Existing lot | Same lot, commodity, stage |
| Compensation | Original eligible postings | Accounting reversal within atomic correction; no reverse-production lineage |

Milling records positive fractions actually produced, not fabricated four outputs. Byproducts remain stock; no dispatch/disposal workflow. Rice label alone does not imply compatibility. Repeated drying, remilling, unpacking, standalone write-off, generic stock adjustment and HOLD release are undefined/unsupported and fail closed.

## 5. Verification and acceptance

Required current-suite coverage: concurrency/overspend and atomic rollback; authorization and facility scope; idempotency/conflict; policy/compatibility fail closed; variance confirmation invalidation; exact lineage rounding; transfer identity; HOLD and processing-correction races; immutable evidence/history and one-time reversal; unit precision and coherent Capture procurement; runtime direct-mutation denial; actual byproducts and product identity. Integration tests use PostgreSQL/Testcontainers, not in-memory DB. Arrival/reconciliation/receipt-correction workflow tests are not current supported acceptance because official Capture lacks those endpoints.

Historical nine-step synthetic workflow: prior test description, not current official Capture acceptance. It included synthetic setup, receipt, processing, mass-balance/lineage and scope assertions; arrival observation and receipt correction are unsupported by official Capture and are not active workflows.

Previously reported verification below is historical only and does not establish current unsupported workflows or current migration acceptance.

Earlier implementation owner reported completed checks; documentation owner did not rerun them. Historical results:

- Initial `dotnet build GrainLedger.slnx` succeeded; earlier `dotnet test GrainLedger.slnx --no-build`: 87 passed, 0 failed.
- Earlier `npm test`: 14 passed; `npm run build` passed.
- Earlier synthetic browser workflows exercised receipts, arrival discrepancy, receipt correction, processing correction, HOLD and scope denials. Arrival and receipt-correction flows are historical and unavailable through official Capture.
- Earlier synthetic Capture cutover reports included synthetic receipt tests/browser actions; they are historical only and do not establish current official Capture corrections or current migration acceptance.

No current checks were run by documentation owner. Live Google consent, factory approval and real-stock pilot approval are not established by historical checks.

Useful commands:

```bash
dotnet build GrainLedger.slnx
dotnet test GrainLedger.slnx --no-build
npm test
npm run build
```

Backend integration tests require Docker/Testcontainers. Browser test host is test-only. Live Google consent requires credentials.

## 6. Fail-closed boundaries

| Area | Implemented behavior | Requirement to broaden |
| --- | --- | --- |
| Tolerance | No confirmed effective policy → no processing commit, even exact balance. Synthetic values only in test/demo. | Confirmed operation percentage/effective date/evidence. |
| Compatibility | No explicit compatible input-product rule → no blend. | Approved combinations/output identity. |
| Receiving shortage/gain | No current arrival-observation or correction endpoint; no balance adjustment. | Approved Capture contract, evidence, authority and accounting treatment. |
| Byproduct dispatch/disposal | Outputs remain stock, no fabricated outbound posting or hiding in loss. | Approved dispatch/disposal workflow. |
| HOLD release | No release action; response/isolation/read/correction do not clear it. | Explicit authority/evidence/audit/multi-HOLD rule. |
| Receipt correction | No current correction endpoint; existing stock/source history remains unchanged. | Approved endpoint and reviewed provenance workflow. |
| Wrong owner | Reject source/owner change in this workflow. | Explicit controlled provenance workflow. |
| Reprocessing | Undefined repeated stages/actions rejected. | Explicit operation/stage/product/mass-balance rules. |

A blocked lot/action does not block unrelated eligible work. No live-stock pilot may rely on omitting unsupported physical movement from ledger.
