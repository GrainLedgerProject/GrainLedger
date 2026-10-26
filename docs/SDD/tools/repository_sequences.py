from pathlib import Path
import textwrap,json,re
R=Path(__file__).resolve().parents[1];D=R/'diagrams'
seqs={
'05_sequence_publish':r'''
sequenceDiagram
 autonumber
 actor A as Mill Administrator
 participant UI as :CatalogScreen
 participant E as :MrlVersionEndpoints
 participant C as :CatalogDbContext
 participant DB as :PostgreSQL
 A->>UI: Publish prepared MRL draft
 UI->>E: POST /api/mrl-versions/{id}/publish, If-Match, CSRF
 E->>E: Require platform.configure and current actor
 E->>C: BeginTransactionAsync, lock version and market/commodity pair
 C->>DB: BEGIN, advisory locks, load draft/limits/published intervals
 DB-->>E: Current MRL snapshot
 E->>E: Check ETag, source, mg/kg limits, effectivity and publication order
 alt Missing/stale ETag or invalid publication
 E-->>UI: 428 / 412 / 409 ProblemDetails, no publication
 else Valid successor
 E->>C: Close open predecessor interval when applicable, append correlated CatalogEvents
 E->>C: Mark draft PUBLISHED, SaveChangesAsync
 C->>DB: Save predecessor interval, new publication and events atomically
 E->>C: CommitAsync
 E-->>UI: Published version and ETag
 end
 UI-->>A: Show outcome
 Note over E,DB: Limits/source of published predecessor remain retained.<br/>Resolver is existing infrastructure, not a completed residue assessment.
''',
'06_sequence_receipt':r'''
sequenceDiagram
 autonumber
 actor P as PurchasingStaff
 participant UI as :LedgerReceiving
 participant E as :ProcurementEndpoints
 participant C as :CaptureDbContext
 participant Port as :IProcurementLedgerPort
 participant DB as :PostgreSQL
 P->>UI: Submit procurement source expectation
 UI->>E: POST /api/procurements, stable operation key and decimal strings
 E->>E: Authorize purchases.write, validate canonical source payload
 E->>C: Begin READ COMMITTED and acquire stock-write advisory lock
 C->>DB: Lock 714928352 before source/stock reads
 E->>C: Find previous CaptureOperation by company/device/operation
 alt Same actor, same key and accepted canonical hash
 C-->>E: Retained original registration result
 E-->>UI: Replay outcome, no duplicate source or stock
 else Key reused with changed content
 E-->>UI: 409 OPERATION_CONFLICT, no second source package
 else New operation and valid source scope
 E->>Port: IsValidReceivingOptionAsync(shared connection, transaction)
 Port->>DB: Validate receiving facility/location and company
 E->>C: Save CaptureBusinessEvent, Procurement, ProcurementItems, SourceLots, CaptureOperation
 E->>Port: RegisterPendingAsync(shared connection, transaction, actor, procurementId)
 Port->>DB: ledger.register_pending_procurement(actor, procurementId)
 E->>C: CommitAsync
 E-->>UI: 201 source result, PENDING_ARRIVAL, InitialStock=0
 end
 UI-->>P: Registration outcome and separate receiving state
 Note over C,DB: Source Status stays COMMITTED; source identity is not usable stock.<br/>Port never commits the caller transaction.
''',
'06_sequence_arrival':r'''
sequenceDiagram
 autonumber
 actor O as MillOperator
 actor M as MillManager
 participant UI as :LedgerReceiving
 participant E as :LedgerEndpoints
 participant S as :LedgerService
 participant DB as :PostgreSQL protected routines
 O->>UI: Record complete actual measurements for every procurement line
 UI->>E: POST /api/ledger/receiving/{id}/measurements
 E->>S: ReceivingAsync(measurements, request)
 S->>DB: ledger.arrival_command(actor, id, measurements, payload)
 DB->>DB: READ COMMITTED, global write lock, scope/version/idempotency checks
 alt Every actual normalized mass exactly equals expected mass
 DB->>DB: Append measurements and post actual lots/positions/contributions/events atomically
 DB-->>UI: RECEIVED_MATCHED, actual mass becomes usable
 else Any discrepancy
 DB->>DB: Append immutable complete observation set, no stock
 DB-->>UI: AWAITING_MANAGER_REVIEW, usable stock=0
 M->>UI: Review mismatch with reason and scoped evidence
 UI->>E: POST /api/ledger/receiving/{id}/reviews
 E->>S: ReceivingAsync(reviews, request)
 S->>DB: ledger.arrival_command(actor, id, reviews, payload)
 alt APPROVE_SHORTAGE, every actual <= expected and at least one short
 DB->>DB: Append review and post positive actuals only atomically
 DB-->>UI: RECEIVED_SHORTAGE_APPROVED
 else REQUEST_REMEASUREMENT
 DB->>DB: Retain old evidence, append request; next measurement set is new
 DB-->>UI: REMEASUREMENT_REQUESTED, no usable stock
 else KEEP_BLOCKED or unsupported surplus/invalid review
 DB-->>UI: BLOCKED_UNRESOLVED or rejection, no stock
 end
 end
 Note over S,DB: One procurement = one complete arrival; no partial-arrival, surplus approval or tolerance.<br/>Legacy NULL receiving marker and existing stock are preserved.
''',
'07_sequence_post':r'''
sequenceDiagram
 autonumber
 actor O as MillOperator
 actor M as MillManager
 participant UI as :LedgerOperations
 participant E as :LedgerEndpoints
 participant S as :LedgerService
 participant DB as :PostgreSQL protected routines
 O->>UI: Prepare physical operation draft
 UI->>E: POST /api/ledger/operations
 E->>S: CommandAsync(draft, payload)
 S->>DB: ledger.command(authenticatedActor, draft, payload)
 DB-->>UI: Draft ID and version, no stock posting
 opt Nonzero in-tolerance signed processing variance
 M->>E: POST /operations/{id}/confirm with reason/evidence
 E->>S: CommandAsync(confirm, payload)
 S->>DB: Retain scoped manager confirmation bound to current draft
 end
 O->>UI: Commit actual physical operation
 UI->>E: POST /api/ledger/operations/{id}/commit
 E->>S: CommandAsync(commit, payload)
 S->>DB: ledger.command(actor, commit, payload)
 DB->>DB: lock_writes at READ COMMITTED, commit_operation checks
 alt Missing current policy, owner/stage/HOLD/stock/variance failure
 DB-->>S: Protected SQL exception, transaction rolled back
 S-->>UI: Mapped 400 / 403 / 409 ProblemDetails
 else Valid physical conversion
 DB->>DB: Append postings/events and micro-kg source contributions, update positions atomically
 DB-->>UI: Committed operation and actual balances
 end
 Note over E,DB: Existing physical blending is not export approval.<br/>Planned approved-plan execution must add matching INPUT/OUTPUT reservation checks inside this boundary.
''',
'08_sequence_reconcile':r'''
sequenceDiagram
 autonumber
 actor O as MillOperator
 actor M as MillManager
 participant UI as :LedgerOperations
 participant E as :LedgerEndpoints
 participant S as :LedgerService
 participant DB as :PostgreSQL protected routines
 O->>UI: Request correction of committed operation
 UI->>E: POST /api/ledger/corrections
 E->>S: CommandAsync(correction_request, payload)
 S->>DB: Retain correction and block original outputs
 M->>UI: Review physical positions and complete replacement content
 UI->>E: POST /api/ledger/corrections/{id}/reconcile
 E->>S: CommandAsync(correction_reconcile, payload)
 S->>DB: Lock writes, validate assigned manager/evidence/current positions
 alt Original confirmed correct
 M->>E: POST /corrections/{id}/cancel with verified evidence
 E->>S: CommandAsync(correction_cancel, payload)
 S->>DB: Retain verified cancellation, remove correction restriction only
 else Eligible physical correction
 DB->>DB: Bind reconciliation to positions and exact replacement version/content
 DB-->>UI: Reconciled correction; original history retained
 else Downstream consumption or loading prevents correction
 DB-->>UI: Reject correction posting, retain restriction/investigation
 end
 Note over S,DB: Reconciliation does not edit committed stock or release a HOLD.<br/>Changing the replacement invalidates prior reconciliation.
''',
'08_sequence_commit':r'''
sequenceDiagram
 autonumber
 actor O as MillOperator
 participant UI as :LedgerOperations
 participant E as :LedgerEndpoints
 participant S as :LedgerService
 participant DB as :PostgreSQL protected routines
 O->>UI: Commit current reconciled correction
 UI->>E: POST /api/ledger/corrections/{id}/commit
 E->>S: CommandAsync(correction_commit, payload)
 S->>DB: ledger.command(actor, correction_commit, payload)
 DB->>DB: lock_writes, validate scope, original outputs and reconciled replacement digest/version
 alt Evidence, remaining positions or replacement binding invalid
 DB-->>S: Reject and rollback complete correction
 S-->>UI: 400 / 403 / 409 ProblemDetails
 else Reconciliation and replacement still valid
 DB->>DB: Reverse eligible original postings once and commit replacement in same transaction
 DB->>DB: Update positions, retain correction events and source attribution
 DB-->>UI: Committed reviewed replacement, original history preserved
 end
 Note over S,DB: Export reservation accounting is a planned extension.<br/>Future REVERSE_CONSUME and replacement CONSUME must join this transaction, never a separate commit.
''',
'12_sequence_hold':r'''
sequenceDiagram
 autonumber
 actor C as CustomerManager
 actor M as MillManager
 participant UI as :LedgerStock
 participant E as :LedgerEndpoints
 participant S as :LedgerService
 participant DB as :PostgreSQL protected routines
 C->>UI: Issue explicit lot HOLD
 UI->>E: POST /api/ledger/holds
 E->>S: CommandAsync(hold, payload)
 S->>DB: ledger.command(actor, hold, payload)
 DB->>DB: Validate owner, lot, scope and retain HOLD plus scoped notices atomically
 DB-->>UI: Active lot HOLD
 M->>UI: Read notices or record physical response
 UI->>E: POST /notifications/{id}/read or /holds/{id}/respond
 E->>S: CommandAsync(notification_read or hold_response, payload)
 S->>DB: Validate persisted grant/facility and retain action
 DB-->>UI: Notice read or response retained, HOLD still active
 Note over E,DB: Current lot HOLD does not automatically cascade to descendants.<br/>Container HOLD, complete target traversal and planned authority/reservation effects are target extensions.
''',
'13_sequence_history':r'''
sequenceDiagram
 autonumber
 actor U as Authorized internal user
 participant UI as :LedgerStock / Workspace
 participant E as :LedgerEndpoints
 participant S as :LedgerService
 participant DB as :PostgreSQL
 U->>UI: Search permitted business history
 UI->>E: GET /api/ledger/history with allowed filters/page
 E->>S: ReadAsync(history, query)
 S->>DB: ledger.read(actor, history, payload)
 DB->>DB: Validate persisted active role and owner/facility scope
 DB->>DB: Filter, order and page before scoped detail projection
 DB-->>S: Authorized history page with role-based redaction
 S-->>UI: JSON page or ProblemDetails
 UI-->>U: Show retained event detail and history
 Note over S,DB: Identity/Catalog/Capture keep their own audit records.<br/>Unified export/compliance history is planned, not an existing combined endpoint.
'''
}
for name,src in seqs.items():(D/(name+'.mmd')).write_text(textwrap.dedent(src).strip()+'\n')
# General offline target must preserve registration/arrival separation.
p=D/'06_sequence_sync.mmd';s=p.read_text().replace(':SynchronizationController',':SynchronizationEndpoints (planned)').replace(':SyncService',':SyncCoordinator (planned)').replace(':ProcurementRepository',':Capture persistence port (planned)').replace('appendReceiptLotsPostingsAndAppliedSubmission','appendSourceExpectationAndAppliedSubmission').replace('Receipt, paddy lots, postings, event, projections and outcome','Procurement source expectation, source identities, event and pending receiving outcome').replace('COMMIT one atomic acceptance','COMMIT one atomic source registration').replace('ACCEPTED(appliedEventId)','APPLIED(sourceEventId), PENDING_ARRIVAL, zero usable stock');s+=' Note over UI,DB: Complete offline lifecycle is planned; source APPLIED never grants usable stock.\n';p.write_text(s)
# Add one sequence for the observed receiving boundary; preserve UC identities.
p=R/'GrainLedger_SDD.md';s=p.read_text();needle='### 3.3 '
pos=s.index(needle)
s=s[:pos]+'''#### 3.2.4 Measure and review complete physical arrival

![Measure and review complete physical arrival](diagrams/06_sequence_arrival.svg)

Diagram 0: Measure and review complete physical arrival Sequence Diagram

'''+s[pos:]
count=0
def ren(m):
 global count
 count+=1;return 'Diagram '+str(count)+':'
s=re.sub(r'^Diagram \d+:',ren,s,flags=re.M)
s=s.replace('CaptureOperation |','CaptureOperation |')
p.write_text(s)
m=json.loads((R/'design_manifest.json').read_text());m['features'][1]['seqs'].append(['06_sequence_arrival','Measure and review complete physical arrival']);m['diagrams'].append('06_sequence_arrival');m['diagram_count']=count;(R/'design_manifest.json').write_text(json.dumps(m,indent=2))
print('Updated',len(seqs),'repository-informed sequence sources; total diagram count',count)
