# Container S2 — implementation plan, 18/11/2026

Status: implementation and focused isolated verification completed after S1 commit `8fd96ac`; subsequently deployed to the configured shared Supabase after separate explicit S2 approval; see the [deployment record](../reports/container-s2-deployment-2026-11-18.md). This is the second implementation stage of the [core plan](container-core-completion-plan-2026-11-17.md), scoped to the seven items reconfirmed by the user. Complete implementation first, then run one focused container verification phase; do not run the full backend suite between changes.

## Required work

1. Scope-aware re-review after partial production/loading: ALL_REMAINING and OUTPUT_ONLY, immutable review bases, exact outstanding reservations and no repeated spent inputs (FR-EXP-003, R03/RV03).
2. Activate P1/P2 one at a time; preserve historical output/loading identity and assess every committed load at CLOSE (FR-EXP-003/004, RV03/RV04).
3. Execute only remaining approved quantities, retaining actual batch measurements, yield/variance checks and operation history. No arbitrary quantity exceptions or surplus allocation (FR-LED-001/002, RV03).
4. Integrate existing mixed-stock custody, ordinary transfer and bagging with explicit reviewed reservation handoff; conserve exact source shares, retain restrictions and deny unsupported stock generation changes (V2 p.6, FR-LED-001, RV02).
5. Retained scoped HOLD notifications/read state and assigned-manager acknowledgement/isolation/response. These actions never release HOLD. Resuming loading/CLOSE after HOLD, release/cancellation, auto-HOLD and unload/reopen/reseal remain excluded (FR-TRC-002, R09/R10; user clarification).
6. Versioned shipment documents and append-only evidence supplements with reason/user/time, immutable closing dossier and scoped file access (FR-EXP-005).
7. Complete container regression and browser evidence, remaining CT-16/18/25/26 fixtures, positive production CT-27, supported/blocked custody CT-28, and a documented tracing dataset/oracle benchmark (NFR-REL, NFR-PER-01).

## Implementation order

- [x] Forward-only private schema and protected entry points, retaining existing grants and immutable S1 records. No shared database migration in this implementation run.
- [x] Review/activation/readiness and whole-manifest CLOSE contracts.
- [x] Partial execution and reviewed stock handoff using the existing caller-owned Ledger transaction and advisory lock.
- [x] HOLD notices/handling and document/supplement routes.
- [x] Web callers for all implemented transitions, role visibility and exact-request retries.
- [x] Focused backend/container and frontend checks, isolated actual API/browser verification and benchmark; fixes found in this final phase may receive targeted reruns only.
- [x] Update contracts, execution report and deployment package with actual outcomes and remaining source-policy limitations.

All quantities are decimal strings and exact six-place kg. Replays bind actor/action/payload. Persisted role/permission/company/facility and current evidence must be rechecked after waiting for locks and before commit. A changed scientific basis rolls the whole mutation back. New private tables/helpers remain denied to runtime; no owner startup, seeds, data resets, commit or push is authorized by this plan.


## Completion record

See [implementation/verification report](../reports/container-s2-implementation-2026-11-18.md) and [deployment package](../guides/container-s2-deployment.md). The supported core paths are implemented. Unsupported other-plan pool splits, automatic successor/top-up rebinding, ambiguous attribution and quantity exceptions remain explicitly blocked; no new business policy or post-HOLD release was invented. Browser evidence distinguishes actual UI transitions from API coverage and blocked outcomes; it is not a blanket PASS for CT-01–CT-30. At implementation completion, shared migration/grants, commit and push had not been performed. Shared migration/grants were subsequently applied with separate explicit approval; commit/push remain unperformed.

The two remaining complete browser walkthroughs (P1→P2→CLOSE and pool→transfer/partial loading/bagging→CLOSE without HOLD) subsequently passed on fresh isolated fixtures; see [additional browser verification](../reports/container-s2-web-walkthroughs-2026-11-18.md). Shared migration/grants were subsequently applied with separate explicit approval; the deployment record distinguishes isolated test data from existing Supabase data.
