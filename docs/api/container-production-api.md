# Approved container blend production

Implemented S1 scope: one approved BLEND_INPUT_STOCK plan, one complete BLENDING operation and one exact-target ordinary finished output. Production and output review are separate from actual container loading. This document describes code; shared-database deployment requires its own approval.

## Workflow and authority

1. ExportStaff requests production for an own-company active approval. Current input assessment, unchanged demand, ordinary stock and restrictions must pass.
2. An assigned MillOperator saves actual processing measurements against that request. Inputs must exactly match the approved lot/location/quantity set; the server generates the output code using existing Ledger issuance. One ordinary BLENDED_RICE output must equal the container target. A different measured output is refused, not rounded or silently adjusted.
3. The operator commits the saved draft. Current input evidence is rechecked. INPUT consumption, normal Ledger BLENDING postings/lineage, output reservation and execution/audit/receipt commit together. Effective processing tolerance/compatibility and any required MillManager variance confirmation use existing Ledger rules. Production is not actual loading.
4. Confirm finished-lot quality through Export assessment. CustomerManager reviews actual output against current residue, quality, MRL and assessment policy. Only current PASS grants output loading authority. The original input proposal is immutable; a separate output review retains its actual-output snapshot/fingerprint.
5. ExportStaff uses the existing [loading/CLOSE API](container-execution-api.md). Only the reviewed output position can be loaded; INPUT reservations cannot be loaded as finished output. Loading can be partial. CLOSE retains production/output-review references and does not debit stock again.

No role is borrowed or impersonated. Mill actors receive operational task fields; failed production checks return status/findings without the internal scientific evidence snapshot. Wrong owner/facility and role are rejected by persisted server/database scope checks.

## Routes

| Route | Permission/actor | Contract |
| --- | --- | --- |
| `GET /api/container-production` | `containers.operate`, assigned MillOperator | List non-closed tasks in assigned facilities. |
| `GET /api/export-containers/{id}/production` | Authenticated and scoped Export/CM or assigned mill actor | Current task; `NOT_REQUESTED` when absent. Operational IDs, input quantities, operation and output are returned; no private assessment context. |
| `POST .../production/request` | `containers.write`, own ExportStaff | `{operationId, version, proposalId}`; version is the current **container** version. |
| `POST .../production/draft` | `conversions.write`, assigned MillOperator | `{operationId, version, draft}`; version is the **production task** version. Draft is the existing Ledger draft shape. |
| `POST .../production/commit` | `conversions.write`, assigned MillOperator | `{operationId, version}`; consumes the saved complete production draft. |
| `POST .../production/approve` | `blend.approve`, own CustomerManager | `{operationId, version, reason}`; review actual output before any loading. |
| `POST .../production/reject` | `blend.approve`, own CustomerManager | Same review fields; releases outstanding output reservation, retains production/history. |

Mutations retain real authentication/CSRF. DTOs reject unknown root fields, actor/ready/approved claims and invalid versions/IDs. Processing masses must be decimal strings; kg precision is six places, no exponent/rounding. Loss may be zero; input/output masses must be positive. Reason is nonblank and at most 1,000 characters. Output stage/product/commodity, facility and quantities are enforced server-side.

`draft` contains `id`, `ownerId`, `facilityId`, `kind`, `occurredAt`, `equipmentId`, `inputs`, `outputs`, `declaredLossKg`, plus the saved operation `version` when editing. Each input has `lotId`, `locationId`, `quantityKg`. The single output has `id`, server-reserved `code` (empty at creation), `commodityId`, `stage`, `productIdentity`, `locationId`, `quantityKg`. Existing operation confirmation is invalidated by edits as before.

The task response includes `version`, `containerVersion`, `state`, `operationId`, `outputId`, `outputLocationId`, `outputCode`, `targetKg`, and scoped draft/input information when available. `containerVersion` is refreshed after commit/review; it is not interchangeable with task version or original F15 decision version.

## States, review and reservations

`NOT_REQUESTED → REQUESTED → DRAFT → PRODUCED → APPROVED`.

- Saved draft edits remain DRAFT and increment task version. One committed operation is supported.
- PRODUCED has real output stock reserved for this container, but loading is blocked until output review.
- CM rejection before loading changes the task to REJECTED and appends RELEASE; no stock/production reversal and no HOLD release.
- A subsequent pre-load review can reserve that same output again only if stock is still available, unrestricted, at the original position and currently PASS. Re-review appends a RESERVE event; it does not modify the original reservation or releases, nor rerun inputs.
- Output/authority identities are given by immutable proposal selection, production-operation mapping and output-review records. Their reservation purposes are INPUT versus the mapped OUTPUT; DIRECT_STOCK keeps its existing approved-position semantics.
- Container status stays PLANNED while loaded mass is zero. Production commit and output reviews increment container version so stale tabs cannot load against a superseded output review. First actual load changes status to LOADING, then exact-target CLOSE to CLOSED.
- Revocation/order-target revision after actual production is refused even while container status remains PLANNED. Existing revocation before production remains possible. If its approval is replaced, the unexecuted task reads APPROVAL_CHANGED; Export can request against the new approval. Old drafts/receipts/events remain history and confer no new output authority.
- After any loading, output re-review/rejection is blocked in S1. Changed evidence or HOLD blocks continuation/CLOSE; [S2](../plans/container-core-completion-plan-2026-11-17.md) supplies full partial-execution revalidation and plan transitions.

Lifetime reservation/event totals can include both consumed INPUT and reserved/consumed OUTPUT, and repeated pre-load reserve/release cycles. They are not physical mass or container loaded mass. The container execution view exposes only the mapped output allocation for BLEND_INPUT_STOCK; actual loading is summed only from committed load lines.

Readiness distinguishes `APPROVED_PRODUCTION_PENDING`, `OUTPUT_REVIEW_REQUIRED`, `OUTPUT_REVIEW_REJECTED`, changed evidence and unsupported handoff. A plan with no production still reports `BLEND_OUTPUT_HANDOFF_REQUIRED`. Pool/custody remains explicitly blocked.

## Atomicity, replay and freshness

Use one caller-owned READ COMMITTED transaction, stock advisory lock `714928352`, company lock then stock/container rows. Protected production invokes the existing Ledger routines on that connection; the ordinary command API is not granted an arbitrary reservation-consume bypass. No transaction spans user input or an external call.

Same actor, operation ID, action and payload replays the stored receipt, including after later state changes. Changed action/payload conflicts. Active role, permission and facility/owner scope are checked before replay. UI retains uncertain network/408/5xx/malformed results in per-user/container sessionStorage and retries the exact request. Refresh after replay for current state.

Input approval fingerprints are recomputed before production; actual output is assessed independently. Scientific dependencies are read again after final writes; a changed fingerprint rolls back the complete operation. Legal/yield effective-time checks run at the final boundary as well, including time changes without a publisher write. Existing writers continue protecting reserved stock.

## Installation and limits

Forward migration `20261117000000_ContainerProduction`, SQL resource `ContainerProduction.sql`, and one new runtime entry `grainledger.ledger_container_production(uuid,uuid,text,jsonb,jsonb)`. New production/review/request/event tables and private helpers stay inaccessible directly to runtime; immutable history triggers remain enforced. The migration extends reservation events with append-only RESERVE and updates the protected outstanding calculation and container views/commands without converting legacy preparations or editing historical postings.

See [deployment checklist](../guides/container-production-deployment.md). No shared migration/grant/seed is implied by code changes or normal startup.

Deferred: mixed pools/custody successors, moving reserved output, multi-batch/partial production, output quantity exceptions, multiple plans/containers, revalidation after partial loading, dossier supplements/documents and notification inbox. HOLD release/unload/reopen are not implicitly introduced.
