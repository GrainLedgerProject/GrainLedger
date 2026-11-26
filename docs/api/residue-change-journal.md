# Residue change journal

Implemented locally on 2026-11-23. Shared-database deployment is not confirmed.
This document covers publication hooks and the opt-in F12 propagation worker.
The read-only change/risk APIs are described in [the read contract](residue-risk-reads.md).
Commercial market reassessment remains a separate stage.

## Transaction boundary

Migration `20261123000000_ResidueChangeFoundation` supplies private change, target
and risk-event tables. Migration `20261123010000_ResidueChangePublication` installs
journal hooks at the database persistence boundary. They borrow the existing
publisher transaction and do not commit independently.

An evidence publication cannot commit without its journal rows. A failure in a
journal hook rolls back the evidence, confirmation or physical stock transaction.
Existing authorization, review, immutable version and replay checks still run.
A replay that returns an existing publication does not enqueue another cause.

The hooks run as protected trigger functions with a fixed search path. PUBLIC and
the runtime role have no EXECUTE permission on these functions, and runtime has
no direct write permission on the journal tables. Actor and company come from
persisted publications/events, not an extra enqueue request supplied by a client.
The existing Ledger advisory lock `714928352` is retained.

## Causes and scheduling

| Persisted fact | Cause | Scope and roots |
| --- | --- | --- |
| Report selection, including automatic confirmation and manual report review | `REPORT_SELECTION` | Owning company, selected source's reserved Ledger lot ID, substance |
| Reviewed source group version | `SOURCE_GROUP` | Owning company, all explicit member sources' lot IDs, substance |
| Schema 2 FLOW profile version | `PROFILE_PUBLISHED` | Profile's company or internal global scope; code/substance retained for discovery |
| Profile retirement | `PROFILE_RETIRED` | Retired profile's company or global scope; series code retained |
| Company policy setting or batch item | `POLICY_ADOPTION` | Owning company, profile code and candidate substances retained for discovery |
| Committed drying, milling, merge, blending or bagging event | `PROCESSING_COMMITTED` | Event owner scope, original and resolved physical inputs and outputs |
| Receipt, pool ingress/withdrawal or processing compensation | `MATERIAL_CHANGED` | Event owner scope, affected physical input/output lots |

Publication hooks create one `PUBLICATION` cause and separate `EFFECTIVE_FROM` or
`EFFECTIVE_TO` wakeups when those timestamps lie in the future. Cause identity is
unique by kind, subject ID/version, company and phase. Known time and due time
are PostgreSQL timestamps. Retirement creates an immediate cause.

These wakeups do not redefine scientific applicability. The future worker must
still resolve each source/process at its material-time window using the existing
F12 resolver. A window ending today does not automatically invalidate a source
whose receipt lies within that historical window.

Profile/adoption roots are intentionally deferred to worker discovery. Empty
root arrays on those causes do not mean there are no affected lots. Global
profile causes remain internal until scoped discovery. Evidence can precede
physical arrival; the later receipt also journals material creation.

## Physical topology

A deferred constraint trigger on committed material events executes at the end of
the transaction. It can therefore see lots and custody edges inserted after the
event, including pool generations, aliquots and remainders. It records physical
inputs even when a source accounting share rounds to zero. It does not filter
by remaining inventory, use an interactive trace page limit or precompute a
blend allocation. Draft creation, review previews and ordinary reads do not
create causes. HOLD and loading decisions retain their existing contracts.

## Verification

`backend/tests/GrainLedger.Api.IntegrationTests/ResidueChangePublicationTests.cs`
contains nine focused integration tests. They call the existing HTTP workflows
through the test host and inspect PostgreSQL journal rows. Failure injection at
the second sample journal and deferred processing journal checks real rollback,
including unchanged stock. Further cases check manual/automatic replay, explicit
supersession, company scope, future phases, adoption batch rollback, physical
pool/aliquot/remainder coverage and restricted runtime permissions.

The publication, foundation, report-review, company-policy, batch, evaluation,
group-boundary and baseline suites passed **107/107** tests in isolated PostgreSQL
Testcontainers. No shared migration, seed, backfill or worker activation was run.
CI's existing backend `dotnet test GrainLedger.slnx` discovers these tests; a remote
CI run was not started as part of this change.

## Propagation worker (step 3)

Migration `20261123020000_ResiduePropagation` adds the private discovery frontier
and current lot/panel projection. It reuses the existing residue graph query and
calculator. Manual graph reads retain their original actor authorization gate.
The worker reaches the private graph through a claimed-job capability, not an
HTTP context, fake account or original publisher's current session.

A tick claims one due job in a short transaction and releases the connection.
Discovery pages roots by UUID and expands physical children with a persistent
frontier/child cursor. Both seed and child pages are bounded by batch size.
Converging paths share a frontier entry. Current inventory is not a traversal
filter, and no interactive trace page cap is used. Group supersession includes
historical membership so removed members are still recalculated. Profile scope
uses adoption history as a conservative candidate set; the F12 resolver decides
actual scientific applicability. Unadopted companies are skipped.

For material changes without an explicit substance panel, discovery derives the
company's known substances from adopted profiles and evidence. Panels are sorted
and partitioned into at most 200 distinct substances per target context. No
substance is silently truncated. A receipt before any residue policy/evidence
can finish without evaluations; later evidence/adoption creates new work.

Each target is materialized in a read-only REPEATABLE READ transaction. Its
revision includes the actual graph, committed company/global journal vector and
time-dependent visibility boundaries. Calculation happens after releasing that
transaction. Under READ COMMITTED, APPEND takes the Ledger lock before the job
row, rechecks lease token/generation/expiry and the current revision, then appends
an evaluation/risk event and updates target progress/current projection atomically.
A new basis returns serialization conflict for retry. An expired/replaced lease
cannot append, including after waiting on the global lock. Successful discovery
or append renews the 60-second lease. Retry keeps a durable generic error marker
and a five-second backoff; large jobs yield between bounded batches.

System evaluations have nullable actor ID plus typed target/revision provenance;
manual evaluations keep their actor and replay contract. System snapshots expose
`createdByKind: SYSTEM`, `changeId`, `targetEntryId` and `dependencyRevision`.
Unique target/revision persistence makes replay after a crash idempotent. Current
projection updates cannot move to an older snapshot time. Unknown scientific
inputs still produce `UNRESOLVED`; completing a job does not imply market PASS.
This stage only discovers/evaluates LOT targets. Export context mapping, F14 risk
assessment and plan/container freshness are not implemented by this worker stage.

A confirmed lab report also creates a deferred `REPORT_CONFIRMED` cause: a retest
can invalidate a bound even if no replacement selection is published. Delayed
confirmation timestamps use database due time. Cutover seeds publication causes,
future phases and per-company material work from existing persisted facts without
rewriting original reports, evaluations or decisions. Existing journal keys are
preserved on conflict. Runtime cannot write frontier/current/history directly or
invoke private graph/basis helpers; only the fenced worker RPC is granted.

### Activation

The hosted service reads final configuration at startup and returns before opening
any connection when disabled. Default is disabled. Only enable after reviewing the
local database destination and explicitly applying the migration and restricted
runtime grants. Normal API startup does not migrate, seed or repair permissions.

```json
{
  "ResiduePropagation": {
    "Enabled": true,
    "BatchSize": 25,
    "PollSeconds": 5
  }
}
```

`BatchSize` is constrained to 1..100; idle polling to 1..60 seconds. Do not put
this opt-in in shared configuration without deployment authorization. No shared
DB configuration was changed or worker activated during implementation.

### Worker verification

`ResiduePropagationTests.cs` covers late evidence on exhausted descendants,
removed group members, more than 200 substances, zero-share dependencies,
idempotent append/crash recovery, lease replacement before and during a lock
wait, basis changes/temporal boundaries, private scope, old-schema cutover,
transient write failure, concurrent ticks, future due work, global profile scope
and the real hosted-service loop. The hosted test confirms evidence via HTTP and
waits for the background service; it does not call the runner manually.

Further work: Export context assessment and freshness,
full acceptance/performance validation and deployment review. Historic approval
and closed dossier data remain outside this stage.


The final step-3 regression run passed **120/120** tests (13 worker cases plus
107 publication/foundation/F12 regressions), with no skipped tests. The test host
uses isolated PostgreSQL Testcontainers. A dedicated host configuration opts the
worker in for one end-to-end background test; all other fixtures keep it disabled.
No remote CI or shared-database deployment was performed. Large production-load
stress/performance testing remains part of final acceptance.
