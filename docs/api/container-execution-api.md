# Planned container execution: direct stock, HOLD, closure and trace

Current implementation: `feat/container`, 13 November 2026. Follow [execution plan](../plans/container-execution-plan-2026-11-13.md) and [web walkthrough](../guides/container-execution-web-test.md). The [preparation API](container-api.md) remains a separate legacy draft workflow and never counts as actual loading.

## Supported boundary

The authoritative ID/order/company/target/status/version is F13 `exports_containers`. Protected Ledger extensions retain physical container/seal, pinned proposal/facility, actual loads, exact source allocations/complement, container HOLDs, closure dossier and request receipts/history. Nothing automatically converts legacy preparation records.

**DIRECT_STOCK** means the approved lots are already produced and physically present, including a pre-produced blended lot. First successful load starts execution; subsequent loads consume the same active approval. No standalone successful “start” request changes the status. An approval selecting **BLEND_INPUT_STOCK** remains blocked until the blend owner supplies actual production/output-reservation handoff. This feature does not grant ExportStaff mill-processing authority.

Current actual loading supports ordinary, non-custody-derived positions with proven source attribution. Mixed pools, withdrawn custody generations and reservation transfers through top-up/bagging/transfer require the handoff recorded in the plan. These cases are rejected, never silently remapped to a successor lot.

## Routes and authorization

All routes use `/api/export-containers/{id}/execution`, persisted authentication, no-store responses and cookie CSRF for POST. Administrator has explicit global trace authority only, without general export business access.

| Method/path suffix | Behavior | Authorization |
| --- | --- | --- |
| GET base | `{container,readiness}`; full actual manifest, reserved remaining and current phase checks | own-company ExportStaff/CustomerManager with `export-orders.read`; evaluation also needs `residue.evaluate` |
| POST `/metadata` | Set physical number/seal; optimistic version, history and stable operation ID | own-company ExportStaff, `containers.write`; not CLOSED |
| POST `/loads` | Append actual load, source shares, consume reservation and debit stock atomically | own-company ExportStaff, `containers.write` + `residue.evaluate`; current approved DIRECT_STOCK |
| POST `/holds` | Append container HOLD; may be placed after closure without rewriting dossier | own-company CustomerManager, `holds.manage` |
| POST `/close` | Exact-target check, current assessment/restrictions, atomic CLOSED+dossier | own-company ExportStaff, `containers.write` + `residue.evaluate` |
| GET `/history?page=1&pageSize=25` | Paged container execution events, actor IDs, server time and original request | own-company export readers; pageSize maximum 100 |
| GET `/dossier` | Historical immutable closure snapshot; JSON can be downloaded by the web UI | CM receives source/evidence detail; ExportStaff receives a redacted view without source composition/evidence or per-load assessments |
| POST `/trace` with `{}` | Actual source kg, display percentages and original Capture plot provenance; writes TRACEBACK audit | own-company CM or explicit global Administrator trace permission; ExportStaff backward requests return 403 |

Container mutation DTOs reject unknown fields. The server derives actor/company and assessment; a request-supplied `ready`, `approved`, actor, company or assessment cannot authorize a load. Unauthorized identifiers return 403 without disclosing foreign content.

### Example payloads

All mass is **decimal text**, positive, no more than six decimals and no rounding. Versions are integer container concurrency tokens, separate from F15 decision version.

```json
POST /api/export-containers/{id}/execution/metadata
{"operationId":"<stable UUID>","version":1,"containerNumber":"SYNTHETIC-C1","sealNumber":"SYNTHETIC-SEAL"}

POST /api/export-containers/{id}/execution/loads
{"operationId":"<new stable UUID>","version":2,"proposalId":"<active approved proposal UUID>","decisionVersion":1,"loadedAt":"2026-11-13T12:00:00Z","lines":[{"lotId":"<approved finished lot UUID>","locationId":"<approved position UUID>","quantityKg":"4.000001"}]}

POST /api/export-containers/{id}/execution/holds
{"operationId":"<stable UUID>","version":3,"reason":"Synthetic reviewed blocking finding"}

POST /api/export-containers/{id}/execution/close
{"operationId":"<stable UUID>","version":4}
```

The same actor/operation ID, route/action and canonical JSON payload returns the saved receipt, even after later state/evidence changes. Changed content/container/action conflicts. Current actor/company/role is checked before replay. A replay does not post stock or append duplicate events. GET detail after replay to see current state; the receipt preserves its original version.

UI persists the exact request in sessionStorage for uncertain network/408/5xx or malformed success responses. A retry preserves operation ID, payload, loading time and version. Known 4xx refusals allow a refresh/new request. No credentials or evidence bytes are stored there.

## Accounting and source attribution

Each actual load has immutable lines, actor/occurrence/recording times and the complete server assessment. `loadedKg` and `remainingKg` derive from all committed lines. Drafts, reservations and original output mass are not summed into loaded mass. Lines are restricted to approved lot/location quotas; duplicate positions must be combined before sending. At most 100 lines/request, 1,000 loads/container, 100 approved lots for the current assessment and 10,000 positive source shares/position are supported; unsupported sizes fail explicitly.

Private source balances capture the position's exact source complement. A single-source position retains its current mass as that source. A multi-source position initially requires creation contributions to equal that physical position and exactly one positive location; otherwise attribution is unresolved. After loading, the retained source complement must still equal physical stock. External processing/transfers that make it disagree require reconciliation; no ratios or source facts are silently fabricated.

Allocation uses integer micro-kg, exact `div`/`mod`, largest remainder and UUID tie-breaks. Per-load allocations sum exactly to loaded kg; remaining shares are the exact complement. Original Ledger creation contributions are never overwritten. UT-14's 25,000 kg out of 70,000 kg containing 11,600 kg of source L3 allocates **4,142.857143 kg** to L3 and **20,857.142857 kg** to the other source.

Actual loaded mass is retained as container custody records and source allocations while source-position stock is debited. It is not a second withdrawable location balance. CLOSE performs no second debit. Original events/postings and load allocations remain immutable; correction/receipt reversal of container-loaded stock is refused without a defined compensation workflow.

## Assessment, locking and restrictions

The caller owns a READ COMMITTED AppDbContext connection/transaction. It acquires stock advisory lock `714928352`, then the existing company lock, before row changes. Role/company is rechecked after waiting; the protected command holds a shared actor-row lock through the mutation so a role change cannot authorize a later write. Source/stock/reservation/load/closure routines do not commit the caller transaction.

F12 intervals, F14 conservative direct-stock assessment, applicable MRL, confirmed quality and company assessment policy are reused. The full pinned approved selection is assessed; only its exact lot/location allocations may be loaded. Physically distinct lots are not chemically averaged. Current dependency fingerprint must equal the reviewed proposal's fingerprint; changed evidence, policy or quality requires review instead of a silent replacement approval. A partial execution that subsequently loses freshness stays blocked until the owner supplies an authorized revalidation path.

Policy/MRL/rule/lab/quality publishers use the same stock writer boundary. Current MRL/policy effectivity is also checked at commit because time can cross a scheduled publication boundary while calculations run. Assessment records include phase, calculation version, dependency revision, decision time, legal/policy versions, bounds/rules/provenance and quality observations. A finite residue interval alone is not PASS.

Readiness is a current advisory read, not a transferable token. Missing required inputs remain INCOMPLETE. Common blockers include `ACTIVE_APPROVAL_REQUIRED`, `BLEND_OUTPUT_HANDOFF_REQUIRED`, `POSITION_OR_SOURCE_HANDOFF_REQUIRED`, `APPROVAL_EVIDENCE_CHANGED`, `CONTAINER_HOLD`, `LOT_RESTRICTED`, `SEAL_METADATA_MISSING` and `EXACT_TARGET_NOT_LOADED`. The mutation always checks again and returns 409 with blockers/domain reason when refused.

Container checks include direct restrictions and positive source/physical ancestry HOLDs, including ordinary processing paths where the generic mixed-bulk HOLD helper alone is insufficient. No descendant HOLD is created automatically. Manual container HOLD is append-only, independently blocks load/CLOSE, and does not release through lab confirmation, tracing, acknowledgment or closure. The UI shows current HOLD alerts; a separate notification inbox/acknowledgment workflow is not claimed by this release.

## Trace and dossier semantics

Container trace uses only immutable committed load allocations. Missing actual loading or unavailable original Capture provenance returns explicit findings and `complete:false`; it never invents plots. Percentages are rounded display values; kg strings remain the accounting authority. The container trace reads manifest/source facts in one snapshot, writes only TRACEBACK audit and rechecks current trace authority before returning.

Existing forward lot/lab trace now includes `actualContainers` and `containerCoverage: ACTUAL_LOADING`. Targets are found from the complete reachable material lot set, separately from UI paging. A source-root entry additionally requires a positive actual allocation from that source, excluding structural zero branches. `linkedLoadedKg` is the amount loaded from reachable lots, **not necessarily the mass attributable to the starting source**. Container backward trace provides the actual source-attributed kg. A linked target reports container HOLD and effective loaded-lot restriction separately.

Lot graph/current stock and actual targets share a repeatable-read snapshot; a fresh runtime connection rechecks authorization before returning. If the optional container-target routine is not installed, existing lot trace explicitly reports `containerCoverage: NOT_INSTALLED` rather than claiming source-to-container completeness. Oversized target sets fail without a partial success result. No full-season ≤30-second performance claim follows from this implementation.

Closure stores the full manifest across every accepted load, exact source kg/display shares, physical identifiers, original approval, current assessment/legal/evidence references, actor/time and audit. Later HOLDs do not rewrite it. ExportStaff's JSON is a permission-redacted view of the stored snapshot; CM can read the full source/evidence snapshot. PDF/Excel/legal certification, supplements, unload/reopen/reseal and shipment dispatch are not implicitly provided.

## Deployment

Forward AppDbContext migration `20261113000100_ContainerExecution` embeds `Data/Sql/ContainerExecution.sql`. Explicit owner runtime-role configuration grants only the new public command/read/target routines. Actual tables, complement balances, allocation/restriction helpers and triggers remain private. No baseline migration is edited, old stock/history is converted or normal startup grant repair is introduced.

Missing feature migration returns 503 at its endpoints. If installed, the finite entry grants must satisfy runtime validation. Shared Supabase deployment, cutover/migration/seed/grants and write smoke require separate explicit approval. This work verifies only disposable PostgreSQL; no shared deployment is asserted.
