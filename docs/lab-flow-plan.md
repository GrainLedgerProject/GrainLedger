# External laboratory workflow plan

Status: sample collection/dispatch, immutable laboratory report intake/results, confirmation, linked corrections/supplements/retests, and the working web UI are implemented. Confirmed reports can show a server-computed VN numeric baseline comparison against published Catalog limits. This is not the full decision-time legal assessment described in section 5; approved applicability, interpretation, and coverage rules remain necessary. No automatic HOLD or stock changes are authorized.

Implementation contract: [Samples and laboratory API](sample-api.md#laboratory-report-routes). Capture migration `20261020000000_ExternalLaboratoryWorkflow` installs company-scoped report evidence and append-only confirmation guards. This is not evidence that the migration is installed on a shared database.

Verification used isolated PostgreSQL and the actual cookie-authenticated API/browser surface: pre-arrival collection with zero stock, separate dispatch/acknowledgement, original-file upload/download, exact CSV/TSV preview and row validation, confirmation, source-lot results, supplementary history, Export Staff reads, and cross-company/Purchasing Staff report denial. Desktop and mobile report review were inspected. Clearing either required result text field was also exercised: submission shows validation without uploading a report. Focused sample/lab regressions passed 16 tests; the web suite passed 98 tests. Full backend run passed 295 of 297; unrelated existing administrator-demotion race and stale processing-output-code test expectations failed.

Confirmation UI now shows printed-versus-registered identity checks and submission errors inside the dialog. Leading/trailing whitespace is ignored during identity comparison without rewriting legacy evidence. Browser smoke verification used live read-only report/sample data and an intercepted confirmation rejection; no report was submitted to shared Supabase. Mobile inspection at 390 px confirmed dialog scrolling, visible rejection feedback, and no whole-page horizontal overflow. Backend success/immutability regressions use isolated PostgreSQL.

Capture migration `20261020120000_TrimLegacyLabIdentityComparisons` updates the database confirmation guard to use the same whitespace comparison as the API. Without this upgrade, padded legacy evidence can still fail when its confirmed association is inserted, even after restarting the updated API. Shared-database application requires explicit approval.

## 1. Purpose and scope

GrainLedger records physical samples, dispatch to an external laboratory, received reports, and results linked to the sampled procurement lot. Testing, laboratory instruments, and laboratory processing remain outside GrainLedger.

Proposal references: [GrainLedger V2](GrainLedger_V2.pdf), pages 8–9: F11 records samples and results; F12 calculates residue bounds; F18/F19 cover tracing and HOLDs. These are separate responsibilities, not one global pass/fail flag.

### Confirmed business decisions

1. Sampling covers procurement lots only. Sampling dried, milled, blended, or other processing-output lots is outside this plan.
2. One physical sample represents one procurement lot. Composite samples spanning multiple lots are not allowed.
3. The Customer Manager receives and registers laboratory results and is responsible for HOLD decisions. Automatic HOLD triggers and release rules are not established by this role decision.
4. Recording a sample does not deduct sample mass from stock.

### Recommended assessment scope

Use Vietnam as the default assessment market for the initial workflow, subject to verified applicable limits and approved interpretation rules. This is an application default, not a claim that testing in Vietnam must use only Vietnamese MRLs.

Keep laboratory measurements independent of market assessments. Export screens and export operations remain outside the current implementation scope. Future destination assessments must not require changing or overwriting stored results.

## 2. Existing foundations

- Capture already records company-scoped procurement samples and append-only handovers. See [sample API](sample-api.md).
- A sample references `capture.source_lots` through `sourceLotId`, not a Ledger lot selected when results arrive.
- Procurement registration creates stable source/lot identifiers while initial usable stock remains zero. Sample source validation checks committed Capture procurement and its business event; it does not require mill arrival or usable Ledger stock. See [procurement API](procurement-api.md).
- The working web app provides sample custody, laboratory report intake/review, CSV/TSV preview, and lot-linked confirmed results. The nested `apps/web/grainledger` demo is not the working app and is not an implementation target.
- Catalogs already provides active substances, measurement units, and versioned market/commodity MRL limits. MRL lookup does not itself assess a sample or lot. See [MRL API](mrl-api.md).
- Ledger provides immutable evidence-storage conventions, scoped access, HOLDs, and processing lineage. Capture stores laboratory originals under explicit company scope without a facility requirement; Ledger facility evidence remains separate. See [architecture](architecture.md).

These statements describe repository code and contracts, not verification of deployed database migrations or runtime availability.

## 3. Workflow

### 3.1 Register a sample

Purchasing Staff records:

- Generated sample code and physical seal code.
- Procurement source-lot reference and its customer company.
- Collection time and material sampled.
- Collector, derived from the authenticated user.

Write or print the sample code on the physical sample bag. Bind the sample to its procurement source at collection, and preserve that relationship when receiving and processing occur later.

Reject cross-company references and samples spanning several lots. Do not infer representation of other purchases, seasons, or lots from a shared farmer or plot.

Registration creates neither usable stock nor a stock deduction.

### 3.2 Record dispatch

Reuse the sample handover history. Record:

- Receiving laboratory name/reference.
- Dispatch or handover time and parties.
- Seal condition and supporting evidence when available.
- Laboratory sample reference when provided.

A dispatch record establishes that staff recorded sending the sample. It does not prove that the laboratory received it. Record laboratory acknowledgement separately when evidence is available; do not invent laboratory processing status.

The Customer Manager must be able to review the sample identity and handover history needed to register results, under explicit company-scoped authorization.

### 3.3 Receive a report

The Customer Manager uploads the original report and records:

- Laboratory, report number, and report issue date.
- Receipt time, distinct from report issue and sample collection times.
- Laboratory sample reference and any stated sample/seal identifiers.
- Whether the report is partial, final, supplementary, or an amendment, as supported by the report.

Match the report's sample identity to the registered physical sample before confirming its lot association. If identifiers disagree or the match cannot be established, retain the report as unresolved evidence. Do not guess the lot from a farmer name or a similar lot code.

One report file may describe several samples. Each report section/result set must map to its own registered sample; the system must not apply every result in that file to every referenced lot. A report containing another company's data must not expose that data through a shared attachment.

Receiving a report does not establish that every requested test is complete.

### 3.4 Enter and verify results

Preserve the original report, then enter repeatable result rows. For large panels, accept the application's documented CSV template or pasted table data with preview and row validation before confirmation.

The Customer Manager checks sample identity, substance names, units, values, qualifiers, and reporting limits against the original report.

Confirmation means the record and transcription were checked. It is not a declaration that the lot is safe or compliant with every market.

### 3.5 Correct or supplement a report

Confirmed reports, original files, result rows, and confirmed sample associations are immutable.

- A correction appends a linked amendment or replacement with a reason and authenticated actor.
- A supplement adds evidence without silently replacing unrelated earlier rows.
- A retest is separate evidence, not an overwrite.
- Preserve the original report, prior interpretations, and their timestamps.
- Do not silently prefer the latest or lowest result when evidence conflicts. Assessment selection/conflict rules require explicit policy.
- An amended report does not rewrite historical assessments or release a HOLD.

## 4. Flexible result representation

No fixed external report template is required. Store two layers: immutable original report evidence and structured rows for machine-readable interpretation.

### Report metadata

Keep owner, sample associations, laboratory, report identifier, issue/receipt times, original evidence reference, confirmation actor/time, and amendment/supplement relationships.

A lot can have multiple samples. A sample can have multiple reports. A report can contain multiple sample-specific result sets, with authorization enforced for both metadata and original file access.

### Result rows

Use one row per reported test result, not one database column per substance.

| Field | Purpose |
| --- | --- |
| Substance/test label as printed | Preserve laboratory wording. |
| Catalog substance reference, when resolved | Connect recognized results to the existing active-substance catalog. |
| Original result text | Preserve values such as `0.023`, `<0.01`, or `ND` exactly. |
| Result kind/qualifier | Distinguish numeric values, below-limit values, not detected, and qualitative findings. |
| Numeric value or reporting threshold, when supplied | Store exact decimals; do not use floating-point arithmetic for comparisons. |
| Original unit | Preserve the reported unit without silently guessing a conversion. |
| LOD and LOQ, when supplied | Preserve detection and quantification limits separately. |
| Method and uncertainty, when supplied | Preserve information needed to judge whether evidence supports an assessment. |
| Report page/section/row reference | Support verification against the original document. |

Unrecognized substances and unsupported result types remain stored as raw evidence. Unresolved mappings, incompatible units, or missing interpretation rules prevent calculation; they must not cause data loss or guessed compliance.

Important distinctions:

- `ND` does not mean zero.
- `<0.01` is not a measurement equal to `0.01`.
- An omitted substance is not evidence that it was tested and passed.
- A panel name such as “multi-residue” does not prove coverage of every required substance. Preserve the actual tested-substance list when available.
- Combined residue definitions must not be mapped to a single ingredient without a verified rule.
- Qualitative findings can be recorded but cannot be numerically compared to an MRL without an applicable interpretation rule.

Hundreds of substances require more rows, not schema changes. The internal CSV format is a GrainLedger input convention, not a requirement imposed on external laboratories.

## 5. Measurements and market assessments

### 5.1 Separate facts from judgments

A laboratory measurement describes the tested sample. An assessment compares relevant evidence against rules for a particular market and commodity.

Laboratory location does not determine the only applicable MRL standard. When requesting testing, the intended market, if known, should be communicated to the laboratory because substance coverage, methods, residue definitions, and reporting limits may affect whether the report can support the intended assessment.

If the laboratory includes its own compliance conclusion, preserve that conclusion and its stated standard/version separately. Do not treat an unqualified “pass” on a report as universal compliance.

### 5.2 Vietnam-default assessment

For the initial application workflow, recommend Vietnam as the default market. Require verified applicable legal sources and published Catalog MRL data for the correct commodity; never manufacture limits or treat a missing catalog entry as unrestricted.

An assessment retains:

- Sample and report/result references used.
- Market and commodity.
- Chosen MRL version and supporting snapshot.
- Assessment time and authenticated reviewer/actor.
- Per-substance findings and unresolved/missing evidence.
- Applied interpretation rules and any separately approved safety margin.

Use labels such as:

- **Exceeds limit:** evidence establishes an exceedance under the applicable comparison rules.
- **Within assessed limits:** assessed substances meet the applicable rules; this is not an unrestricted “safe” label.
- **Insufficient evidence:** coverage, mappings, reporting limits, MRL data, or interpretation policy are insufficient.

A comparison finding is not automatically a legal non-compliance decision. Uncertainty and enforcement rules must not be invented. Full market-compliance claims require an approved completeness/coverage rule as well as valid per-substance comparisons.

### 5.3 Example

The following numbers are fictional and illustrate market-specific comparison only. They are not legal limits or an enforcement decision.

Assume substance X was measured at `0.03 mg/kg` in the same material and on the same analytical basis:

| Assessment market | Hypothetical limit | Comparison |
| --- | ---: | --- |
| Vietnam | 0.05 mg/kg | Below limit. |
| Destination A | 0.01 mg/kg | Exceeds limit. |

Therefore, do not store a global “good/bad laboratory result.” Store the measurement once and retain distinct market assessments.

### 5.4 Exporting later

When export functionality exists, create a destination-market assessment using existing evidence only where that evidence is suitable. Preserve the Vietnam assessment unchanged.

Verify:

- Required substances were tested.
- Reporting limits are low enough for the destination limits.
- Methods, units, residue definitions, and commodity basis are compatible.
- Applicable legal version/date and any transition rules are established.
- Any buyer-specific requirements are represented separately from statutory MRLs.

For example, `<0.05 mg/kg` cannot establish compliance with a `0.01 mg/kg` limit. More sensitive testing may be required.

A procurement-paddy measurement does not directly certify finished milled or blended rice. That requires approved processing/residue propagation rules or suitable finished-product evidence. Finished-product sampling is outside this plan and must not be implied by implementing procurement sample intake.

Export operations, automatic blend qualification, and finished-product certification are not part of this implementation.

## 6. Lot display, tracing, and HOLDs

Show sample collection, dispatch history, original reports, structured results, unresolved issues, confirmation, and amendment history from the sampled procurement lot.

Keep sample binding stable even if results arrive after the lot has been dried, milled, blended, or exhausted. Any downstream impact follows Ledger lineage. Do not copy the source's measured concentration unchanged to descendants or overwrite original stock/history.

Direct measurements and inherited/calculated evidence must be visibly distinct.

The Customer Manager owns result review and HOLD decisions within persisted permissions and company scope. Staff must not override comparison findings by manually marking a result “passed.”

This plan does not authorize automatic HOLD placement, unrestricted release, or changes to existing HOLD semantics. Existing HOLDs must not be released by report confirmation, a passing comparison, a retest, or a report amendment.

## 7. Implementation sequence

1. Extend Capture's existing procurement sample/handover workflow with structured laboratory dispatch and sample-reference information. Keep company-scoped source identity and current idempotency conventions.
2. Add company-scoped immutable report evidence and sample-specific result rows, including unresolved intake, confirmation, supplements, and amendments. Reuse storage conventions without fabricating facility scope or exposing raw bytes through unscoped references.
3. Replace lab placeholders with sample details, dispatch history, original report upload/view, result entry/import preview, confirmation, and lot-linked results. Authorize Purchasing Staff collection/handover, Customer Manager report intake/review, and Export Staff lab reads; explicitly permit only the supporting sample reads each role requires.
4. Add Vietnam-default market assessment only when legal data and interpretation/coverage rules are approved. Reuse Catalog MRL version resolution and preserve decision evidence. Until then, intake remains available but compliance assessment is explicitly insufficient/not assessed.
5. Exercise the complete sample-to-lot path with isolated PostgreSQL and the actual API/browser surface. Do not migrate, seed, or mutate the shared Supabase database for verification without explicit approval.

Steps 1–3 and isolated verification in step 5 are implemented. Step 4's full decision-time legal assessment remains gated. The existing read-time VN numeric baseline is described in the API guide; it is not a compliance decision. [F12 derived lot intervals](f12-residue-bounds-plan.md#9-delivered-f12-contracts) are implemented separately, with reviewed interpretation/evidence/process inputs and explicit `UNRESOLVED` outcomes when those are missing. F12 does not create market assessments, stock changes or HOLD release.
The role-by-role [laboratory workflow guide](lab-flow-guide.md) describes the implemented user path; this plan remains the policy/scope record.

Prefer extending existing module features and in-process contracts. Do not add a laboratory integration service or new abstraction layer solely for this workflow. Do not pass EF entities across module boundaries.

## 8. Acceptance checks

- A sample can be recorded against committed procurement before mill arrival; stock remains unchanged.
- One sample cannot represent multiple procurement lots, and cross-company associations are rejected.
- Dispatch and actual laboratory acknowledgement remain distinguishable.
- Wrong or missing report/sample identity stays unresolved rather than silently attaching to a lot.
- One report file containing multiple samples does not leak company data or apply results to the wrong sample.
- A report supports a large substance panel using rows, including numeric, `<limit`, `ND`, qualitative, and unknown-substance entries.
- Missing substances and unsuitable reporting limits produce insufficient evidence rather than PASS.
- Customer Manager can review required sample history and register/confirm results; unauthorized actors cannot.
- Original report bytes and confirmed records remain immutable; corrections and supplements preserve prior evidence.
- Repeated submissions follow existing replay/conflict conventions rather than duplicating confirmed records.
- Late reports remain linked to the original source lot after receiving or processing.
- Assessments retain market, commodity, MRL version, evidence, and decision time; a later MRL update does not rewrite history.
- The same suitable measurement can have different market-specific findings without changing its original value.
- Report intake, amendment, and assessment do not change stock or implicitly release HOLDs.

## 9. Outstanding policy prerequisites

The confirmed scope and responsible role do not settle these rules:

1. **HOLD triggers and scope:** whether pending, exceeded-limit, uncertain, or conflicting evidence requires a HOLD; whether action is manual or automatic; how affected descendant stock is handled.
2. **HOLD resolution:** authorized handling and acceptable evidence without bypassing existing Ledger restrictions. No implicit release path is authorized by this plan.
3. **Assessment interpretation:** handling `ND`, LOD/LOQ, uncertainty, combined residue definitions, conflicting reports/retests, and report-selection precedence.
4. **Assessment completeness:** required substance coverage for the commodity/market and permitted conclusions for partial reports.
5. **Applicable legal data:** verified Vietnam sources/amendments, commodity mapping, effective-date/transition rules, and any explicit defaults when a substance-specific MRL is absent.

Missing policy must fail closed for assessment or disposition, not prevent preservation of original laboratory evidence.

## 10. Deliberately excluded

- Laboratory testing, instruments, internal lab scheduling, and processing status.
- External laboratory accounts or direct laboratory API integration.
- OCR, automatic PDF parsing, or speculative lab-specific importers.
- Composite samples, processed-lot sampling, and sample-mass stock deductions.
- Export screens, shipment approval, blend optimization, or finished-product certification.

A recurring real report format can justify a dedicated importer later. No external format is required to implement evidence upload plus flexible result rows.

## References

- [GrainLedger V2 proposal](GrainLedger_V2.pdf), pages 8–9.
- [Sample API](sample-api.md).
- [Procurement API](procurement-api.md).
- [MRL API](mrl-api.md).
- [Architecture and authorization](architecture.md).
- [Setup and deployment safety](setup-and-deployment.md).
- [Vietnam Food Administration: Circular 50/2016/TT-BYT publication](https://vfa.gov.vn/van-ban/thong-tu-quy-dinh-gioi-han-toi-da-du-luong-thuoc-bao-ve-thuc-vat-trong-thuc-pham.html). Publication reference only; this document does not verify the full current legal framework or prescribe numeric limits.
- [European Commission: pesticide MRL questions and answers](https://food.ec.europa.eu/plants/pesticides/maximum-residue-levels/qas-pesticides_en). Confirms EU MRLs apply to imported as well as EU-produced food.
