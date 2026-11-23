# Container S2 workflow

Forward migration `20261118000000_ContainerWorkflow` extends S1 without resetting stock or rewriting committed evidence/load/dossier history. One new runtime entry is `grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb)`; its tables/helpers remain private. Normal startup does not migrate or repair grants. Shared deployment requires approval for this migration and new entry; the previous S1 approval is not reused.

## Review and execution

- `GET /api/export-containers/{id}/workflow` returns scoped proposals/operational positions, review history, handoffs, document metadata, supplements and HOLD handling. Mill actors see assigned operational fields and no scientific review bases or document bytes.
- `POST .../workflow/reviews`: `{operationId,version,proposalId,scope,decision,reason}`. Persisted CustomerManager with `blend.approve`; APPROVED also requires `residue.evaluate`. Scope is ALL_REMAINING or OUTPUT_ONLY; decision APPROVED/REJECTED; reason 1–1000 characters. The server derives the exact remaining input/output scope, assesses current policy/evidence and retains the new basis. Clients cannot submit PASS, actor or allocation authority.
- A new approval activates only that plan, releases the previous plan's unconsumed reservations and re-reserves eligible scope atomically. Existing batches/loads remain linked to their original plan. Rejection of an inactive proposal does not deactivate a different plan. Evidence-driven STALE transitions release unfinished authority, without reversing physical execution or releasing HOLD.
- New plan drafts after partial execution target only unproduced/unloaded demand. A P2 plan cannot rename or adopt P1-produced outputs. Versioned order specification changes after execution invalidate unfinished authority; old outputs can receive OUTPUT_ONLY review against current requirements. Header/target changes after execution remain unsupported.
- `POST /api/export-containers/{id}/production/next` accepts `{operationId,version}` for an assigned MillOperator. Use the production task `version` from `GET /api/export-containers/{id}/production`. Starts a new saved draft only when production quota remains. Existing production draft/commit routes allow a partial batch with the original reviewed recipe, exact input fractions and remaining quotas. `plannedKg` is the original plan output quantity, `remainingProductionKg` is the quantity pending production, and `targetKg` is the current draft/batch quantity or remaining target. Production remains separate from loading.
- ALL_REMAINING is required before executing further inputs after a scope review. OUTPUT_ONLY never reruns spent inputs. A new batch/output identity requires actual output evidence/review before loading. Current scientific dependencies are checked again after writes and before commit.

New actual loading requires `loadedAt` between the creation time of each loaded lot and the database clock. The entire request rolls back when a line falls outside that range.

CLOSE assesses every committed load across plan versions and current requirements, retaining per-plan latest review/production references and the actual whole-manifest basis. Old authorization remains in immutable load records; CLOSE does not debit stock again. Exact target/order limits and current HOLD restrictions remain enforced.

## Reviewed stock handoff

Assigned mill worklist: `GET /api/container-work-items` (Operator `containers.operate` or Manager `holds.process`).

1. Assigned Operator saves `POST .../workflow/handoffs` with `{operationId,version,proposalId,kind,content}`. Kind TRANSFER/BAGGING. Content names `lotId,fromLocationId,toLocationId,quantityKg,occurredAt` and current `poolRevision` for pools; BAGGING also names `equipmentId,outputKg,declaredLossKg`.
2. CustomerManager sends `POST .../handoffs/approve` with `{operationId,version,handoffId,handoffVersion,reason}`. A current scientific assessment and exact saved scope are retained.
3. Assigned Operator sends `POST .../handoffs/commit` with `{operationId,version,handoffId,handoffVersion}`. INPUT authorization, reservation release/establishment/consumption, existing Ledger transfer/bagging, exact source complement, output claim/reservation and receipt share one transaction.
4. CustomerManager reviews the resulting actual positions before Export loads them. New positions/generations do not inherit the old PASS automatically.

Ordinary transfer retains lot identity; custody extraction uses existing aliquot/remainder routines and source-wise complement. Direct Export loading from a current mixed pool remains blocked until an assigned operator performs the reviewed extraction. Other-plan pool reservations prevent an ambiguous split; external top-up never automatically rebinds their quota. Partial ordinary production is supported; pool partial execution that cannot preserve its pinned remaining input identity remains blocked. Output storage for approved blend production remains ordinary; explicit reviewed handoff can subsequently move/bag it. BAGGING handoff is limited to equal actual input/output and zero declared loss; surplus/shortage policy, arbitrary recipes, cross-facility or ownership transfers are not introduced.

## Shipment records

`GET /api/export-orders/{id}` returns `completedMassKg` from actual load lines with a committed CLOSE and `fulfillmentStatus` (`IN_PROGRESS` or `COMPLETE`). `allocatedMassKg` and `remainingMassKg` continue to describe planned demand allocation.

- `POST .../workflow/documents`: Export `{operationId,version,documentType,filename,contentType,content,reason}` after CLOSED. `content` is base64, 1 byte–10 MiB; safe filename ≤200 characters, document type ≤80, reason ≤1000. Server computes SHA-256 and allocates the next per-container/type version under the writer lock. Same request replays without another version.
- `POST .../workflow/documents/packing-list` accepts `{operationId,version}` from Export after CLOSE. The server generates printable HTML from recorded container and seal identifiers, order and commodity codes, closure time and net mass by loaded lot. Each request creates the next `PACKING_LIST` version with a SHA-256 hash. Retrying the same request returns its saved result. Package counts and gross or tare weights are omitted because the manifest does not record them.
- `GET .../workflow/documents/{document}/file` returns a scoped Export/CM attachment with immutable bytes and hash.
- `POST .../workflow/supplements`: Export `{operationId,version,evidenceId,reason}` after CLOSED. Evidence must belong to the same company and a shipment facility. Append actor/time/reason without overwriting the closing snapshot or reopening CLOSE.

## HOLD notices and handling

Trace HOLD forms submit immutable evidence and a reason. A Customer Manager can use `POST /api/ledger/holds` with `{id,lotId,evidenceId,reason}` or `POST /api/export-containers/{id}/execution/holds` with `{operationId,version,evidenceId,reason}`. Evidence must match the target company and a recorded facility. Older request shapes continue to work. In the placement transaction, the system retires any affected approval and releases only the unused reservation. Container HOLD blocks draft, preview, proposal approval and further execution. Lot HOLD alerts active own-company Export Officers and Mill Managers assigned to affected shipment positions, in addition to existing recipients.

Container HOLD creation commits unique per-recipient notices for responsible own-company CM/Export and assigned Mill Managers. Missing required recipients rolls back HOLD. `GET /api/container-notifications` returns only current authorized recipient notices, including unread notices retained while offline. `POST .../workflow/notifications/read` accepts `{operationId,version,noticeId}` and does not release restrictions.

`POST .../workflow/hold-responses` accepts `{operationId,version,holdId,facilityId,responseAction,evidenceId,reason}` for an assigned MillManager. Container handling supports ACKNOWLEDGE/RECORD_RESPONSE. Physical remaining-lot isolation continues through the existing Ledger lot HOLD/transfer/handling routes; container unload/isolation movement is not invented. Reading, acknowledging, isolating or recording response never clears HOLD. Resuming loading after HOLD, HOLD release/cancellation, auto-HOLD and unload/reopen/reseal are excluded by the user's scope decision.

## Business history

`GET /api/ledger/business-history` unifies permitted procurement and arrival, laboratory, export plan, reservation, container, HOLD, trace, processing/correction and administration events. Optional filters are `category`, exact `eventType`, partial `search` (record code/ID, event type or linked reference), inclusive UTC dates `from` and `to` in `YYYY-MM-DD`, `page` and `pageSize`. Warehouse visibility follows assigned facility scope. Tenant and administration records follow their current role permissions. Event summaries include actor, time, reference, selected versions and evidence references when permitted.

## Transactions, errors and browser retries

Every mutation uses caller-owned READ COMMITTED, stock advisory lock `714928352` before company/rows, and persisted current actor/permission/owner/facility scope after lock wait, including replay. Replay binds actor, action, container and payload. Changed identity/content/version conflicts without partial writes. Scientific bases are internal and omitted from non-CM history; role-specific dossier redaction is preserved.

Mass is a positive decimal string with six-place kg precision, never a JSON number or JavaScript float. Ordinary workflow conflicts return 409, invalid payloads 400, wrong scope/role 403, missing schema 503. The web retains exact uncertain requests per user/container in IndexedDB, including document bytes too large for sessionStorage. Retry keeps operation ID, route and payload. A definitive rejection permits a fresh request after reload.

See [S2 implementation plan](../plans/container-s2-implementation-plan-2026-11-18.md) and [web walkthrough](../guides/container-s2-web-test.md). A capability description is not a shared-deployment or acceptance result.


Container source trace giữ nguyên endpoint và source totals; phần Flowchart mới bổ sung graph backward từ toàn bộ actual manifest. Xem [contract tracing](traceability-api.md#container-nodes-và-backward-từ-container) và [kịch bản web](../guides/container-tracing-flowchart-web-test.md). Migration tracing là bước deployment riêng, chưa được áp dụng chỉ vì code đã có.

The completion migration `20261123000000_ContainerTraceCompletion` adds the loading and HOLD guards and the scoped history and order-progress routines. Run the existing explicit migration and runtime-grant configuration before deploying the new code.
