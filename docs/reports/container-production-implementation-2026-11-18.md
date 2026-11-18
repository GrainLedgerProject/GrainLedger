# Container S1 — implementation and verification, 18/11/2026

## Outcome

Implemented the S1 slice from the [core completion plan](../plans/container-core-completion-plan-2026-11-17.md): an approved BLEND_INPUT_STOCK request is executed as one real Ledger BLENDING operation, its ordinary exact-target output is reserved and reviewed, and that output can be partially loaded and closed with immutable dossier and actual source trace. Existing DIRECT_STOCK remains supported.

**Implemented and verified in isolated PostgreSQL/browser. Subsequently deployed to the configured shared Supabase on 18/11/2026 after separate approval:** see the [deployment record](container-production-deployment-2026-11-18.md). The updated [core plan](../plans/container-core-completion-plan-2026-11-17.md#9-đợt-2--s2-hoàn-thiện-các-yêu-cầu-còn-lại) limits remaining implementation to requirement-backed S2 work and excludes extensions. This is not whole-SRS acceptance, a legal shipment certificate, a remote CI result or a witnessed deadline sign-off.

## What changed

- Added the production request/draft/commit and actual-output approve/reject endpoints, assigned-operator worklist and Export/CM production panel. Persisted owner, role, facility, approval and version guard every transition/replay.
- Reused Ledger processing/code issuance/compatibility/tolerance/variance confirmation. The protected production entry invokes Ledger on the same caller transaction; no broad refactor of the ordinary command service or role impersonation.
- Added forward migration `20261117000000_ContainerProduction`: four private production/review/request/event tables, immutable history and demand/revocation guards, one public runtime entry, append-only output re-reserve semantics and updated protected container read/write routines.
- INPUT consume, physical BLENDING output/lineage, output reservation, production marker and receipt/audit are atomic. Production alone keeps actual loaded at zero; first real load changes the business status to LOADING.
- Actual output has a separate reviewed snapshot/fingerprint. It does not overwrite the input proposal or pretend the original selection was DIRECT_STOCK. Output review rejection releases outstanding output without reversing production; pre-load re-review can re-reserve only available eligible stock, with a new RESERVE event and retained release history.
- Production commit and review change the container version, preventing an old loading tab from silently using a changed output authorization. Task, operation, container and original approval decision versions remain distinct.
- Current scientific dependencies are checked before and after final writes, with final legal/yield effective-time checks. Missing/changed evidence cannot be converted into PASS by replay or production success.
- Existing loading/CLOSE uses only the mapped reviewed output reservation, retaining exact source complement. Dossier includes production operation and output review references; trace is based on actual loading.
- Fixed the HOLD picker to include reserved/exhausted own-company lots, refreshed parent container status after mutation, separated demand-content changes from container-version changes in planning labels, labeled exhausted quota correctly and removed repeated event-ID React keys in read-only tables.

## Checks actually run

| Check | Result |
| --- | --- |
| `dotnet test GrainLedger.slnx` on final implementation | **598 passed, 0 failed, 0 skipped**, PostgreSQL Testcontainers; about 2m33s reported test duration. |
| Focused container production/execution/approval/legacy regression | **27 passed**; includes 8 new production cases and existing DIRECT_STOCK coverage. |
| `npm test` | **241 passed**, 34 files in the full run before the final table regression. The final focused container/table run is recorded separately below. Three new production UI tests cover uncertain exact-request retry across remount, unsaved measurement gating and reviewer role controls. |
| Final focused container/table frontend run | **14 passed**, 3 files; includes the final multi-output key regression. |
| `npm run build` | PASS, TypeScript and production Vite build. Existing large-chunk warning remains; no performance claim. |
| Real browser path | PASS with Playwright/Brave against production frontend assets and the existing loopback BrowserWorkflowHost forwarding to actual API handlers on a disposable PostgreSQL database. |
| Documentation links / whitespace | Checked separately after the documentation updates. |

Backend tests cover: actual output review before loading; input/output accounting and exact two-source conservation; reject/re-review and route-bound replay; wrong roles/foreign scope; rollback after input consume and after physical output creation; concurrent production; draft input/quantity tampering; stale input/output evidence; role revocation while waiting for the stock lock; legal expiry injected at the final audit write; and denied direct private helper/table access. Expiry fault injection is isolated and deterministic, not an arbitrary sleep or a shared-database trigger.

The new UI transport failure test is a component test, supplemented by real API receipt replay integration coverage. The browser record below is the positive end-to-end path; it is not labeled a production response-loss browser test.

Pre-existing compiler/analyzer warnings remain (unused LedgerService constructor parameter, existing xUnit assertions). No unrelated refactor was made to silence them.

## Browser evidence

The host provisioned a synthetic two-source fixture through existing test/API helpers; it began with an approved plan. Test-only loopback session bootstrap created real validated cookie sessions for the three roles; the normal auth/CSRF and business handlers were retained. This is not a claim that production onboarding or SMTP was tested.

1. Export requested production; state REQUESTED.
2. Assigned Operator opened Ledger → Approved production, saved the complete actual blend draft and committed it. State PRODUCED, one output of 20 kg, actual container loading still zero.
3. Export saw the finished-output review blocker and disabled loading.
4. CM uploaded synthetic quality evidence for the actual output, confirmed moisture 13% and approved actual output with a reason.
5. Export saved container/seal and loaded **4.000001 + 15.999999 = 20.000000 kg**. Parent status updated to LOADING, without requiring a manual page reload.
6. Export confirmed CLOSE; status CLOSED, two actual loads.
7. CM traced **two sources of 10.000000 kg each** and read the immutable dossier. Its output authorization referenced the actual production operation and separate output review.
8. No duplicate-key React console error was observed in this path. The isolated test host is temporary and stopped after verification.

Final fixture (isolated only): container `5a13bcc4-7a01-466e-82af-e112c6d1f36b`; consult the artifact manifest for the exact generated IDs. No fixture UUID in this record grants authority in another environment.

Artifacts are local and temporary, not committed evidence or a long-term acceptance archive:

- `/tmp/container-production-tests/all-backend-final.trx`
- `/tmp/container-production-tests/container-final.trx`, `production-mass-boundaries.trx` (focused follow-up covering numeric processing mass/loss refusal)
- `/tmp/container-production-web-test2.log` (full 241-test run), `/tmp/container-production-focused-web-final.log` (final 14 focused tests), `/tmp/container-production-web-final-build.log`
- `/tmp/container-production-browser-final/results.json`, `closed-export.png`, `dossier-cm.png`
- `/tmp/container-production-shared-readonly-preflight.json`

Session/cookie fixtures and real secrets are excluded from repository evidence. Do not publish the BrowserWorkflowHost session manifest, which contains test session cookies.

## Shared-database status at implementation time

Read-only preflight against the configured shared database found the latest installed migration `20261114000000_ResidueProfileRetirement`, no production table/entry, and the existing RELEASE/CONSUME-only reservation action constraint. The metadata read ran in a READ ONLY transaction and rolled back. No shared migration, grant, seed or business-data write was performed in this implementation turn; `.env` was preserved.

After this implementation run, the user separately approved the new migration and runtime grant. They were applied with a backup and read-only runtime checks; see the [deployment record](container-production-deployment-2026-11-18.md). The [deployment checklist](../guides/container-production-deployment.md) still governs any other environment. Normal startup does not install migrations or grants.

No commit, push, PR or remote CI was performed. Existing untracked test reports from the earlier container run were preserved.

## Remaining scope

The list below records S1 capability limits, not an instruction to implement every unsupported variant. The user's subsequent scope decision is reflected in sections 9–10 of the core plan: required S2 workflows remain; unsupported extensions and unconfirmed quantity/legacy policies are excluded.

S1 supports one plan, one complete batch, one exact-target ordinary BLENDED_RICE output, pre-load review/re-review and partial loading. Current limitations remain explicit blockers:

- Revalidation after any actual loading; ALL_REMAINING and multiple-plan CLOSE/plan switching.
- Mixed pool/custody generation or successor-position reservation handoff; transfer/bagging of reserved output.
- Partial/multi-batch production, output surplus/shortage and multi-container output allocation.
- Container notification inbox, versioned shipment documents/dossier supplements, legacy mapping and full-season/staging acceptance.

HOLD release, unload/reopen/reseal and shortage closure are not introduced. The successful S1 flow does not remove those business-policy boundaries.
