# Ledger implementation and verification

Status: Receiving-loss threshold implemented. Procurement registers source expectation without creating usable Ledger stock; exact-match or within-threshold operator acceptance posts measured arrival mass, and above-threshold/surplus cases require assigned MillManager evidence-backed resolution. Capture route contract is in [procurement-api.md](procurement-api.md). Historical source evidence and stock are grandfathered unchanged; receiving migrations do not retroactively freeze, reinterpret or assign thresholds to legacy records. Shared-database migrations, seeds and schema/role/grant changes require explicit database-owner approval.

## 1. Scope and sources

Implemented baseline follows source documents listed below. Source diagrams inform business semantics; schema, transaction, immutable payload and locking are engineering decisions, not claims source diagrams mandate those mechanisms.

Confirmed decisions:
- Deliver usable ledger with real prerequisites and basic working UI.
- Adopt qualified stage matrix as implementation baseline, not factory approval.
- Receiving loss uses a default 2% threshold with optional company overrides; configuration changes require authorized Administrator and append-only history.
- One procurement represents one complete arrival; use separate procurement per load. No truck model, partial arrivals, customer settlement or surplus acceptance.
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

- Identity, Catalog, Capture and Ledger are host modules with owned schemas/migrations. No EF entities cross module boundaries. Official Capture procurement is integrated with Ledger; retired MockCapture modules, routes and `--seed-mock` remain unavailable. Development-only `--seed-ledger` creates clearly labeled demo mill setup and stock through the official Catalog/Capture/Ledger APIs; see [development accounts](development-accounts.md).
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

Basic setup records support scoped, version-checked edits by their current creator; unknown legacy creators remain read-only. Name edits are allowed while referenced; structural retirement of in-use resources returns conflict. Deletes are soft deletes, not erasure: persisted rows/history remain, active lists/details/selectors/counts hide tombstones, IDs/codes stay reserved, and restore is unsupported. Capture farmer/plot/season/plot-season edits and deletes are limited to unused records owned by the current actor; procurement, source, evidence, history and committed stock remain immutable.

Catalog and Capture lists and large Ledger queues use bounded server-side search/filter/sort/pagination, with supported filters scoped and sort fields allowlisted. Their paged responses include `items`, `page`, `pageSize`, `totalCount`, `hasMore`; page size defaults to 25 and caps at 100. Basic Ledger setup endpoints return small arrays that the UI filters, sorts and pages locally. Selectors use bounded searchable lists and may include only authorized pinned selections. Pager displays `<< < 1 2 3 > >>` with up to five numeric page links and accessible first/previous/next/last names. Catalog mutations require current ETag (`If-Match`); Capture edits require `expectedVersion`, deletes send version. Operation drafts and replacement operation drafts are editable with matching version; correction requests, committed operations/postings and dependent history are immutable.

### Receipt and receiving

Procurement contract uses `POST /api/procurements` for source registration, `GET /api/ledger/receiving-tasks?facilityId={id}&page=1&pageSize=25` and `GET /api/ledger/receiving/{id}` for assigned-facility task/detail, and `POST /api/ledger/receiving/{id}/measurements` and `/reviews` for operator arrival and manager decisions. Exact DTO fields, status names, and migration marker are in [procurement API](procurement-api.md).

- One procurement is one complete arrival, with separate procurement per load and no truck model. Operator submits a complete actual-mass set for every line. Exact normalized match confirms arrival and posts actual mass. A complete positive set with at least one shortage and every line within the snapshotted company threshold posts actual mass as `RECEIVED_WITHIN_TOLERANCE`; any surplus or over-threshold shortage remains pending review with no stock.
- Manager review requires reason and evidence scoped to procurement owner and assigned facility. `APPROVE_SHORTAGE` is valid only if every line is at or below expected and at least one is short; it posts actual mass only. `REQUEST_REMEASUREMENT` appends request/observations; `KEEP_BLOCKED` leaves unexplained shortage/surplus without stock. Surplus acceptance remains unsupported.
- Persist exact decimal strings and normalize supported catalog mass units to positive `numeric(20,6)` kg without rounding. Preserve immutable source expectation separately from actual measurements and append-only measurement/review history.
- Registration, complete measurements, manager decision and accepted stock posting use one shared transaction and Ledger advisory lock `714928352` at `READ COMMITTED`; accepted actual mass and receiving transition are atomic. `receivingVersion` begins at 0 and increments on mutation; stale version/set conflicts. Same operation ID, canonical payload and actor replays saved result; changed payload conflicts.
- Receiving thresholds: system default starts at 2%; authorized Administrators can set company overrides. Exact decimal-string, versioning, own-company privacy, immutable config-history and per-measurement applied-snapshot contracts are documented in [procurement API](procurement-api.md). Threshold changes affect new measurement submissions only; do not recompute pending decisions or old snapshots.

### Operations, variance, units and lineage

- Supported: drying, milling actual fractions, merge, compatible blend, bagging preserving product identity, and unchanged-lot transfer. Stage matrix below is adopted implementation baseline, not factory approval.
- Operation draft supports full edit. Commit checks persisted ownership/permission, facility, positive inputs/outputs, stock, stage, HOLD/correction/receiving restriction, effective policy, compatibility and transfer constraints.
- Processing form derives non-blending output commodity from first input lot. Drying derives `DRIED_PADDY`; milling selects only actual supported fractions; merge inherits input stage and product identity; bagging derives `BAGGED_RICE` while retaining underlying identity. Blending retains explicit output commodity selection and approved compatibility checks. Deterministic stage/identity fields are not editable; editing an existing draft reapplies these rules without rewriting committed records.
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

Required current-suite coverage: concurrency/overspend and atomic rollback; authorization and facility scope; idempotency/conflict; policy/compatibility fail closed; variance confirmation invalidation; exact lineage rounding; transfer identity; HOLD and processing-correction races; immutable evidence/history and one-time reversal; unit precision and procurement registration without stock; exact arrival, mismatch block, shortage approval, append-only remeasurement, unresolved discrepancy, stale version/set, scoped evidence, and runtime direct-mutation denial. Integration tests use PostgreSQL/Testcontainers, not in-memory DB.

Historical synthetic workflow descriptions and earlier arrival/correction browser reports are historical only; they do not establish current receiving behavior or migration acceptance. Historical Capture rows with `receiving_workflow IS NULL` and existing Ledger stock remain grandfathered unchanged; threshold snapshots remain absent for pre-feature measurement sets.

Earlier implementation owner reported completed checks; those results remain historical:

- Initial `dotnet build GrainLedger.slnx` succeeded; earlier `dotnet test GrainLedger.slnx --no-build`: 87 passed, 0 failed.
- Earlier `npm test`: 14 passed; `npm run build` passed.
- Earlier synthetic browser and cutover reports are historical only; they do not verify current physical receiving behavior or its migration.

Receiving-threshold implementation verification (isolated PostgreSQL, real HTTP/cookie sessions):

- Backend build passed with existing NU1902/NU1903 Pkcs 10.0.0 and unread-access CS9113 warnings. Full `dotnet test GrainLedger.slnx --no-build`: 273 passed, 4 failed of 277. All 34 `Receiving*` cases passed, including migration, threshold concurrency/strict input, and inherited-column runtime denial. Three unrelated SampleTests fail because SeedSourceAsync commits event payload `{}` before procurement and provenance trigger rejects it. Identity reciprocal admin-demotion race returned 401 + 200 where existing test expects 409.
- Web `npm test`: 87 passed across 17 files; `npm run build` passed.
- Isolated browser: company A default 2%, 100 kg → 98 kg accepted with 98 kg stock; 100 kg → 97.5 kg awaited manager with zero stock. Administrator changed default 2% → 3% with append-only server actor/time/reason history and created company B 3% override. B accepted 100 kg → 97.5 kg with 97.5 kg stock and `COMPANY_OVERRIDE` revision; own-company read exposed 3% without broad Setup access. Restoring default 2% left earlier pending/accepted snapshots unchanged. Manager approved A's 97.5 kg shortage with reason and scoped evidence; actual mass posted, stored 2% snapshot and config remained unchanged.
- Setup/manager browser layout at 375 px had no body overflow; tables used local horizontal overflow; browser errors empty. No shared database migration, seed or data-changing check was performed.

Backend suite was not fully green. Shared database deployment not verified. Live Google consent, factory approval and real-stock pilot approval are not established by these checks.

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
| Tolerance | No confirmed effective processing policy → no processing commit, even exact balance. Synthetic values only in test/demo. | Confirmed operation percentage/effective date/evidence. |
| Compatibility | No explicit compatible input-product rule → no blend. | Approved combinations/output identity. |
| Receiving discrepancy | Exact operator match or complete within-threshold set posts measured actual; over-threshold shortage/surplus stays pending for manager; manager shortage approval posts actual only; remeasurement appends. | No customer settlement, partial arrival or surplus approval. |
| Byproduct dispatch/disposal | Outputs remain stock, no fabricated outbound posting or hiding in loss. | Approved dispatch/disposal workflow. |
| HOLD release | No release action; response/isolation/read/correction do not clear it. | Explicit authority/evidence/audit/multi-HOLD rule. |
| Receipt correction | No current correction endpoint; existing stock/source history remains unchanged. | Approved endpoint and reviewed provenance workflow. |
| Wrong owner | Reject source/owner change in this workflow. | Explicit controlled provenance workflow. |
| Reprocessing | Undefined repeated stages/actions rejected. | Explicit operation/stage/product/mass-balance rules. |

A blocked lot/action does not block unrelated eligible work. No live-stock pilot may rely on omitting unsupported physical movement from ledger.
