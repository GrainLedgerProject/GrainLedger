# Ledger implementation and verification

Status: ledger and synthetic Development/Testing MockCapture workflows implemented. Identity and Ledger remain normal host modules; MockCapture owns durable synthetic sources, receipts and receiving workflows in isolated `capture_mock`. It is unavailable in Production and has no automatic migration to future real Capture. Earlier verification notes predate this MockCapture cutover; latest cutover verification is recorded in section 5.

## 1. Scope and sources

Implemented baseline follows source documents listed below. Source diagrams inform business semantics; schema, transaction, immutable payload and locking are engineering decisions, not claims source diagrams mandate those mechanisms.

Confirmed decisions:
- Deliver usable ledger with real prerequisites and basic working UI.
- Adopt qualified stage matrix as implementation baseline, not factory approval.
- Keep operational tolerance unconfigured until confirmed; synthetic values only for clearly labeled tests/demo.
- Record and block receiving discrepancies without generic stock adjustment.
- Correct unused procurement receipts with immutable evidence and provenance.
- Give Mill Operators task-scoped reads in assigned facilities, never customer-wide access.
- Fail closed where approved business rules are absent.
- Preserve the accepted deferred reservation-correction contract: correction `RELEASE` requires correction correlation but no movement linkage; only `CONSUME` and `REVERSE_CONSUME` require movement linkage. Reservation implementation remains excluded from this milestone.

Sources in `/home/asemika/Documents/ObsidianVault/MyVault/captone/`:
- `docs/GrainLedger_V2.pdf`, pp. 3–9: balance, immutable history, ownership, F08–F10.
- `diagrams/erd_v2/rev_3/BUSINESS_RULES.md`: transaction, lineage, correction and HOLD semantics.
- `diagrams/dbdiagram/rev_1/grainledger.dbml`, `REVIEW.md`: PostgreSQL mapping and mapping discrepancies.
- `diagrams/usecase/rev_3/plantuml/NOTES.md`: actor responsibilities/workflow boundaries.

These source revisions were inspected. Diagram design is not proof of factory/supervisor approval. Physical schema is mapping reference, not instruction to replace Identity implementation.

### Included

- Facilities, locations, equipment, commodities, occupancy/clearance, staff assignments, evidence-backed policy and compatibility setup.
- Farmer, plot, season, receipt and source lot creation; processing, transfer, mass balance and lineage.
- HOLD handling, notifications, immutable evidence, processing and unused-receipt corrections.
- Arrival observations, correction/investigation and restriction propagation.
- Scoped stock, composition, history and operational UI.

### Separate milestones

Not included: offline capture, pesticide declarations, sampling/lab workflows, residue propagation/compliance, export orders, blend approval, reservation, loading/container traceability, and settlement of genuine transport shortage/gain. Physical blending records processing; it does not certify export compliance. No factory approval claimed.

## 2. Architecture and database boundary

See `docs/architecture.md` for module structure, role provisioning, UI workflow map, runtime/migration deployment steps, exact numeric and lineage guarantees, and explicit exclusions.

- Identity and Ledger are host modules with owned schemas/migrations; MockCapture is a durable synthetic Development/Testing module with isolated `capture_mock` schema and history table. No EF entities cross module boundaries.
- MockCapture source/receipt records and Ledger lot, posting, package guards share a PostgreSQL transaction; no cross-module commit.
- Runtime login is pre-created, restricted and non-owner. Production migration order excludes MockCapture. Development/Testing migration applies its isolated schema only in those environments; ordinary startup never migrates. Never use postgres superuser as app runtime or point Development migration at production/a teammate database.
- Ledger mutation goes through finite protected PostgreSQL commands; runtime cannot directly mutate Ledger tables/projections. Protected SECURITY DEFINER routines use fixed search path, revoked PUBLIC execute, persisted actor checks and DB-enforced invariants.
- Source/stock writes acquire transaction advisory lock `714928352` before source/stock rows. READ COMMITTED required so statement snapshots refresh after lock wait; other isolation levels reject. One global lock is intentional serialization/throughput ceiling. Ordered per-lot/location locks only if measured contention warrants complexity.
- No broker, separate service, generic workflow engine or frontend framework.

## 3. Implemented workflow contracts

### Setup and permissions

Setup configures facilities, locations, equipment, commodities, staff assignments, compartment occupancy/clearance, evidence-backed effective tolerance policies and blend compatibility. Customer/staff safe lookups expose only needed non-sensitive IDs/names. Authorization checks persisted actor, permission, owner and facility; UI gating is not security.

Administrator manages platform/accounts, but has no inherited processing, variance approval, correction/receiving reconciliation, HOLD response or other business authority. Mill Manager and Mill Operator require explicit permission and assigned facility. Operator data excludes farmer contacts and unrelated sensitive synthetic source/evidence/reconciliation fields. Purchasing Staff owns own-company synthetic farmer/purchasing data in Development/Testing.

### Receipt and receiving

Synthetic source/receiving routes are `/api/capture-mock/*`, Development/Testing only. `/api/capture-mock/status` returns `{ "synthetic": true }` when enabled. Sources & receiving UI hides when unavailable; Ledger sections remain usable. Committed synthetic receipts create durable receipt-derived Ledger stock. No automatic migration to future real Capture.

- A receipt draft has no stock effect. Commit creates one RAW_PADDY source lot and matching credit per positive line, atomically with persisted source records.
- Persist original quantity text/unit, including leading zeroes. Normalize exactly: kg = quantity; g = quantity / 1000; t = quantity × 1000. Original text permits at most six fractional digits; normalized kg must fit positive `numeric(20,6)` up to `99999999999999.999999`, without rounding. `0.001 g` converts to `0.000001 kg`; `0.0001 g` is rejected. Reject exponent notation, excess precision, overflow and nonpositive inputs. Decimal strings cross API, never floating-point mass math.
- Farmer, plot, season, owner, plot-season and lot provenance agree. Durable receipt header/document and normalized rows must remain coherent at transaction commit.
- Mill Operator with `lots.receive` records actual arrival measurement/time/evidence, never overwrites procurement quantity. Mismatch blocks source and descendants against use/transfer under same lock.
- Mill Manager with `receiving.reconcile` appends immutable measurement corrections or investigation. Exact match can remove only receiving restriction; independent HOLD/correction stays. `REAL_DIFFERENCE` remains blocked. No receiving balance adjustment or tolerance.
- Purchasing receipt correction request is restricted to unused same-owner source: exact owner/location/farmer/season/ordered source identities and line count remain fixed. Fresh receipt/line/lot UUIDs; corrected time/measure/code allowed. Any committed subsequent movement, including transfer, processing or downstream use, rejects this simple path. Request blocks original stock; any existing current arrival measurement must match replacement quantity, and `REAL_DIFFERENCE` cannot be bypassed. Replacement is immutable after request. Manager with `corrections.reconcile` submits durable evidence and exact physical quantities for every replacement line; the same permission is required for replacement commit. Commit rechecks under the shared lock and atomically reverses original credit, creates replacement source lots/credits and completes Capture state.
- Human lot code stays unique to immutable provenance chain. Explicit reviewed correction may retain code with a new lot UUID; unrelated duplicate reuse is rejected. HOLD copies to replacement; duplicate reversal and wrong-owner correction are forbidden.

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
- Evidence bytes, committed source receipt/header/lines, original observations, appended corrections/investigations and postings are durable and immutable; evidence is not a mutable URL. Downloads require current owner/facility scope. Operator event details omit evidence/reconciliation secret payloads.

Exact routes/DTO implementations: [Ledger module](../backend/src/GrainLedger.Modules.Ledger/) and [MockCapture module](../backend/src/GrainLedger.Modules.MockCapture/). MockCapture routes/schema are Development/Testing only.

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

Required suite coverage: concurrency/overspend and atomic rollback; authorization and facility scope; idempotency/conflict; policy/compatibility fail closed; variance confirmation invalidation; exact lineage rounding; transfer identity; HOLD/correction/receiving races; immutable evidence/history and one-time reversal; unit precision and coherent synthetic MockCapture source; correction lot-code identity chain and copied HOLD; runtime direct-mutation denial; actual byproducts and product identity. Integration tests use PostgreSQL/Testcontainers, not in-memory DB. Test results below include latest MockCapture cutover checks; earlier workflow observations predate it.

### Nine-step smoke/test workflow

The executable `Pdf_nine_step_smoke_proves_real_stock_and_authorization` test covers:

1. Configure facility, locations, commodities, assignments and evidence-backed synthetic policies; register farmer, plot, season and plot-season. Production policy remains unconfigured until confirmed.
2. Commit receipt lines: 50,000, 30,000, 20,000 kg.
3. Dry 100,000 → 88,000 kg with declared loss 12,000 kg and variance 0.
4. Mill into actual head rice 58,000, broken rice 8,000, bran 6,000 and husk 15,700 kg; declared loss 300 kg and variance 0. Byproducts remain stock.
5. Verify every milling output's exact 50/30/20 source contribution and total.
6. Transfer 1,000 kg head rice with unchanged identity; isolate held 8,000 kg broken rice without release; atomically compensate/replace a separate drying operation.
7. Separate receipt: record 20 kg source / 19 kg arrival; verify unchanged stock but unavailable and transfer blocked, correct measurement to 20 kg and preserve original 19.
8. Correct unused receipt 30→29 with manager evidence reconciliation and atomic reversal/replacement; verify subsequent transfer prevents further simple receipt correction.
9. Verify assigned-facility operator reads and foreign-facility, farmer-contact and pending-user denials. Other integration tests cover foreign-company scope, `REAL_DIFFERENCE`, correction identity chains and independent HOLD/restriction retention.

### Final observed verification

Implementation owner reported these completed checks; documentation owner did not rerun them:

- Initial `dotnet build GrainLedger.slnx` succeeded. Test owner fixed warnings; later test builds succeeded.
- Final `dotnet test GrainLedger.slnx --no-build`: **87 passed, 0 failed**.
- `npm test`: **14 passed**.
- Final `npm run build`: **passed**.
- Historical production deployment smoke predated the MockCapture cutover. It does not verify current migration order or MockCapture Development/Testing behavior; do not use it as evidence for this module.

Actual browser workflows exercised a restricted PostgreSQL runtime API with real app cookies and CSRF through a test-only authentication host:

- Purchaser committed source receipt **100 kg**; operator dried to **88 kg**, declared loss **12 kg**, then transferred **88 kg** without changing lot identity. Operator requested correction; manager uploaded file evidence and reconciled current physical positions against the reviewed replacement version; operator atomically committed corrected **88 kg**.
- Separate receipt **20 kg**, arrival observed **19 kg**: available stock became **0**. Evidence-backed manager measurement correction to **20 kg** retained original **19 kg** observation and restored available **20 kg**.
- Unused receipt **30→29 kg** correction preserved human lot code with a distinct replacement UUID and provenance chain; original remained visible at **0**, superseded.
- Customer HOLD on corrected **88 kg**: manager isolated **88 kg**, uploaded evidence and responded; notification read retained HOLD and available **0**.
- Foreign customer: empty lists and lot-detail **403**. Operator unassigned facility: list `[]`; farmer contacts **403**. Pending account: **403**, no Ledger navigation. Anonymous: **401**.
- Latest duplicate-code selector disambiguation was visually verified.

Latest MockCapture cutover verification reported by implementation owner: **113 backend tests and 23 frontend tests passed**; frontend build passed. Browser workflow registered a synthetic farmer and committed a **10 kg** receipt that appeared in Ledger stock; old `/api/capture/farmers` returned **404**. Fresh Production migrations created no `capture_mock` schema and Production `/api/capture-mock/status` returned **404**. A forward upgrade was verified against an isolated initial Ledger database. Incompatible teammate farmers schema/row and Production teammate fixture remained unchanged; `.env` and Supabase were unchanged.

Policies and evidence were explicitly synthetic. Test/browser authentication harness is not a production login bypass. Live Google consent, factory approval and real-stock pilot approval are not established by these checks.

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
| Receiving shortage/gain | Preserve both evidence-backed measurements; block affected stock; no balance adjustment. | Approved evidence, authority and accounting treatment. |
| Byproduct dispatch/disposal | Outputs remain stock, no fabricated outbound posting or hiding in loss. | Approved dispatch/disposal workflow. |
| HOLD release | No release action; response/isolation/read/correction do not clear it. | Explicit authority/evidence/audit/multi-HOLD rule. |
| Receipt correction after movement | Reject simple correction; no cascade/rewrite/automatic transfer reversal. | Approved downstream reconciliation. |
| Wrong owner | Reject source/owner change in this workflow. | Explicit controlled provenance workflow. |
| Reprocessing | Undefined repeated stages/actions rejected. | Explicit operation/stage/product/mass-balance rules. |

A blocked lot/action does not block unrelated eligible work. No live-stock pilot may rely on omitting unsupported physical movement from ledger.
