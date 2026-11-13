# Container execution verification — 13 November 2026

Branch: `feat/container`, based on merged `bcf2c3e` / dev `57f247d`. See [current plan](../plans/container-execution-plan-2026-11-13.md), [API](../api/container-execution-api.md) and [web test guide](../guides/container-execution-web-test.md). This records actual local checks; it is not a GitHub Actions run, shared deployment or reviewer sign-off.

## Delivered

Planned-container DIRECT_STOCK execution supports partial actual loading, atomic reservation consumption/source-position debit/retained custody, exact source complement, current conservative assessment and reviewed dependency fingerprint, manual container HOLD, strict-target closure/dossier, role redaction and source↔actual-container trace. Legacy preparation remains draft-only. The blend owner's approved-production/output handoff, custody-generation reservation transfer and unsupported lifecycle exceptions remain blocked as described in the plan.

Read local Report5 and BM04 DOCX plus November 9 amendment notes. All 159 pre-existing untracked files remain unchanged and untracked. Their NOT RUN entries and team counts/signatures were not modified. BM04's committed test counts and performance threshold are not treated as execution results.

## Checks run

| Check | Actual result |
| --- | --- |
| `npm ci` with existing lockfile | Pass; no lockfile change. npm reports one existing high dependency advisory; no dependency upgrade or audit fix was performed in this feature. |
| `dotnet restore`; Release build | Pass; existing CS9113 unread-access and xUnit2020 warnings remain. |
| Full backend Release/xUnit/Testcontainers + XPlat coverage, MaxParallelThreads=4 | **573 passed, 0 failed, 0 skipped**, about 3m09s. TRX and Cobertura generated under ignored `artifacts/test-results/backend`. |
| New ContainerExecutionTests | **8/8 passed** in the full suite. Prior focused runs supported implementation, not additional distinct report cases. |
| Production TypeScript/Vite build | Pass; existing >500 kB chunk warning remains. |
| Full web Vitest coverage, maxWorkers=2, testTimeout=10000 | **260/260**, 34 files, pass. Coverage: statements 75.48%, branches 71.78%, functions 66.38%, lines 83.52%; no project coverage threshold is defined. |
| CI routing/failure-gate script tests | Pass. Changed paths select backend, web and Docker jobs. |
| Docker application image build, without push | Pass, local tag `grainledger:container-execution-ci`. |
| Documentation links / actionlint / coverage HTML generation | Pass: links for the full container diff against dev, actionlint v1.7.12 with pinned SHA-256, backend coverage HTML generation and web coverage Summary. |
| Actual HTTP cookie/CSRF smoke on isolated loopback test host | Pass; see final validation below. No shared-data smoke. |

During development, focused tests exposed and corrected SQL parse/schema mistakes and an ambiguous frontend test locator. A substantive late-source-HOLD regression initially accepted the second load because the generic ordinary-lot restriction helper did not cover that source ancestry; the new container-specific positive-source/physical-ancestry restriction check corrected it. No automatic child/container HOLD records or release were added. Malformed/empty successful responses and 408 are retained as uncertain operations and retry the same payload.

## Focused behavior / Report5 mapping

| Evidence in source | Behavior verified | Report5 relationship |
| --- | --- | --- |
| `Direct_approved_stock_loads_partially_closes_once_and_retains_an_immutable_manifest` | 4 + 6 kg loading, stock/reservation reconciliation, replay, closure once, redacted/full dossier, late HOLD preserves original dossier, immutable load rows | Supports ST-16/17, container side of ST-18; not a witnessed browser E2E claim. |
| `Source_and_container_tracing_use_actual_mass_and_deny_foreign_or_export_backward_reads` | No-load incomplete; actual source kg/plot/percent; source forward finds actual target; Export backward/cross-company denied; trace leaves stock unchanged | IT-11/12 support; IT-13/14 automated coverage. |
| `Container_hold_and_current_lot_or_quality_changes_block_without_consuming_stock` | Missing target/seal/changed quality and container HOLD block; reservation not consumed; scoped/paged history | ST-19 and freshness/security support. |
| `Concurrent_loads_and_changed_replay_cannot_overspend_and_failure_after_consume_rolls_back` | Failure injected after consume/before posting rolls all changes back; same-version race has one winner; changed operation payload conflicts | F16 accounting/reliability support. |
| `Permission_revoked_while_waiting_for_loading_lock_is_rechecked_before_write` | Persisted role revocation while waiting returns 403, stock unchanged | IT-13 / NFR-SEC-01 support. |
| `UT14_exact_source_allocation_conserves_25000kg_and_private_helper_has_no_runtime_grant` | The production SQL allocator returns 4,142.857143 + 20,857.142857 = 25,000 kg for the declared oracle; helper private to runtime | UT-14 automated oracle. |
| `Preproduced_multisource_direct_stock_keeps_exact_complement_across_partial_loads` | Real two-source pre-produced blend; partial micro-kg loads sum exactly, remaining complement equals stock; creation contributions unchanged | UT-14/IT-12 conservation support; not approved blend execution. |
| `Late_source_HOLD_blocks_continuation_without_automatic_container_HOLD_or_stock_change` | Late source HOLD prevents next load; no auto container HOLD or stock change | F18/F19 separation and current restriction support. |

The source methods are in [ContainerExecutionTests](../../backend/tests/GrainLedger.Api.IntegrationTests/ContainerExecutionTests.cs); [fixture](../../backend/tests/GrainLedger.Api.IntegrationTests/ContainerExecutionScenario.cs) creates actual receiving/processing/lab/policy/order/approve records through the real API in disposable PostgreSQL. Synthetic MRL/catalog setup is explicitly test-only. Full suite also includes existing trace, lab, export, runtime security and ledger regressions. These methods do not replace the six functional case IDs or inflate BM04 totals.

## Limits

- During the implementation/test run above, no shared Supabase migration/seed/grant/reset or business write, `.env` edit, commit or push. The separately approved deployment below occurred afterward.
- No remote CI status claimed; local workflow-equivalent checks must still run on the eventual PR.
- No browser/manual ST-18, mobile rendering, staging/witnessed acceptance, full-season dataset or ST-21 ≤30s benchmark claimed. Actual HTTP smoke is recorded separately from browser testing.
- JSON dossier is a scoped technical manifest/decision snapshot, not an approved legal certificate/PDF. Auto release, notification inbox/acknowledgment, unload/reopen/reseal and late-supplement workflows are not provided.
- After partial execution, changed scientific/legal dependencies block continuation/close until an authorized revalidation path is supplied; a new PASS is not silently substituted for the reviewed approval.

## Final validation

Actual HTTP smoke used the opt-in existing BrowserWorkflowHost in Testing on loopback port 5095, with synthetic cookie sessions and disposable PostgreSQL. It was exercised through HTTP requests; no preview/browser automation or shared runtime database was used.

- Started from a real approved DIRECT_STOCK proposal over a pre-produced two-source 20 kg blended lot. Metadata save remained on the planned container ID.
- Loaded **4.000001 + 5.999999 = 10.000000 kg**, physical stock **20 → 10 kg**. Retry preserved the original receipt and did not append another load.
- Container backward trace returned two sources at **5.000000 kg / 50%** each with original plot provenance. Source forward returned one actual container target; no stock mutation by tracing.
- Closed once, rejected further loading, retained the same original dossier after a later manual container HOLD and close retry. History contained five mutation events and pageSize=2 returned a bounded page.
- Foreign company read and ExportStaff backward source trace returned **403**; ExportStaff dossier omitted source/evidence detail, while CM could read the full immutable snapshot.
- The final frontend run after navigation/permission-scope refinements passed **260/260 with coverage**; production build and Docker rebuild passed. Current role/company/permission changes remount the Export workspace; legacy preparation points users to the planned Export container.
- CI routing tests **7/7**, actionlint, full changed-doc link validation and coverage HTML/Summary generation passed. CI gate was checked with the local successful job outcomes; no remote run is asserted.

The isolated HTTP host and temporary synthetic session file are removed before handover. No user-owned backend/frontend process is stopped.


## Follow-up: approved configured-database deployment

User explicitly requested applying migrations and configuring EXECUTE after the implementation/test run above. Recorded at 2026-11-13T15:25:29.237401+00:00. This confirmation applies only to the configured database; it does not assert deployment in other environments.

Read-only preflight confirmed that owner/runtime connections identify the same database, the monolith schema and all six prior AppDbContext migrations (through `20261112000100_BlendApprovalRevocation`) were already installed, and only the two container migrations were pending. No reset, baseline reapplication or old-schema cutover was needed.

Ran the approved application owner workflow `dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --migrate` in Development. It applied **20261113000000_ContainerPreparation** and **20261113000100_ContainerExecution**, then ran the existing finite runtime-role configuration in the same command path.

Postflight:

- No pending AppDbContext migrations.
- Runtime EXECUTE is true for `ledger_container_action`, `ledger_export_container_command`, `ledger_export_container_read`, `ledger_container_targets`, `ledger_trace_lot` and `ledger_trace_starts` at their current signatures.
- Runtime EXECUTE remains false for private `ledger_container_allocate_sources` and `ledger_container_lot_blocked`; full restricted-runtime validation passes.
- Normal backend startup using the configured restricted runtime connection passed; loopback `/health` returned **200**. The temporary port-5096 verification instance was stopped; no user-owned process was stopped.
- The aggregate fingerprint of stock mass and lot/posting/HOLD/order counts stayed unchanged. No seed, business write smoke, evidence upload, connection-setting edit, commit or push was performed.

The feature is now installed in this database. Start/restart the regular backend/web and use Export orders → approved DIRECT_STOCK planned container → Loading and closure. BLEND_INPUT_STOCK/output and custody-reservation handoff remain blocked as documented; deployment does not create approval/policy/evidence or test business data.
