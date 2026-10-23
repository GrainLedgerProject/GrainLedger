# Laboratory workflow guide

This guide covers ordinary evidence handling from procurement sampling through report confirmation. GrainLedger records samples, custody events, original laboratory reports, transcribed results, and verified links to source lots. Laboratory testing and processing happen outside the system. GrainLedger does not decide legal compliance or automatically change stock or HOLDs.

For exact HTTP contracts, permissions, payloads, and limits, see [sample and laboratory API](sample-api.md). For optional synthetic test data only, see [synthetic report generator guide](synthetic-lab-report-guide.md).

## Names that must not be confused

| Name | Meaning |
| --- | --- |
| Procurement source lot | Purchase-side lot the physical sample represents. Report confirmation links results to this source lot. Not a Ledger stock lot ID. |
| Sample code | GrainLedger code identifying the registered physical sample; write or print it on the bag. |
| Seal code | Physical identifier on the sample seal. Record actual value; it is not the sample code. |
| Laboratory reference | Laboratory-assigned sample reference, if provided. It is not a GrainLedger ID. |
| Printed report number | Laboratory's report identifier. Copy it as printed; it is not a GrainLedger ID. |

Use readable codes and selectors in normal work. Do not copy raw UUIDs into the workflow.

## Purchasing Staff: sample and custody

1. Open **Paddy intake → receipt → source-lot code**. Use the committed procurement source lot represented by the physical sample. Sample can be recorded before mill arrival; usable stock is not required.
2. Choose **+ Take sample**. Record material sampled, collection time, and actual physical seal code. Save to open sample details, then write/print generated sample code on the sample bag. One sample represents one source lot only.
3. Use **Send to laboratory** after physical handover to record actual dispatch. Enter only known parties, time, laboratory and references, seal condition, or supporting evidence. Dispatch records that staff recorded sending; it does not prove laboratory receipt or processing.
4. **Record laboratory receipt** is optional; use it only after laboratory confirms physical receipt. This is a separate acknowledgement, not inferred from dispatch. Generic **+ Record handover** remains for other actual custody transfers. Do not invent a handover merely because results arrived electronically.

Sample registration, handover, report intake, and confirmation do not deduct stock or create stock.

## Customer Manager: receive, review, confirm

1. Open **Samples**, choose registered sample, then **+ Receive report**. Sample may be preselected as editable convenience; verify it against report and custody history.
2. Enter laboratory name, printed report number, issue date, and actual report receipt time. Pick report kind. Keep collection, issue, and receipt times distinct. Lab and sample preselection never replaces checking the original.
3. **Optional report details** is collapsed by default; use it for lab conclusion or stated standard. These remain the laboratory's assertions, not GrainLedger assessment.
4. Upload the complete original report. Review the entire file before confirming company-only evidence. Every part of the file must belong to this company. Split/redact mixed-company material before intake; never attach another company's evidence.
5. Enter each sample-specific section. Copy printed sample code, seal code, and laboratory reference exactly when present; do not invent missing identity. Set a shared unit/method only if the original applies it to that section. Each result needs a substance/test, **Reported result** and unit: type plain numeric text, `<threshold`, or `ND` without entering the same numeric value twice. **Additional lab details** retains LOD, LOQ, reported uncertainty, row-specific method/reference and explicit interpretation overrides for unusual notation. For a large panel, use **Import CSV / paste TSV → Preview rows → Add previewed rows**; import belongs only to that section. Existing imported values remain authoritative over section defaults. Check all values against the original, then choose **Save report evidence**.
6. Saved report opens for review. Use **Download original** to compare the recorded rows with the uploaded file, and inspect the sample's custody history. Saving alone does not confirm a sample-to-lot association.
7. Choose **Review and confirm**. Select the verified registered sample for every section and the identity basis. Use `SAMPLE_CODE` only when the printed sample code matches; `SEAL_CODE` requires a matching printed seal. All printed sample/seal identifiers must agree: one match cannot override another mismatch. `LAB_REFERENCE` requires matching persisted laboratory handover details and supporting evidence; a freely typed laboratory reference is not enough.
8. After checking identity and transcription against the original, check both attestations and choose **Confirm identity and bind compatible results**. Do not attest to a check you have not performed. The same action records eligible residue bindings under adopted policy; unresolved rows return explicit reasons and remain unbound without undoing a valid identity confirmation.
9. Reopen the original source lot through **Back to source lot** or **Paddy intake → receipt → source-lot code**. Confirmed results appear under **Reports for source lot**. Pending or unresolved reports remain accessible from their sample/report records but are not confirmed source-lot evidence.

If identity is wrong, missing or contradictory, keep the report unresolved. Do not guess from farmer, plot, similar codes or an unverified lab reference. Retain the original and obtain supporting evidence or a linked corrected report.

### Edit or delete before confirmation

Open an unconfirmed report and choose **Edit draft** to correct report details, sample sections or transcribed results. Existing values are prefilled. Keep the current original file, or choose a replacement and check the whole-file company-only attestation again. Saving creates a new draft version and opens it for review; earlier original bytes and transcription remain retained for audit. This does not correct the laboratory document itself: request a linked amendment/retest when the external evidence is wrong.

Choose **Delete draft**, review the warning, then confirm deletion to remove an unconfirmed report from the active queue. Original evidence and the deletion audit record remain retained. A report referenced by another report cannot be deleted. Cancel leaves it unchanged.

Confirmed reports have neither action and cannot be reopened. Another user's edit, deletion or confirmation makes an older draft action stale: reload the report instead of resubmitting different data under an uncertain operation ID.

### What the review status means

| Status | Meaning | Next action |
| --- | --- | --- |
| `UNRESOLVED` | At least one section has missing or mismatching proposed identity. | Investigate identity; do not guess a lot or claim confirmation. |
| `PENDING_REVIEW` | Proposed identities match printed sample/seal identifiers, but a manager has not confirmed the record. | Compare original, custody evidence and transcription; explicitly confirm if valid. |
| `CONFIRMED` | A manager recorded verified sample associations and transcription checks. | Review evidence from the source lot; append later changes as new reports. |
| `SUPERSEDED` | Earlier unconfirmed draft replaced by a newer draft version. | Open the replacement; earlier evidence is read-only. |
| `DELETED` | Unconfirmed draft removed from the active queue; original retained for audit. | No confirmation, editing or repeat deletion. |

`NOT_ASSESSED` is a separate assessment status, not a review failure or a safety decision.

### Relationship reason and later evidence

Independent `FINAL` or `PARTIAL` report can remain unlinked; linking is optional through **Link to an earlier report**. Choose earlier report from **Related report** selector. Choices show report number, laboratory, issue date, and kind; search loaded reports or choose **Load more reports**. A selected report stays visible and selected even when search text does not match it. `SUPPLEMENTARY`, `AMENDMENT`, and `RETEST` require a related report and a **Relationship reason**. No UUID copying is needed. Examples:

- `Laboratory corrected the unit printed for row 4.`
- `Remaining panel results supplement the earlier partial report.`
- `Separate retest requested after conflicting measurements.`
- `Final report completes the earlier partial report.`

For later evidence, open the earlier report and choose **Create linked supplementary**, **Create linked amendment**, or **Create linked retest**. Earlier report is preselected. Laboratory and an unambiguous registered-sample context may be suggested, but remain editable. Printed identifiers, results and attestations are not copied; enter and verify the new document. Saving opens the new report for its own review.

Reason explains the relationship; it does not resolve conflicts or release HOLD. Original files, rows, and confirmed associations are immutable. Corrections, supplements, and retests are new linked evidence. Never silently prefer latest/lowest result or rewrite earlier interpretation.

## Export Staff: read evidence

Export Staff can read company-scoped laboratory evidence when granted `lab.read`; this does not grant report intake, editing, deletion or confirmation. Customer Manager needs persisted `lab.write` to receive, revise/delete an active unconfirmed report, or confirm it. Purchasing Staff's sample/custody permissions do not grant report writes. Company scoping applies to sample, report, association, metadata, and original download.

## Interpreting recorded results

Preserve laboratory wording and units. `ND` means not detected under reported method/limits; it is not zero. `<0.01` is not a measured value equal to `0.01`. An omitted substance is not evidence it was tested or passed. Do not map unknown substances or invent conversions, limits, or legal conclusions. A laboratory's own conclusion and stated standard remain its assertions, not GrainLedger assessment.

Report confirmation checks transcription and sample identity, not PASS. A confirmed report may show the existing numeric VN baseline when published rows exist; that comparison is separate from residue intervals and is not a legal certificate. No automatic HOLD or release follows report intake, confirmation, amendment, or retest. Existing stock and HOLD decisions remain unchanged.

## Mixed-bulk source evidence boundary

Pooling does not change sample or report subject: samples remain associated with original procurement source lot. Procurement and Ledger views may link that source to current pool/descendant stock through source-attributed lineage; this is evidence navigation, not a pool-specific laboratory test. A late confirmed report remains source identity `NOT_ASSESSED` and does not adjust pool shares, stock availability or HOLD. Positive custody/processing ancestry propagates explicit HOLDs only to descendants containing that source; a later top-up does not taint an earlier aliquot. Isolation movement preserves HOLD and does not release it. No automatic lab-triggered HOLD or assessment exists.

## Calculated lot residue bounds

Laboratory facts do not automatically become guaranteed intervals. Administrator publishes reviewed interpretation/process profiles; Customer Manager explicitly adopts applicable profiles. Identity/transcription confirmation automatically binds uniquely compatible rows using that adopted scientific context/review. Unnormalized reported uncertainty, missing ND limits, unavailable source stock, incompatible methods and conflicting reports remain explicit exceptions. Open **Resolve exceptions or bind after policy adoption** only for those cases. Missing/conflicting inputs remain `UNRESOLVED`; latest/lowest report never wins by default.

For untested sources, **Laboratory reports → Residue fallback and report evidence** supports explicit own-company source-group membership and externally supported fallback without requiring a report. Record source IDs, material/time scope, interval, reviewed precision, evidence/reviewer and explicit direct/fallback precedence; shared farmer/plot alone does not establish representation.

Open **Ledger → Stock → lot detail → Lot residue bounds**. Current bounds load automatically without creating evaluation history. **Refresh live preview** recomputes from current evidence/policy; **Lock snapshot for audit** writes an immutable historical evaluation for selected substances. Results show intervals or unresolved reasons, capture time and policy/evidence/physical-path references. History remains explicitly historical after later reports/rules. Assigned mill staff may inspect derived bounds without access to private laboratory originals. Residue bounds change no stock/HOLDs and issue no market PASS. Technical API, deployment and policy boundaries: [residue bounds contracts](residue-bounds-plan.md#15-substance-matrix-single-action-report-confirmation-and-live-bounds).

## Maintaining residue rules and evidence

Routine setup starts with published profiles, not a blank scientific form. Under **Residue profiles**, select applicable system profiles, inspect their summaries and publish adoption with one shared effective period and review. Each profile setting remains a separate immutable version. Selecting a company override requires an already reviewed same-Code company rule; missing or inapplicable intended overrides never silently fall back.

For a new scientific profile, open expert setup and choose **Interpret laboratory results** or **Define processing factors**. Full scientific applicability, values, unit, precision, evidence, effective dates and actual reviewer remain required. Codes/UUIDs/version links are generated. Existing-profile revisions and company clones retain applicability in a read-only summary; percentage/factor changes stay prominent, while advanced applicability remains editable. A single factor can expand to a reviewed lower/upper range. Review full values before publication; fresh scientific review is required for changes.

Published history remains readable. Revise only eligible latest versions; immutable predecessors and earlier evaluations stay unchanged. A concurrent-publication `409` retains the draft and requires explicit review of refreshed history, not automatic retry/rebase. Stages, processes and conditions retain existing scientific gates; suggestions are not approvals and `UNCONDITIONAL` is never assumed.

For confirmed-report exceptions, backend matches adopted profiles and previews exact bounds without publishing. Confirm reviewed exception bindings explicitly; policy scientific reviewer and current binding confirmer remain distinct. Named report/result choices support conflict resolution without copying UUIDs. Supported `µg/kg`/`μg/kg`/`ug/kg` values, LOD, LOQ and reviewed selection uncertainty/threshold normalize exactly to `mg/kg`; enter selection amounts in the original row's unit. Raw evidence stays unchanged; bare `ppb` remains unresolved because its mass-fraction meaning is not represented explicitly. Source-group fallback stays separate; shared farmer/plot alone never establishes it.

## Fictional example

All values below are fictional workflow examples, not legal limits or compliance guidance.

- Procurement source lot: `RAW-20261020-000002`
- Sample code on bag: `SMP-261020-0001`
- Physical seal: `SEAL-001`
- Laboratory reference: `LAB-S001`
- Printed report number: `R-EXAMPLE-1042`
- Laboratory: `Example Analytical Lab`
- Issue date: `2026-10-19`; manager receives report: `2026-10-20 09:00`
- Result: test label `Compound X`, original text `<0.010`, kind `BELOW_LIMIT`, numeric threshold `0.010`, unit `mg/kg`, LOQ `0.010`

These values remain distinct: source lot is purchase identity, sample code identifies bag record, seal identifies physical closure, laboratory reference identifies the lab's sample, and report number identifies document. The threshold is preserved as printed; it is not an assessment limit.
