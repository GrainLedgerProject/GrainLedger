# Container S2 — focused web verification

Use an isolated PostgreSQL/browser host with synthetic reviewed data and matching API/web build. Do not reset old HOLD/stale/CLOSED fixtures or run shared migrations/data writes implicitly. Follow the [S2 API](../api/container-workflow-api.md) and [core scope](../plans/container-core-completion-plan-2026-11-17.md). This guide is a specification; execution results are recorded separately.

## Re-review and plan versions

1. Export loads 4/10 kg from a qualified DIRECT_STOCK fixture. CM confirms a new in-limit quality observation. Refresh checks: authority becomes stale, unspent reservation releases, actual 4 kg remains unchanged.
2. CM opens **Container reviews and shipment records**, selects the original proposal, **Existing outputs only**, records reason and **Approve and activate scope**. Continue the remaining six kg only after current PASS.
3. Use a separate fixture for P1→P2: after four kg, prepare a current draft for only six unproduced/unloaded kg. Propose; CM selects P2 and approves ALL_REMAINING. Loading retains separate proposal IDs for P1/P2.
4. Before CLOSE, refresh. If an old loaded scope needs current evidence review, review that original plan OUTPUT_ONLY. This does not relabel old output as P2 or reserve obsolete unused quantities. CLOSE evaluates all committed loads; dossier retains old load authority and new closing basis.

## Partial approved production

1. On an approved 20 kg two-source blend, Export requests production. Assigned Operator opens **Ledger → Approved production**, saves an actual 10 kg batch with the original input recipe and commits.
2. Confirm actual output quality. CM selects **Remaining inputs and existing outputs**, reviews PASS and records a new scope review. Load `4.000001` kg from the actual first output.
3. Operator chooses **Prepare remaining approved production**, saves/commits the second 10 kg batch using only remaining inputs. First operation/output and load remain history. Confirm second output quality; CM reviews existing outputs.
4. Load `5.999999` from the first output and `10` from the second; CLOSE at 20. Verify each original input consumed once in total, exact source shares, two batches and no CLOSE stock debit.

## Pool, transfer and bagging

1. Approved current-pool stock initially explains the handoff requirement. Operator worklist shows the container; open its stock handoff panel.
2. Select actual output, TRANSFER, current revision, ordinary destination and measured quantity/time; save. CM supplies reason and approves the saved handoff. Operator commits with confirmation.
3. The actual aliquot/output and exact remainder are retained. Confirm quality of the new actual identity where required, then CM reviews actual positions. Load part.
4. Use an independent fixture or remaining eligible quota for BAGGING. Save actual equal input/output and zero-loss handoff, review then commit, confirm actual bagged quality and review it before loading. Stop on unsupported measured quantity exceptions; never adjust measurements to fit.
5. Wrong role/facility, stale revision, other-plan pool reservations, unsupported successor generation and external source-complement changes must reject without stock/reservation/claim drift. No automatic broad rebinding.

## HOLD, documents and evidence

1. CM places HOLD. Relevant owner CM/Export and assigned MM see retained **Container HOLD notifications**. Mark read; restriction remains. MM records acknowledgement/response with scoped evidence; lot isolation uses the existing lot HOLD workflow. No release/resume/unload control appears.
2. On a CLOSED fixture, Export uploads a provided synthetic shipment document with type/reason. Upload another version of the same type; both immutable files remain downloadable within scope.
3. Upload company/facility-scoped evidence and append a supplement with reason. Re-read CM/Export dossier: closing snapshot and its redaction unchanged, CLOSED remains. Later HOLD does not rewrite it.

## Finish the earlier PARTIAL cases

- CT-16: choose reserved/exhausted source through the actual Ledger HOLD picker; check continuation blocks and historical actual kg remains.
- CT-18: use separately scheduled MRL and assessment-policy expiry fixtures, retaining pre-expiry PASS and post-expiry rejection times. Final-write expiry injection belongs only in isolated regression, not shared triggers.
- CT-25: use a newly seeded isolated retained-legacy history fixture with no Capture mapping; trace keeps actual kg and reports SOURCE_PROVENANCE_UNAVAILABLE/incomplete. Do not delete or corrupt valid provenance to manufacture the case.
- CT-26: log in with independently verified tenant-B Export and CM accounts; paste A's container/workflow/document/trace URLs and verify denial/redaction.
- CT-27: extend the old blocker-only case with actual approved production/review/loading/CLOSE.
- CT-28: separately record the supported reviewed extraction path and expected rejection of unsupported or stale handoff variants.

Run the focused container backend tests and directly affected stock/plan boundaries once implementation is assembled, then focused frontend checks/build and the actual isolated browser path. The season benchmark uses 50 official sources, 101 physical conversions and 1,000 actual container load histories; document bulk fixture setup separately from functional API/UI proof. Measure warm handler/JSON timings independently of network/page load, and validate the complete target set and exact source-mass oracle. No whole-backend test run is required by this walkthrough.
