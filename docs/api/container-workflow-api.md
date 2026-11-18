# Container S2 workflow

Forward migration `20261118000000_ContainerWorkflow` extends S1 without resetting stock or rewriting committed evidence/load/dossier history. One new runtime entry is `grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb)`; its tables/helpers remain private. Normal startup does not migrate or repair grants. Shared deployment requires approval for this migration and new entry; the previous S1 approval is not reused.

## Review and execution

- `GET /api/export-containers/{id}/workflow` returns scoped proposals/operational positions, review history, handoffs, document metadata, supplements and HOLD handling. Mill actors see assigned operational fields and no scientific review bases or document bytes.
- `POST .../workflow/reviews`: `{operationId,version,proposalId,scope,decision,reason}`. Persisted CustomerManager with `blend.approve`; APPROVED also requires `residue.evaluate`. Scope is ALL_REMAINING or OUTPUT_ONLY; decision APPROVED/REJECTED; reason 1–1000 characters. The server derives the exact remaining input/output scope, assesses current policy/evidence and retains the new basis. Clients cannot submit PASS, actor or allocation authority.
- A new approval activates only that plan, releases the previous plan's unconsumed reservations and re-reserves eligible scope atomically. Existing batches/loads remain linked to their original plan. Rejection of an inactive proposal does not deactivate a different plan. Evidence-driven STALE transitions release unfinished authority, without reversing physical execution or releasing HOLD.
- New plan drafts after partial execution target only unproduced/unloaded demand. A P2 plan cannot rename or adopt P1-produced outputs. Versioned order specification changes after execution invalidate unfinished authority; old outputs can receive OUTPUT_ONLY review against current requirements. Header/target changes after execution remain unsupported.
- `POST /api/export-containers/{id}/production/next`: `{operationId,version}` for an assigned MillOperator. Starts a new saved draft only when production quota remains. Existing production draft/commit routes now allow a partial batch with the original reviewed recipe, exact input fractions and remaining quotas. `plannedKg` is the original plan output quantity; `remainingProductionKg` is not yet produced; `targetKg` is the current draft/batch quantity or remaining target. Production still does not count as loading.
- ALL_REMAINING is required before executing further inputs after a scope review. OUTPUT_ONLY never reruns spent inputs. A new batch/output identity requires actual output evidence/review before loading. Current scientific dependencies are checked again after writes and before commit.

CLOSE assesses every committed load across plan versions and current requirements, retaining per-plan latest review/production references and the actual whole-manifest basis. Old authorization remains in immutable load records; CLOSE does not debit stock again. Exact target/order limits and current HOLD restrictions remain enforced.

## Reviewed stock handoff

Assigned mill worklist: `GET /api/container-work-items` (Operator `containers.operate` or Manager `holds.process`).

1. Assigned Operator saves `POST .../workflow/handoffs` with `{operationId,version,proposalId,kind,content}`. Kind TRANSFER/BAGGING. Content names `lotId,fromLocationId,toLocationId,quantityKg,occurredAt` and current `poolRevision` for pools; BAGGING also names `equipmentId,outputKg,declaredLossKg`.
2. CustomerManager sends `POST .../handoffs/approve` with `{operationId,version,handoffId,handoffVersion,reason}`. A current scientific assessment and exact saved scope are retained.
3. Assigned Operator sends `POST .../handoffs/commit` with `{operationId,version,handoffId,handoffVersion}`. INPUT authorization, reservation release/establishment/consumption, existing Ledger transfer/bagging, exact source complement, output claim/reservation and receipt share one transaction.
4. CustomerManager reviews the resulting actual positions before Export loads them. New positions/generations do not inherit the old PASS automatically.

Ordinary transfer retains lot identity; custody extraction uses existing aliquot/remainder routines and source-wise complement. Direct Export loading from a current mixed pool remains blocked until an assigned operator performs the reviewed extraction. Other-plan pool reservations prevent an ambiguous split; external top-up never automatically rebinds their quota. Partial ordinary production is supported; pool partial execution that cannot preserve its pinned remaining input identity remains blocked. Output storage for approved blend production remains ordinary; explicit reviewed handoff can subsequently move/bag it. BAGGING handoff is limited to equal actual input/output and zero declared loss; surplus/shortage policy, arbitrary recipes, cross-facility or ownership transfers are not introduced.

## Shipment records

- `POST .../workflow/documents`: Export `{operationId,version,documentType,filename,contentType,content,reason}` after CLOSED. `content` is base64, 1 byte–10 MiB; safe filename ≤200 characters, document type ≤80, reason ≤1000. Server computes SHA-256 and allocates the next per-container/type version under the writer lock. Same request replays without another version.
- `GET .../workflow/documents/{document}/file`: scoped Export/CM attachment download, immutable bytes/hash. No developer-invented PDF/Excel template or legal certificate is generated.
- `POST .../workflow/supplements`: Export `{operationId,version,evidenceId,reason}` after CLOSED. Evidence must belong to the same company and a shipment facility. Append actor/time/reason without overwriting the closing snapshot or reopening CLOSE.

## HOLD notices and handling

Container HOLD creation commits unique per-recipient notices for responsible own-company CM/Export and assigned Mill Managers. Missing required recipients rolls back HOLD. `GET /api/container-notifications` returns only current authorized recipient notices, including unread notices retained while offline. `POST .../workflow/notifications/read` accepts `{operationId,version,noticeId}` and does not release restrictions.

`POST .../workflow/hold-responses` accepts `{operationId,version,holdId,facilityId,responseAction,evidenceId,reason}` for an assigned MillManager. Container handling supports ACKNOWLEDGE/RECORD_RESPONSE. Physical remaining-lot isolation continues through the existing Ledger lot HOLD/transfer/handling routes; container unload/isolation movement is not invented. Reading, acknowledging, isolating or recording response never clears HOLD. Resuming loading after HOLD, HOLD release/cancellation, auto-HOLD and unload/reopen/reseal are excluded by the user's scope decision.

## Transactions, errors and browser retries

Every mutation uses caller-owned READ COMMITTED, stock advisory lock `714928352` before company/rows, and persisted current actor/permission/owner/facility scope after lock wait, including replay. Replay binds actor, action, container and payload. Changed identity/content/version conflicts without partial writes. Scientific bases are internal and omitted from non-CM history; role-specific dossier redaction is preserved.

Mass is a positive decimal string with six-place kg precision, never a JSON number or JavaScript float. Ordinary workflow conflicts return 409, invalid payloads 400, wrong scope/role 403, missing schema 503. The web retains exact uncertain requests per user/container in IndexedDB, including document bytes too large for sessionStorage. Retry keeps operation ID, route and payload. A definitive rejection permits a fresh request after reload.

See [S2 implementation plan](../plans/container-s2-implementation-plan-2026-11-18.md) and [web walkthrough](../guides/container-s2-web-test.md). A capability description is not a shared-deployment or acceptance result.
