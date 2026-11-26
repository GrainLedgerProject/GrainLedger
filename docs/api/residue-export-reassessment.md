# Export reassessment after an F12 change

Migration: `20261123040000_ResidueExportPropagation`. Deploy it with the reviewed runtime grants before enabling `ResiduePropagation:Enabled`. Normal API startup does not migrate, seed, repair grants, or enable the worker.

## Discovery and context

The existing durable F12 job first finishes its physical dependency frontier. Export discovery then uses bounded UUID pages to find proposals whose saved allocation, output claim, or actual load intersects that frontier. It does not use rounded source contributions as the dependency graph.

Each proposal creates a `PLAN` target with `PLANNED_ALLOCATION`. Produced or loaded proposals also create a `CONTAINER` target with `ACTUAL_LOADING`. The context key is the proposal UUID, so a container with several historical proposals retains separate contexts. Discovery checkpoints `exportAfter` and `exportsDone`; FINISH cannot complete a job before Export discovery and every discovered target have completed.

The migration reopens operational metadata for previously completed LOT jobs. It preserves their causes, evaluations, risk events and completed LOT targets; Export contexts are then backfilled through the same worker.

## Assessment

`ExportResiduePropagation` obtains a repeatable-read snapshot through a claimed job capability. It does not construct an HTTP user or impersonate a Customer Manager. Export owns the protected context reader. The worker reuses the F12 evidence resolver/calculator, the applicable MRL and assessment-policy readers, `BlendAssessment.Evaluate`, direct-stock conservative bounds, product mapping, and the input MRL screen that prohibits dilution of a violating input.

Each context uses the order's market and commodity. A physical PADDY-derived finished fraction is matched through the existing Export product mapping; its Ledger commodity is not rewritten. QC uses the same normalization as interactive F14 so equivalent decimal representations do not invalidate the saved dependency hash.

For a proposal, assessment uses its saved allocation without rerunning the optimizer. Its own reservation does not make that allocation appear unavailable. This is a compliance reassessment, not a stock availability check or an execution authorization.

For a container, the existing retained review-context builder supplies remaining inputs and outputs. Remaining production, actual output and actual loading are evaluated separately. Directly packed physical stages are grouped separately and every lot must satisfy quality and residue limits; their concentrations are not averaged to conceal a violation. CLOSED contexts use actual load lines, including consumed lots. Current output requirements can be assessed even when the original unproduced demand is obsolete; obsolete remaining-input demand is incomplete.

The snapshot contains assessment findings, market/commodity, MRL/policy versions, F12 provenance, section scope and references to the historical proposal/decision version and dossier where present. Public risk APIs retain the step-4 nested redaction rules. They expose safe freshness/proposal/decision-version fields, not raw evidence or private job state.

## Persistence and freshness

Writes acquire the Ledger advisory lock, the company Export lock and then the job row. APPEND rechecks lease, generation, expiry and the current dependency basis. A changed basis returns `40001` for durable retry. A stale lease returns `55000`. Repeating an append for the same target/revision returns the existing event.

The worker appends an immutable `ledger_residue_risk_events` row and updates the private `exports_residue_freshness` projection in the same transaction. Snapshot time orders the projection, so an older assessment cannot replace a newer one. Runtime has finite function EXECUTE rights and no direct projection-table access; the context builder and legacy LOT-job core remain private.

Proposal GET includes an optional `residueFreshness` object:

```json
{
  "riskEventId": "<uuid>",
  "assessedAt": "<UTC timestamp>",
  "assessmentStatus": "PASS",
  "freshness": "REVIEW_REQUIRED",
  "baselineUnchanged": false
}
```

- `CURRENT`: the proposal reassessment passes and its evidence dependency hash matches the saved baseline.
- `REVIEW_REQUIRED`: the reassessment passes, but it does not prove that the historical decision reviewed the current basis. This is also the conservative value for actual container contexts, which are not the original planned allocation.
- `STALE`: the reassessment fails or is incomplete.
- `NOT_RECHECKED`: no current projection exists, or the readonly freshness check detects another basis change after the stored assessment. `assessedAt` remains the time of the last assessment, not a claim of live approval.

Pending proposals retain their existing live F14 freshness check. Decided proposals expose the separate background result. `stockAndEvidenceCurrent` remains null for that decided-proposal branch because this worker does not recheck physical availability. These fields cannot grant execution authority.

No worker path changes original proposal snapshots, decision history, approvals, scope reviews, dossiers, HOLDs, stock postings, reservations or reservation events. Production, loading and CLOSE retain their transaction-time checks. The existing container GET invalidation contract is unchanged. A worker PASS never approves or reapproves a shipment.

## Verification

Integration coverage uses real HTTP fixtures and isolated PostgreSQL Testcontainers. Added cases cover pending/approved proposal changes, the same stock passing one synthetic market and failing another, partial loading, CLOSED dossiers, partial production, migration backfill, revision conflicts, expired/reclaimed leases, idempotency and private-helper permissions. Existing F12, proposal, approval, production and container suites provide regression coverage. No shared database migration or data write is part of this verification.

Verification on 2026-11-23: the related integration suite passed 241/241 tests (zero skipped) in isolated PostgreSQL after the user-authorized WSL reset. No CPU-affinity override or HTTP retry was used in that successful run. A prior local clock regression and its diagnostics are retained in the implementation plan. This does not claim remote CI or a full backend acceptance run.

## Mandatory source laboratory evidence for Export

Export requires confirmed, correctly associated laboratory results and an active reviewed report selection for every contributing source and each substance required by the order's assessment policy. A finished lot can inherit these results through reviewed F12 processing steps; a separate finished-lot residue test is not required.

Export passes `requireSourceLab: true` through candidate bounds, hypothetical blends, final preview, proposal recheck, approval/execution assessment and background commercial reassessment. `GROUP_FIRST` and untested-source groups cannot replace direct source laboratory evidence in these calculations. The requirement is server-controlled; preview/proposal requests cannot disable it. General Ledger F12 evaluation retains its reviewed group behavior. Checking that a lab exists while calculating from a group bound is not permitted.

Missing source lab produces unresolved bounds and the `SOURCE_LAB_REQUIRED` finding where applicable. `SOURCE_LAB_SELECTION_MISSING` identifies missing reviewed selections; `SOURCE_LAB_SELECTION_CONFLICT` identifies unreviewed report/selection conflicts. Incomplete panels or unsupported interpretation/processing rules remain incomplete evidence; none can produce a PASS proposal. Lab confirmation confirms evidence, not destination-market compliance. Existing input-lot MRL screening remains in force.

No new public endpoint, table, migration or rewrite of historical snapshots is required. Old previews are rechecked before proposal; existing approvals retain their history and must pass current evidence checks before execution.

Verification on 2026-11-24: the focused source-lab/resolver/API suite plus the existing policy regression passed 34/34 cases. The related backend regression finished 292/293: one existing container fixture failed while publishing a policy setting (HTTP 400); its two cases passed when run separately. This does not establish a fully green broad run or a confirmed root cause for that intermittent setup failure. All new source-lab cases passed in the broad run. The planning FE suite passed 18/18 tests and the production FE build passed. Checks used isolated PostgreSQL Testcontainers and HTTP test hosts, with no shared-database migration, seed or browser deployment.
