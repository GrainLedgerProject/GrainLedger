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
5. Enter each sample-specific section. Copy printed sample code, seal code, and laboratory reference exactly when present; do not invent missing identity. For a large panel, choose **Import CSV / paste TSV**, upload through **Or choose CSV file** or paste TSV, then **Preview rows → Add previewed rows**. Each import belongs only to that section. Check result text, exact decimal strings, qualifiers, units and references against the original, then choose **Save report evidence**.
6. Saved report opens for review. Use **Download original** to compare the recorded rows with the uploaded file, and inspect the sample's custody history. Saving alone does not confirm a sample-to-lot association.
7. Choose **Review and confirm**. Select the verified registered sample for every section and the identity basis. Use `SAMPLE_CODE` only when the printed sample code matches; `SEAL_CODE` requires a matching printed seal. All printed sample/seal identifiers must agree: one match cannot override another mismatch. `LAB_REFERENCE` requires matching persisted laboratory handover details and supporting evidence; a freely typed laboratory reference is not enough.
8. After checking identity and transcription against the original, check both attestations and choose **Confirm checked record**. Do not attest to a check you have not performed.
9. Reopen the original source lot through **Back to source lot** or **Paddy intake → receipt → source-lot code**. Confirmed results appear under **Reports for source lot**. Pending or unresolved reports remain accessible from their sample/report records but are not confirmed source-lot evidence.

If identity is wrong, missing or contradictory, keep the report unresolved. Do not guess from farmer, plot, similar codes or an unverified lab reference. Retain the original and obtain supporting evidence or a linked corrected report.

### What the review status means

| Status | Meaning | Next action |
| --- | --- | --- |
| `UNRESOLVED` | At least one section has missing or mismatching proposed identity. | Investigate identity; do not guess a lot or claim confirmation. |
| `PENDING_REVIEW` | Proposed identities match printed sample/seal identifiers, but a manager has not confirmed the record. | Compare original, custody evidence and transcription; explicitly confirm if valid. |
| `CONFIRMED` | A manager recorded verified sample associations and transcription checks. | Review evidence from the source lot; append later changes as new reports. |

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

Export Staff can read company-scoped laboratory evidence when granted `lab.read`; this does not grant report intake or confirmation. Customer Manager needs persisted `lab.write` to receive/confirm reports. Purchasing Staff's sample/custody permissions do not grant report writes. Company scoping applies to sample, report, association, metadata, and original download.

## Interpreting recorded results

Preserve laboratory wording and units. `ND` means not detected under reported method/limits; it is not zero. `<0.01` is not a measured value equal to `0.01`. An omitted substance is not evidence it was tested or passed. Do not map unknown substances or invent conversions, limits, or legal conclusions. A laboratory's own conclusion and stated standard remain its assertions, not GrainLedger assessment.

Report confirmation checks transcription and sample identity. Assessment remains `NOT_ASSESSED`, not `PASS`. No automatic HOLD or release follows report intake, confirmation, amendment, or retest. Existing stock and HOLD decisions remain unchanged.

## Mixed-bulk source evidence boundary

Pooling does not change sample or report subject: samples remain associated with original procurement source lot. Procurement and Ledger views may link that source to current pool/descendant stock through source-attributed lineage; this is evidence navigation, not a pool-specific laboratory test. A late confirmed report remains source identity `NOT_ASSESSED` and does not adjust pool shares, stock availability or HOLD. Positive custody/processing ancestry propagates explicit HOLDs only to descendants containing that source; a later top-up does not taint an earlier aliquot. Isolation movement preserves HOLD and does not release it. No automatic lab-triggered HOLD or assessment exists.

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
