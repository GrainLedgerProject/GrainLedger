# Receiving loss threshold — implementation plan

Status: implemented and verified on isolated PostgreSQL; no shared database changes.

## Goal

Allow an assigned MillOperator to confirm a complete paddy arrival without manager review when every delivery line is within a configurable transport-loss threshold. System-wide default: **2% per line**, with an optional override for the company that owns the procurement.

The threshold controls escalation only. Stock must always use measured arrival mass, never expected source mass. A tolerated discrepancy does not establish its cause or settle customer payments.

## Current behavior

Exact complete matches post measured stock immediately. A complete positive set with at least one shortage and every line within the effective per-line threshold posts measured stock without manager review. Any excessive shortage or surplus sends the complete delivery to manager review with no stock. Managers can approve eligible shortages with scoped evidence and a reason, request remeasurement, or keep discrepancies blocked. Surplus acceptance is unsupported.

Relevant implementation:

- [Receiving commands, projections and package invariants](../backend/src/GrainLedger.Modules.Ledger/Sql/ReceivingLossThreshold.sql): threshold configuration/history, measurement snapshots, `ledger.arrival_command`, receiving detail and accepted-stock validation.

- Compatibility: the threshold migration replaces package validation and receiving detail after the unified-lot-code migration. Those overrides must use `ledger.source_lot_code` so procurement, receiving, and accepted stock retain same persisted source-lot code.
- [Measured-stock posting](../backend/src/GrainLedger.Modules.Ledger/Sql/ManageActiveResources.sql): `ledger.accept_arrival`; [original receiving schema](../backend/src/GrainLedger.Modules.Ledger/Sql/PhysicalReceiving.sql): immutable observations/reviews.
- [Setup management routes](../backend/src/GrainLedger.Modules.Ledger/LedgerManagementEndpoints.cs): existing `platform.configure` authorization and version checks.
- [Receiving UI](../apps/web/src/LedgerReceiving.tsx) and [Setup UI](../apps/web/src/LedgerSetup.tsx).
- Forward configuration, history and snapshot migration: `20261019000000_ReceivingLossThreshold` (`ReceivingLossThreshold.sql`).

## 1. Per-line receiving rule

For expected mass `E`, actual mass `A`, and configured threshold percentage `T`:

```text
lossKg = E - A
withinThreshold = A <= E AND (E - A) * 100 <= E * T
```

Use exact normalized kg and decimal arithmetic. Do not round quantities or percentages before the decision. Exactly 2% loss qualifies at the default threshold.

| Expected | Actual arrival | Loss | Result at 2% |
| --- | --- | --- | --- |
| 100 kg | 100 kg | 0% | Accept exact match |
| 100 kg | 99 kg | 1% | Accept within threshold |
| 100 kg | 98 kg | 2% | Accept within threshold |
| 100 kg | 97.999999 kg | 2.000001% | Manager review |
| 100 kg | 101 kg | Surplus | Manager review; existing surplus block remains |

- Evaluate every line independently. A receipt-wide average must not hide excessive loss on one line; surplus must not offset another line's shortage.
- If any line exceeds its loss threshold or has a surplus, send the whole delivery to manager review. Post no stock until resolved. Retain the existing complete-arrival contract; do not introduce partial acceptance.
- Zero/full-loss actual mass remains unsupported.
- Record the discrepancy without assuming moisture loss or another explanation.

## 2. Configuration

Use a system-wide default with an optional company-specific override. This replaces the earlier facility-scoped configuration proposal.

```text
effectiveThreshold = companyOverride if configured, otherwise systemDefault
initialSystemDefault = 2%
```

- Store receiving threshold configuration in Ledger: a system default and optional overrides keyed by the procurement owner's company organization ID. Do not change Identity organization ownership or pass entities across modules.
- Resolve the company from persisted procurement ownership, never from a request-supplied company ID.
- Expose the default, company overrides, and configuration history under **Setup → Advanced setup → Receiving loss thresholds**. Use existing Administrator `platform.configure` permission and UUID-string optimistic version checks. Any active authorized Administrator can maintain these shared settings; do not inherit creator-only editing from basic facility resources or make migration-created settings read-only.
- Administrator records values according to approved policy or agreements; configuration authority does not grant business approval authority. Require a reason for configuration changes, but do not reuse the processing-policy evidence form or its facility/owner requirements.
- Company users may view their own effective threshold and whether it comes from the default or an override. They cannot edit thresholds or view another company's override. Operators and managers see the effective threshold only for receiving tasks within their assigned facility scope.
- Add a narrowly scoped own-company read path using persisted active-role and organization checks. Existing Ledger setup reads exclude company roles; do not grant them broad Setup access or expose all company overrides through shared lookups.
- Validate override targets against persisted customer organizations, including companies without stock or prior receiving tasks. Use an authorized organization lookup rather than Ledger's stock-dependent owner list. New companies without an override automatically use the default.
- Operators and purchasers cannot set a special percentage per receipt or delivery line.
- Percentage contract for this feature: finite base-10 decimal strings with at most six fractional digits, stored and snapshotted exactly as `numeric(8,6)`, with `0 <= T < 100`. Reject JSON numbers, NULL, malformed/nonfinite values, exponent notation, and excess precision before a database cast could round them. Use exact arithmetic, not JavaScript floating point. A configured override of `0` is valid and restores exact-match-only operator acceptance; it must not be mistaken for an absent override.
- Allow removal of a company override with a version check; future submissions then use the current system default. Changing the default affects only companies without an override.
- Give every committed change a fresh, non-reused UUID revision token, following existing setup version conventions. Removing and later recreating an override must not make an old edit token valid again; concurrent creates for the same company must not overwrite one another.
- An absent company override intentionally selects the default. Missing/invalid required default configuration or a configuration-read failure must fail closed, not silently substitute a percentage.
- Keep this setting separate from evidence-backed processing tolerance policies. Transport loss and drying/milling variance are different business rules.
- Do not add facility overrides, company-by-facility combinations, commodity rules, effective-date scheduling, or a generic policy engine.

Example: company A has no override and uses the 2% default; company B has an approved 3% override. A line losing 2.5% requires manager review for A but qualifies for operator acceptance for B. The same per-line rule applies at every receiving facility.

### Configuration history

- Keep a Ledger-owned append-only history of initial configuration, default changes, and company override creation, update, and removal. This is separate from immutable per-measurement snapshots and from Identity account/security audit records.
- Each committed change records its revision, scope (system default or customer company ID), action, old/new percentage or absent-override state, authenticated actor, server-recorded timestamp, and nonempty reason. Actor and timestamp must not be supplied by the browser.
- Apply the configuration change and append its history in the same protected database transaction; either both commit or neither does. Reject unauthorized/stale/invalid changes without creating a successful-change history entry.
- Removing an override records removal, not history deletion. Keep prior revisions addressable by measurement snapshots. The system default cannot be removed.
- Expose bounded, server-paged history to authorized Administrators, with scope, actor, time, reason, and before/after values. Company/operational threshold reads must not leak other companies' settings or configuration-history notes.
- Record the initial 2% default as migration-origin initialization with its migration identifier and server timestamp, not a fabricated human actor. Do not invent audit events for changes that predate this feature.
- Limit this history to receiving threshold configuration; do not introduce a generic audit platform or broaden this task to unrelated factory/system settings.

## 3. Operator and manager workflow

### Operator

1. Enter actual mass for every delivery line.
2. Show expected kg, actual kg, loss kg/percentage, the owning company's effective threshold and its source (system default or company override), and line outcome. Use exact arithmetic or server-provided calculations; never JavaScript floating-point mass accounting.
3. Submit a complete measurement set:
   - Every line exact: retain `RECEIVED_MATCHED`.
   - Every line within threshold, with at least one shortage: use new accepted status `RECEIVED_WITHIN_TOLERANCE`.
   - Any excessive shortage or surplus: retain `AWAITING_MANAGER_REVIEW`.
4. For accepted arrivals, post measured actual mass atomically with the receiving transition.

Any displayed preview is advisory; the server resolves the owning company's current effective threshold when the measurement command acquires the existing write lock and applies the submission. Procurement creation time and client-supplied `capturedAt` do not select a historical policy. The response returns the authoritative applied snapshot and result. History screens show that stored snapshot, never a recomputed threshold from today's settings. Clearly label rounded percentage displays as such; an exact over-threshold result must remain visible even when its displayed percentage rounds to the threshold.

### Manager

- Highlight offending lines and the threshold applied to the submitted measurement set.
- Preserve existing actions: `APPROVE_SHORTAGE`, `REQUEST_REMEASUREMENT`, and `KEEP_BLOCKED`.
- Above-threshold shortage requires review, not automatic rejection. Approval still requires a reason and evidence scoped to the procurement owner and assigned facility.
- Manager approval is an exception for that delivery only. It does not create a receipt-specific threshold or change the company override/system default. Purchasers may provide source observations and evidence for the review, not raise the threshold to bypass it.
- Surplus acceptance remains unsupported. Existing approval must continue to reject any measurement set containing a surplus.

## 4. Backend, history, and migration

- Enforce the per-line rule in the protected Ledger receiving command and accepted-package invariants, not only in the browser.
- Preserve the distinct new-status invariant: `RECEIVED_WITHIN_TOLERANCE` requires a complete accepted set with every actual mass positive and at or below expected, every line within the snapshotted threshold, at least one strict shortage, and no manager-approval record. Exact equality remains `RECEIVED_MATCHED`; manager-approved shortages retain their evidence-backed status. Changing current settings must never invalidate an accepted historical package.
- Snapshot the applied percentage, owning company ID, configuration source (system default or company override), and selected immutable configuration revision on each new immutable measurement set. Resolve these server-side and retain the revision reference; the stored percentage/scope must match that revision. Read and snapshot configuration consistently with the receiving decision so concurrent configuration changes cannot produce a mixed result.
- Serialize configuration mutations against measurements with existing `ledger.lock_writes()` at `READ COMMITTED`, taking the lock before configuration/source/stock reads. Use protected routines for configuration/history writes with persisted Administrator checks; HTTP authorization alone is insufficient.
- Configuration changes affect new measurement submissions only. They must not silently accept pending reviews or reinterpret historical measurements, decisions, or accepted arrivals.
- Pending unmeasured arrivals use the configured threshold when their complete measurement set is submitted. A requested remeasurement uses the configuration current at its new submission and retains all prior sets.
- Preserve original source expectations, actual observations, exact mass strings/units, review history, facility/owner scope, advisory locking, caller-owned transaction, and `READ COMMITTED` semantics.
- Preserve idempotency: replaying a successful operation returns its saved result even if configuration has since changed; changed payload with the same operation ID still conflicts.
- Add a forward Ledger migration. Initialize the system default to 2% and leave company overrides absent; do not create stock or change receiving states. Preserve historical rows without inventing previously applied thresholds.
- Existing measurement sets have no newly applied policy: leave their added snapshot fields absent/NULL and label them as predating this feature rather than backfilling 2%. Require a valid applied snapshot for new measurement sets and the new accepted status; retain original validation for pre-feature records and manager reviews.
- Preserve the restricted runtime boundary: grant only required scoped reads and protected routine execution; reject direct configuration/history mutation and history update/delete. Update explicit routine grants and runtime-role validation as needed. Use a new forward migration/resource, never rewrite generated migration history.
- Add `RECEIVED_WITHIN_TOLERANCE` throughout accepted-state constraints, package validation, stock/procurement projections, task queries, filters, labels, and contracts. Do not reuse `RECEIVED_MATCHED`: its current invariant requires exact equality.
- Keep Capture source status immutable `COMMITTED`; the Ledger receiving projection continues to supply outward workflow status.

Affected consumers include:

- [Capture procurement endpoints](../backend/src/GrainLedger.Modules.Capture/ProcurementEndpoints.cs).
- [Paged receiving queries](../backend/src/GrainLedger.Modules.Ledger/Sql/PagedReceiving.sql).
- [Purchasing UI](../apps/web/src/Procurement.tsx) and receiving/setup UI.

## 5. Verification

Extend existing focused regression coverage rather than introducing a separate test framework:

- Exact match, below threshold, exactly 2%, and one micro-kg beyond the boundary.
- System-default fallback, company override precedence, an explicit 0% override, override removal, and default changes leaving overridden companies unaffected.
- Percentage input boundaries: valid 0% and six-place decimals; reject negative or 100%+ values, JSON numbers, NULL, malformed/nonfinite values, exponent notation, and excess precision without rounding or changing configuration/history.
- Company A at 2% versus company B at 3% for identical 2.5% line loss, with no cross-company leakage and company ownership resolved from persisted procurement.
- Multiple lines where the delivery-wide loss percentage qualifies but one line exceeds its own threshold.
- Surplus and mixed shortage/surplus sets; neither can bypass manager review or existing approval restrictions.
- Exact unit conversion and accepted stock equal to actual mass, not source expectation.
- Above-threshold manager approval, remeasurement, and blocked outcomes.
- Unauthorized default/override edits, own-company read scope, assigned-facility receiving scope, stale configuration versions, idempotent retries after configuration changes, and measurement history retaining the applied threshold/source/version.
- Configuration-history creation, update, removal, and recreation, with correct old/new values, server actor/time, reason, and non-reused revision tokens. Failed/stale/unauthorized changes must leave both current configuration and successful-change history unchanged.
- Concurrent configuration creates/edits and removal/recreation must reject stale writes. Direct runtime configuration writes and history update/delete must be denied.
- Configuration changes racing with measurement submission must leave a consistent applied snapshot and decision; missing required configuration or failed reads must not bypass review.
- Migration preserving existing accepted stock/history and leaving pending reviews unresolved; pre-feature measurement sets retain absent snapshots. Test old unmeasured procurements and new remeasurements using the new policy while old observations retain their original interpretation.

Existing test homes:

- [Receiving integration tests](../backend/tests/GrainLedger.Api.IntegrationTests/ReceivingTests.cs).
- [Receiving migration tests](../backend/tests/GrainLedger.Api.IntegrationTests/ReceivingMigrationTests.cs).
- [Ledger workspace tests](../apps/web/src/LedgerWorkspace.test.tsx).
- [Ledger setup tests](../apps/web/src/LedgerSetup.test.tsx).

After implementation, run relevant backend/web checks and exercise the real advanced Setup/history, company-scoped threshold display, operator, and manager browser paths against isolated PostgreSQL. Prove a 2% → 3% change is recorded with actor/time/reason, earlier snapshots and pending reviews remain unchanged, default versus company-override routing for identical line losses, within-threshold measured-stock posting, above-threshold no-stock escalation, and evidence-backed manager resolution without changing company configuration. Do not run write-based verification against shared Supabase.

## 6. Documentation and deployment

After verification, update affected contracts together:

- [Procurement API](procurement-api.md).
- [Architecture](architecture.md).
- [Ledger implementation and verification](ledger-implementation-plan.md).
- [README](../README.md).

Replace affected no-receiving-tolerance statements while preserving separate processing-policy requirements and unsupported settlement/surplus boundaries. Report only verification actually performed.

Shared Supabase migration, schema/role/grant changes, seeds, or data-changing smoke checks require explicit database-owner approval. Normal application startup must not migrate or seed. This plan authorizes no shared database changes.

## Implementation and exercised verification

Implemented in Ledger migration `20261019000000_ReceivingLossThreshold` (`ReceivingLossThreshold.sql`). Migration initializes the system default to 2% with migration-origin history, leaves company overrides absent, and does not backfill thresholds onto historical measurement sets or alter their status/stock. Shared database migration, schema/role/grant changes, seeds and data-changing checks were not performed; shared deployment still requires explicit database-owner approval.

Exercised on isolated PostgreSQL with real HTTP/cookie sessions:

- Company A used default 2%; 100 kg expected / 98 kg actual posted 98 kg as `RECEIVED_WITHIN_TOLERANCE`. At 97.5 kg, same company remained `AWAITING_MANAGER_REVIEW` with zero stock and stored 2% snapshot.
- Administrator changed default 2% → 3%; append-only history recorded server actor, timestamp and reason. Created company B 3% override; at 97.5 kg actual, B received `RECEIVED_WITHIN_TOLERANCE` and 97.5 kg stock with `COMPANY_OVERRIDE` revision. Company B own-company read exposed effective 3% without broad Setup access.
- Restoring default to 2% left company A pending review and prior accepted snapshots/revisions unchanged. Mill Manager approved A's 97.5 kg shortage with reason and scoped uploaded evidence; actual mass posted, applied 2% snapshot remained, and configuration did not change.
- Setup and manager views were exercised at 375 px; body width remained 375 px and wide tables contained local horizontal overflow. Browser reported no errors.
- Backend build passed with existing NU1902/NU1903 Pkcs 10.0.0 and unread-access CS9113 warnings. Full `dotnet test GrainLedger.slnx --no-build`: 273 passed, 4 failed of 277. All 34 `Receiving*` cases passed, including migration, threshold concurrency/strict inputs, and inherited-column runtime denial. Three unrelated SampleTests fail because SeedSourceAsync commits `payload {}` before procurement and provenance trigger rejects it; Identity reciprocal admin-demotion race returned 401 + 200 where existing test expects 409.
- Web `npm test`: 87 passed across 17 files; `npm run build` passed.
- After merging unified-code changes from `dev`, isolated receiving/code regressions passed **38/38**. Real cookie-session HTTP smoke accepted **12 + 8 kg expected → 11.76 + 7.84 kg actual**, posted **19.600000 kg**, and retained matching unified `RAW-20261019-…` codes across receiving details and accepted lots.

These results are isolated verification only, not a shared database deployment or full-green backend-suite claim.
