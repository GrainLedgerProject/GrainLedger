"""Focused method-level sequences; names are proposed GrainLedger design contracts."""
from pathlib import Path
import textwrap
D=Path(__file__).resolve().parents[1]/'diagrams'
seqs={
'06_sequence_sync':r'''
sequenceDiagram
 autonumber
 actor P as Customer Procurement Officer
 participant UI as :Field UI
 participant Local as :LocalCaptureStore
 participant Ctrl as :SynchronizationController
 participant S as :SyncService
 participant R as :ProcurementRepository
 participant DB as :Database
 P->>UI: Capture receipt offline
 UI->>Local: savePending(stableKey, originalPayload, metadata)
 Local-->>UI: Pending operation retained
 Note over UI,Local: Local capture does not imply server acceptance
 UI->>+Ctrl: synchronize(stableKey, originalPayload)
 Ctrl->>+S: submit(request, authenticatedActor)
 S->>S: authorizeAndCanonicalize(request)
 S->>+R: beginAndLockSubmissionKey(company, device, operationId)
 R->>DB: BEGIN, serialize key and read previous outcome
 DB-->>R: Previous submission or absent
 R-->>-S: Submission snapshot
 alt Existing key, identical canonical hash
 S->>R: finishReadOnlyTransaction()
 R->>DB: End without new business posting
 S->>S: result = retainedOutcome
 else Existing key, changed canonical hash
 S->>R: appendConflict(originalKey, newImmutablePayload)
 R->>DB: Persist conflict outcome, COMMIT
 S->>S: result = CONFLICT
 else New key
 S->>S: validateConnectedReceiptContract(request)
 alt Receipt valid
 S->>R: appendReceiptLotsPostingsAndAppliedSubmission(request)
 R->>DB: Receipt, paddy lots, postings, event, projections and outcome
 R->>DB: COMMIT one atomic acceptance
 S->>S: result = ACCEPTED(appliedEventId)
 else Receipt invalid
 S->>R: retainRejectedSubmissionWithoutBusinessPosting(request)
 R->>DB: Persist original payload and rejection only, COMMIT
 S->>S: result = REJECTED(findings)
 end
 end
 S-->>-Ctrl: SyncOutcome
 Ctrl-->>-UI: Retained outcome or conflict details
 UI-->>P: Show acceptance / pending / rejection / conflict
''',
'07_sequence_post':r'''
sequenceDiagram
 autonumber
 actor O as Mill Operator
 participant UI as :Operations UI
 participant Ctrl as :OperationsController
 participant S as :LedgerService
 participant V as :LedgerValidator
 participant R as :LedgerRepository
 participant DB as :Database
 O->>UI: Record actual operation and optional approved plan
 UI->>+Ctrl: postOperation(command)
 Ctrl->>+S: commitOperation(command, actor)
 S->>+R: beginAndLockOperationScope(command)
 R->>DB: BEGIN, lock stock, reservations, authority and restrictions
 DB-->>R: Protected current snapshot
 R-->>-S: Snapshot
 S->>V: validateOwnerStageStockHoldAndBalance(command, snapshot)
 opt Nonzero signed variance within percent tolerance
 V->>V: requireManagerConfirmation(reason, retainedEvidence)
 end
 alt Any required validation fails
 S->>R: rollback()
 R->>DB: ROLLBACK all business changes
 S->>S: result = Rejected(findings)
 else Operation valid
 alt Unchanged-lot transfer
 S->>R: appendEqualLocationPostingsAndEvent(command)
 R->>DB: Equal withdrawal/deposit, same lot, no new lineage
 else Processing conversion
 opt Approved-plan execution
 S->>V: requireCurrentScopeAndExactInputReservations(plan)
 end
 S->>R: appendConversionPostingsAndSourceContributions(command)
 R->>DB: Inputs, new outputs, loss, signed variance and lineage
 opt Approved-plan output
 S->>R: appendInputConsumptionAndOutputEarmarking(plan)
 R->>DB: Matching reservation events and output allocations
 end
 end
 S->>R: commitWithProjections()
 R->>DB: COMMIT event, postings, lineage and reservation changes
 S->>S: result = Committed(eventId)
 end
 S-->>-Ctrl: CommitOutcome
 Ctrl-->>-UI: Balances or validation findings
 UI-->>O: Display operation outcome
 Note over S,DB: Any failure in the protected posting rolls back the whole operation
''',
'08_sequence_commit':r'''
sequenceDiagram
 autonumber
 actor O as Mill Operator
 participant UI as :Correction UI
 participant Ctrl as :CorrectionController
 participant S as :CorrectionService
 participant L as :LedgerValidator
 participant R as :CorrectionRepository
 participant DB as :Database
 O->>UI: Commit verified correction package
 UI->>+Ctrl: commitCorrection(requestId, replacement)
 Ctrl->>+S: commitPackage(command, actor)
 S->>+R: beginAndLockCorrectionScope(requestId)
 R->>DB: BEGIN, lock original postings, outputs, reservations and request
 DB-->>R: Reconciliation and dependent-use snapshot
 R-->>-S: Protected snapshot
 S->>S: requireReconciledEvidenceAndNoDownstreamUse(snapshot)
 alt Downstream use/loading or invalid reconciliation
 S->>R: rollbackPostingAndRetainInvestigationOutcome()
 R->>DB: Preserve restriction and investigation, no compensation
 S->>S: result = BlockedOrRejected(findings)
 else Eligible package
 S->>R: appendExactReversalsAndReverseInputConsume(original)
 R->>DB: Opposite original postings, unique REVERSE_CONSUME
 S->>L: validateReplacementAgainstReconciledStock(replacement)
 S->>S: requireCurrentPlanAuthorityOrStandaloneReplacement()
 alt Replacement, physical evidence or reservation quota invalid
 S->>R: rollbackEntirePackage()
 R->>DB: ROLLBACK reversals and all replacement changes
 S->>S: result = Rejected(findings)
 else All package invariants hold
 S->>R: appendReplacementAndReconcileReservations(replacement)
 R->>DB: New output lots/lineage, net consumption and unused releases
 S->>R: completeRequestAndCommitCorrelatedEvents()
 R->>DB: COMPLETED, projections and both event sets, COMMIT
 S->>S: result = Completed(compensationId, replacementId)
 end
 end
 S-->>-Ctrl: CorrectionOutcome
 Ctrl-->>-UI: Completed / blocked / rejected outcome
 UI-->>O: Display correction result
 Note over S,DB: No standalone compensation and no externally usable temporary restored stock
''',
'09_sequence_late':r'''
sequenceDiagram
 autonumber
 actor M as Customer Manager
 participant UI as :Compliance UI
 participant Ctrl as :EvidenceController
 participant E as :EvidenceService
 participant B as :BoundCalculator
 participant T as :TraceService
 participant R as :EvidenceRepository
 participant DB as :Database
 M->>UI: Register new or official superseding report
 UI->>+Ctrl: registerEvidence(command)
 Ctrl->>+E: registerReport(command, actor)
 E->>E: validateSourceAndOfficialSupersessionContext(command)
 E->>+R: beginAndLockEvidenceDecisionScope(command)
 R->>DB: BEGIN, serialize evidence and dependent authority
 DB-->>R: Current versions and decision revisions
 R-->>-E: Protected evidence snapshot
 E->>R: appendReportAndResultsPreservingHistory(command)
 R->>DB: New immutable report/results and registration event
 E->>B: deriveSupportedBoundVersions(actualSources)
 B-->>E: New bound versions or incomplete findings
 E->>T: forwardAffectedTargets(sourceLot)
 T-->>E: Affected stock, plans and containers
 E->>E: identifyInvalidUnfinishedAuthority(currentFindings)
 E->>R: appendBoundsRiskAndAuthorityChanges(affectedScope)
 R->>DB: New bounds, LAB_RISK_DETECTED, STALE authority, unused releases
 E->>R: commitEvidenceAndProtectiveChanges()
 R->>DB: COMMIT or rollback all on failure
 E-->>-Ctrl: EvidenceOutcome(affectedTargets, findings)
 Ctrl-->>-UI: Registered versions and risk findings
 UI-->>M: Review affected targets for explicit HOLD decision
 Note over E,DB: No automatic HOLD, no rewrite of prior decisions or CLOSED history
''',
'10_sequence_review':r'''
sequenceDiagram
 autonumber
 actor M as Customer Manager
 participant UI as :Plan Review UI
 participant Ctrl as :PlansController
 participant P as :PlanningService
 participant C as :ComplianceService
 participant R as :PlanningRepository
 participant DB as :Database
 M->>UI: Choose APPROVE or REJECT with review scope
 UI->>+Ctrl: reviewPlan(planId, decision, scope)
 Ctrl->>+P: review(command, authenticatedActor)
 P->>+R: beginAndLockPlanContainerAndAllocations(planId)
 R->>DB: BEGIN, lock active authority, stock and reservations
 DB-->>R: Remaining-input or actual-output snapshot
 R-->>-P: Protected review snapshot
 P->>P: requireCompanyGrantAndValidReviewScope()
 P->>C: assess(REVIEW, exactScope, currentBasis)
 C-->>P: Current verdict, versions and findings
 alt APPROVE requested and current verdict PASS
 P->>R: appendReviewAuthorityAndScopedReservations(command)
 R->>DB: New review, current authority, eligible INPUT/OUTPUT reservations
 R->>DB: Release superseded unfinished authority, COMMIT
 P->>P: result = Approved(scope)
 else REJECT requested or approval cannot pass
 P->>R: retainAssessmentAndActualDecisionWithoutNewAuthority()
 R->>DB: Assessment/findings, REJECT review only when selected, COMMIT
 P->>P: result = RejectedOrApprovalRefused(findings)
 end
 P-->>-Ctrl: ReviewOutcome
 Ctrl-->>-UI: Decision and scoped findings
 UI-->>M: Display retained review outcome
 Note over P,DB: OUTPUT_ONLY uses actual outputs, never reruns spent inputs
''',
'11_sequence_load':r'''
sequenceDiagram
 autonumber
 actor X as Customer Export Officer
 participant UI as :Container UI
 participant Ctrl as :ShipmentsController
 participant S as :ShipmentService
 participant C as :ComplianceService
 participant R as :ShipmentRepository
 participant DB as :Database
 X->>UI: Select approved output stock and loading mass
 UI->>+Ctrl: loadContainer(command)
 Ctrl->>+S: load(command, authenticatedActor)
 S->>+R: beginAndLockLoadingScope(command)
 R->>DB: BEGIN, lock container/order, plan authority, output stock/reservations
 DB-->>R: Protected current loading snapshot
 R-->>-S: Snapshot
 S->>S: requireOpenContainerAndMatchingPlanOutputReservation()
 S->>C: assess(LOADING, planId, loadingId, actualScope)
 C-->>S: Current verdict, basis and findings
 alt PASS and all owner/stock/authority/restriction checks hold
 S->>R: appendLoadingWithdrawalAndReservationConsumption(command)
 R->>DB: Committed loading, negative posting and exact OUTPUT CONSUME
 S->>R: retainAssessmentAndCommitProjections()
 R->>DB: Assessment, event, stock/reservation projections, COMMIT
 S->>S: result = Committed(loadingId)
 else Non-PASS or invalid loading scope
 S->>R: rollbackLoading()
 R->>DB: ROLLBACK, no partial stock or reservation change
 S->>S: result = Refused(findings)
 end
 S-->>-Ctrl: LoadingOutcome
 Ctrl-->>-UI: Committed loading or refusal findings
 UI-->>X: Display result
''',
'11_sequence_close':r'''
sequenceDiagram
 autonumber
 actor X as Customer Export Officer
 participant UI as :Container UI
 participant Ctrl as :ShipmentsController
 participant S as :ShipmentService
 participant C as :ComplianceService
 participant R as :ShipmentRepository
 participant DB as :Database
 X->>UI: Close container
 UI->>+Ctrl: closeContainer(containerId)
 Ctrl->>+S: close(containerId, authenticatedActor)
 S->>+R: beginAndLockContainerOrderAndEvidence(containerId)
 R->>DB: BEGIN, lock container/order and evidence/eligibility revision
 DB-->>R: ALL committed loading across plan versions and old authorizations
 R-->>-S: Whole-container snapshot
 S->>S: requireExactTargetOrderCapAndNoRestriction(snapshot)
 S->>C: assess(CLOSE, containerId, planId=null, loadingId=null)
 C-->>S: Whole-container verdict and current immutable basis
 alt Non-PASS, wrong quantity or changed decision basis
 S->>R: rollbackCloseOrRetryWithFreshBasis()
 R->>DB: No CLOSED state or successful dossier committed
 S->>S: result = RefusedOrReassessmentRequired(findings)
 else Current PASS and all closure conditions hold
 S->>R: appendCloseAssessmentStateAndImmutableDossier(basis)
 R->>DB: One transaction: assessment + CLOSED + unique snapshot/dossier + event
 R->>DB: COMMIT
 S->>S: result = Closed(dossierId)
 end
 S-->>-Ctrl: CloseOutcome
 Ctrl-->>-UI: CLOSED with dossier or refusal findings
 UI-->>X: Display closure result
 Note over S,DB: Snapshot/hash may be persisted in the same transaction as the supplied DB model
''',
'12_sequence_hold':r'''
sequenceDiagram
 autonumber
 actor M as Customer Manager
 actor MM as Mill Manager
 participant UI as :HOLD UI
 participant Ctrl as :HoldsController
 participant S as :HoldService
 participant R as :HoldRepository
 participant DB as :Database
 M->>UI: Issue HOLD on one owned lot or container
 UI->>+Ctrl: issueHold(command)
 Ctrl->>+S: issue(command, authenticatedActor)
 S->>+R: beginAndLockTargetAuthorityAndRecipientScope(command)
 R->>DB: BEGIN, lock target, unfinished authority and responsible assignments
 DB-->>R: Target owner and scoped recipients
 R-->>-S: Protected HOLD snapshot
 S->>S: requireExactOneTargetAndAllResponsibleRecipients()
 alt Invalid target/grant or required recipients missing
 S->>R: rollback()
 R->>DB: ROLLBACK, no false successful notice result
 S->>S: result = RejectedOrConfigurationError
 else Valid HOLD and recipients
 S->>R: appendHoldNoticesAndAuthorityRestrictions(command)
 R->>DB: HOLD + unique recipient notices + event + unused reservation releases
 R->>DB: COMMIT
 S->>S: result = Issued(holdId)
 end
 S-->>-Ctrl: HoldOutcome
 Ctrl-->>-UI: HOLD outcome and retained notices
 UI-->>M: Display result
 MM->>UI: ACKNOWLEDGE / ISOLATE / RECORD_RESPONSE
 UI->>Ctrl: recordHandling(command)
 Ctrl->>S: recordHandling(command, actor)
 opt Authorized isolation
 S->>S: Invoke ledger contract for equal location transfer
 end
 S->>R: appendHandlingWithEvidence(command)
 R->>DB: Retain handling event atomically with any isolation postings
 S-->>Ctrl: Handling recorded, HOLD remains active
 Ctrl-->>UI: Handling outcome
 Note over S,DB: Reading notices or handling a HOLD never releases it
'''
}
for name,body in seqs.items():(D/(name+'.mmd')).write_text(textwrap.dedent(body).strip()+'\n')
print('Refined',len(seqs),'method-level sequences.')
