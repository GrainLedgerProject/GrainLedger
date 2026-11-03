# F14 frontend implementation plan

Date: 2026-10-26
Branch: feature/implement-export
Baseline: 89d205c
Status: core F14 frontend implemented on this branch. Shared DB migrations and grants were applied on 2026-10-27; authenticated browser acceptance remains open.

## Implementation result

- The working web app now links container detail to paged blend drafts and the plan workspace. Users can create, cancel, inspect history, read facility candidates and lineage, run assessed previews, save proposals and review freshness.
- The Export draft DTO now reports `hasProposal`, so the UI can distinguish a saved proposal from an active draft without guessing from `status`.
- Stable operation IDs and exact request bodies are retained in session storage after uncertain create, cancel and propose outcomes. Preview references are hidden after a conservative local timeout; the backend remains authoritative.
- Focused F14 UI tests cover draft creation, incomplete evidence, proposal retry, candidate revision paging and stale proposal display. F14/F13 focused UI tests passed 14/14; the production frontend build and Export module build passed. The F14 proposal integration test passed on disposable PostgreSQL, including the new `hasProposal` response. See the [test guide](f14-frontend-test-guide.md).
- The full frontend suite passed 186/199. Thirteen unrelated residue-rule maintenance/company-policy tests failed or timed out; those components were not changed by this F14 work. They also failed when run by themselves. Record this as an existing branch gate before merging.
- The configured shared database received the reviewed F14 migrations and restricted runtime grants on 2026-10-27. An authenticated end-to-end browser scenario against an isolated database remains required before acceptance; no data-changing smoke scenario was run against the shared database.

## Goal

Extend the working React app in apps/web/src so Export Staff can open an F13 container, create a demand draft, inspect facility stock and ancestry, run the backend planner, review assessment findings, and save an immutable proposal. Customer Managers can read the resulting plans. Use the existing backend contracts in docs/f14-blend-planning.md.

F15 approval/reservation and F16 physical blending/loading are separate workflows. Every F14 screen must state that a proposal has not reserved stock. A PASS is an assessment result, not approval to execute.

## User flow and screens

Export orders -> Order detail -> Container detail -> Blend plans -> Draft workspace -> Preview -> Saved proposal.

1. Extend container detail with a paged Blend plans section and a Create draft action. Display version, creation time, demand currency, and server state. Do not infer that the newest draft is proposed from its version or status alone.
2. Create draft form shows the saved order market, commodity, specification and container target as read-only context. The user chooses target product identity. Send current order/container versions and one stable operation ID. Facility is chosen in the draft workspace; the draft-create contract does not persist it.
3. Draft workspace has a compact demand summary and three sections: Stock and sources, Preview and findings, History. Use separate panels/tabs rather than one long form. Provide navigation back to container and order.
4. Stock and sources contains a facility selector, a paged candidate table, an option to show excluded positions, and source detail on demand. Show names/codes, location kind, available kg, physical eligibility, product compatibility and exclusion reasons. Keep UUIDs/revisions out of primary labels.
5. Preview shows the backend allocations, total input, predicted output, target, assessment result and actionable findings. Expand residue, quality, MRL, policy and evidence detail on demand. Do not add manual allocation inputs: the current preview API searches the facility scope automatically.
6. Proposal detail shows the immutable saved allocations and assessment alongside separately loaded freshness and reasons. Include paged history and explicit refresh. A stale proposal retains its historical results but cannot be presented as currently valid. A new plan uses a new draft.

Implemented routes, following the existing app navigation pattern:
- /export/containers/:containerId (existing, extended)
- /export/blend-plans/:planId

Draft creation is an action in container detail. Saved proposal and freshness appear in the plan workspace.

## Original implementation checklist

The result above records what is implemented and verified. The following checklist preserves the original work breakdown; unchecked acceptance and deployment items remain test gates.

### 1. Typed contracts, permissions and navigation

- [ ] Add focused F14 types/API helpers and route matching in the working app, not the nested prototype.
- [ ] Reuse api/apiWithEtag, CSRF handling, existing panels, notices, paging and session patterns.
- [ ] Split export-orders.write from blend.propose; preview/propose also require residue.evaluate. Honor server capabilities and resource scope.
- [ ] Confirm facility and human-readable label lookup access for ExportStaff and CustomerManager. If an existing lookup requires unrelated Ledger privileges, add a minimal scoped backend lookup rather than broaden authorization.
- [ ] Keep decimal mass and quality values as strings. Format without converting precise values through JavaScript Number.
- [ ] Preserve navigation state and reject responses from an old facility/plan using AbortController or request identity.

Acceptance: F13 routes still work, direct F14 links reload correctly, and read-only users have no write controls.

### 2. Draft list, create, detail and cancellation

- [ ] Load planning context and paged draft list from container detail.
- [ ] Show separate storage/candidate readiness and dependency failures. A missing F14 schema must not disable F13.
- [ ] Create a draft with targetProductIdentity and current parent versions. Show supported product labels; BAGGED_RICE is a stage, not a target identity.
- [ ] Render immutable demandSnapshot with guarded parsing and a usable error state.
- [ ] Show demandCurrent explicitly; changed F13 demand requires a new draft.
- [ ] Cancel only when canCancel permits it, with confirmation, current If-Match and stable operationId.
- [ ] Support paged history and version navigation. Do not implement editing historical draft demand.

Acceptance: create/open/cancel/reopen works, stale parent versions recover through reload, and history is accessible beyond its first page.

### 3. Facility candidates and source inspection

- [ ] Call candidates with facilityId and planId; retain its scopeRevision.
- [ ] Pass scopeRevision on later pages. On a scope conflict, discard the mixed page set and reload from page one.
- [ ] Identify a position by lotId plus locationId, not lotId alone.
- [ ] Distinguish physical eligibility, product hints and compliance. POSSIBLE_BLEND_INPUT is not proof that every combination is compatible.
- [ ] Use full-scope summaries from the API; never sum the visible page to claim total stock or feasibility.
- [ ] Load lineage only when opened, passing locationId and candidateRevision. Show facility -> location -> lot/pool, source contributions, processing/custody and provenance gaps.
- [ ] Explain a pool as the current mixed stock represented by a lot at a location. A location is the storage place.
- [ ] Reset candidate pages and discard old previews when changing facility or draft.

Acceptance: ordinary lots, mixed pools, excluded positions and changed revisions display correctly without leaking data across facilities or companies.

### 4. Planner preview and assessment presentation

- [ ] Run POST preview with facilityId and the current plan-specific scopeRevision. Use the existing default maxIterations; no algorithm tuning field in the primary form.
- [ ] Show loading, no stock, insufficient evidence/policy, proven violations and search exhaustion distinctly.
- [ ] SEARCH_EXHAUSTED means no solution found within the search budget, not proof that no feasible solution exists.
- [ ] Render selection mode, allocations and predicted output from the backend. Do not calculate a client-side compliance verdict, yield or safety margin.
- [ ] Present findings by stock/compatibility, output mass, residues/MRL and quality. Show value, unit, threshold and referenced evidence when provided.
- [ ] Only enable propose when the preview response allows it and supplies a valid unexpired reference. The reference lasts ten minutes; backend validation remains authoritative.
- [ ] Do not gate proposing on planning-context.canPropose: that container-level flag is intentionally false.
- [ ] Keep per-lot residue-preview as optional diagnostic detail, clearly separate from the full plan assessment.

Acceptance: PASS, FAIL and INCOMPLETE remain distinct; absent evidence never produces a positive badge or enabled proposal action.

### 5. Proposal persistence, retries and freshness

- [ ] Propose using only the previewReference and stable operationId; do not submit client-authored assessments or allocations.
- [ ] Persist uncertain create/cancel/propose requests in session storage scoped to user/company/resource. Retry the exact payload before allowing a replacement request; do not generate a fresh operationId after an uncertain outcome.
- [ ] Disable duplicate submissions. An uncertain response must not be treated as a rejected write.
- [ ] After proposal creation, GET proposal for current freshness: the create response contains NOT_RECHECKED.
- [ ] Handle PLAN_ALREADY_PROPOSED by loading the saved proposal. Handle expired/stale preview by refreshing candidates and running a new preview.
- [ ] Separate saved assessment from CURRENT/STALE and transport/read failures. A failed freshness request is not CURRENT.
- [ ] Preserve the original snapshot when stale. Offer a new draft using current demand instead of editing the proposal.
- [ ] Render notices next to the affected action and focus validation errors; do not place all messages at the page top.

Acceptance: exact retries create one proposal, a late HOLD/evidence/stock change is visible, and no F14 action changes stock/postings/HOLD.

### 6. Supporting module data and deployment

- [ ] Verify real API availability for published MRL, company assessment policy, compatibility rules, yield rules when blending, F12 evidence/rules and confirmed finished-lot quality observations.
- [ ] Map missing-data findings to the responsible role/module and show understandable next actions. Do not claim an editing page exists until its route is implemented.
- [ ] Use existing owner APIs to prepare isolated test fixtures; do not invent default margins, yield or quality results.
- [x] Add Customer Manager FE for publishing assessment policies and confirming finished-lot quality against uploaded Ledger evidence under **Export assessment**. The quality observation read also permits the same scoped manager to see the current version. Assigned MillManager yield-rule setup remains separate in Ledger.
- [ ] Shared database migrations and runtime grants are a deployment gate requiring explicit approval under AGENTS.md. Prepare the reviewed sequence; do not run them as part of frontend startup.

### 7. Verification and delivery

- [ ] Focused React tests cover permissions, draft workflow, candidate paging/revision reset, findings, expiry, uncertain retry, conflicts and freshness.
- [ ] Run existing F13 tests, relevant app navigation tests and the web production build.
- [ ] Browser acceptance against the real backend and isolated PostgreSQL: F13 order/container -> draft -> facility -> candidates -> preview -> propose -> reopen -> history.
- [ ] Test both direct finished stock and a multi-lot blend with explicit policies and quality evidence.
- [ ] Test missing policy/evidence, quality/MRL failure, demand edit, HOLD/stock/evidence revision after preview, preview expiration and concurrent proposal creation.
- [ ] Verify stock/postings/HOLD before and after the browser flow.
- [ ] Exercise desktop and narrow mobile browser widths, keyboard navigation, focus, long labels and notices. This is responsive web, not a mobile app.
- [ ] Update this plan with completed checks and add an FE test guide with exact navigation, fixture prerequisites and expected results.

## API mapping

| FE action | Existing API |
| --- | --- |
| Read readiness | GET /api/export-containers/{id}/planning-context |
| List/create drafts | GET /api/export-containers/{id}/blend-plans; POST .../blend-plans/drafts |
| Open/cancel/history | GET /api/blend-plans/{id}; POST .../cancel; GET .../history |
| Read candidate positions | GET /api/export-containers/{id}/candidates?facilityId=...&planId=... |
| Inspect sources | GET /api/export-containers/{id}/candidates/{lotId}/lineage?facilityId=...&locationId=... |
| Compute assessed allocation | POST /api/blend-plans/{id}/preview |
| Save proposal | POST /api/blend-plans/{id}/propose |
| Read saved proposal/freshness | GET /api/blend-plans/{id}/proposal |

## Suggested code boundaries

Keep ExportOrders.tsx as the F13 entry point and add a dedicated BlendPlanning feature component plus small candidate, assessment and proposal components. Put request/response types and API helpers beside the feature. Reuse the existing app navigation and UI styles. Avoid adding a second router or a new state-management framework for this workflow.

## Completion criteria

A permitted user can complete the workflow in the browser without copying UUIDs or using Scalar, inspect why a plan is blocked, recover safely from stale data or uncertain writes, and reopen the saved immutable proposal. Required preparation data must exist. Report isolated acceptance separately from shared deployment. F14 frontend completion does not imply F15 approval/reservation is implemented.
