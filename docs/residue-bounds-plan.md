# Residue bounds: readiness assessment and implementation plan

Status: residue bounds and section 12 policy extension implemented locally on 2026-10-23. Base implementation and earlier verification are documented in sections 9–11; extension implementation and isolated verification are documented in sections 12–13. No shared Supabase deployment, backfill, scientific approval, commit, or push. Sections 2–8 retain the pre-implementation assessment and original design rationale.

Planning history, 2026-10-23: the original section 12 recorded an agreed direction and was planning-only at that time. Its design rationale and acceptance scenarios remain below; implementation status is now recorded separately from verification and from deployment/scientific approval.

## 1. Verdict

**GrainLedger now implements scoped, reproducible lot residue calculations. It still lacks approved operational scientific policy/data sufficient to establish finite bounds for every lot. Missing evidence, interpretation or applicable process rules produce explicit per-substance `UNRESOLVED` results, with null unknown endpoints—not guessed numbers or PASS.**

The existing procurement/laboratory/physical-ledger foundation is retained. Catalog now owns reviewed rule versions; Capture owns explicit source-group fallback and report-selection evidence; Ledger reads physical ancestry and appends immutable calculation snapshots. These engineering capabilities do not approve laboratory methods, representativeness, coefficients, or export compliance.

Policy ownership is distinct from calculation ownership: the company, supported by qualified review, supplies or adopts the assumptions; GrainLedger validates applicability and computes the interval. Company responsibility does not authorize changing laboratory facts or treating unsupported settings as scientifically valid. Section 12 records the implemented reusable default/override workflow and its remaining gates.

Do not confuse these three outputs:

- **F11 fact:** what a laboratory reported about a particular procurement sample.
- **Residue calculation:** a substance-specific residue interval for a target lot, with its evidence, transformation path, and unresolved dependencies.
- **Market assessment:** whether sufficient suitable evidence establishes compliance for a particular commodity, market, legal version, date, and safety margin. This consumes residue bounds; it is not the same result.

Confidence is high for the source-code findings below. Deployed schema state, production data completeness, scientific validity of reference coefficients, and current legal applicability were not verified.

## 2. Proposal requirements

Page numbers refer to the 12-page [proposal PDF](GrainLedger_V2.pdf), not a separate SRS or prototype UI.

| Requirement | Proposal evidence | Implementation implication |
| --- | --- | --- |
| Calculate lot residue bounds through inheritance and processing. | Page 8, F12. | A report-level comparison alone is insufficient. |
| Use mass-weighted residue calculations for mixtures. | Page 2, rationale and blending discussion. | Use the mass actually committed as input, not original purchase expectations or remaining stock. |
| Propagate origin and residue after a valid processing step. | Page 4, paragraph beginning “Sau khi đạt”. | Follow committed transformations; drafts do not constitute physical processing. |
| Milling changes residue by output fraction. | Page 5, note below the example; page 6, journey example. | Head rice, broken rice, bran, and husk cannot share an assumed universal factor. |
| Untested lots inherit conservative bounds from a source group; insufficient evidence must not become “pass”. | Page 9, business rule 6. | Source-group definition and evidence-backed fallback bounds are required, not an invented default. |
| A bound is an interval; compliance uses its upper endpoint. | Page 10, glossary. | Preserve bound semantics; do not relabel a measured concentration as a statistical upper bound. |
| Blend qualification uses upper bound against destination MRL minus safety margin, with the applicable version retained. | Page 9, business rule 7. | Keep the future assessment boundary explicit; MRL is not an estimate of actual residue. |
| Results can arrive after processing; history must explain what was known at a decision. | Pages 1 and 3. | Recompute current results when evidence changes without rewriting earlier calculation records. |

The proposal does **not** define a 95% confidence level, source-group membership, a finite pessimistic fallback, numeric processing coefficients, drying adjustment, ND/LOD/LOQ interpretation, uncertainty treatment, or conflicting-report precedence. These cannot be silently filled in by implementation.

## 3. Current implementation and gaps

### 3.1 Evidence and identity

| Capability | Current evidence | Readiness / gap |
| --- | --- | --- |
| Stable procurement source identity | [Procurement.cs:16–35](../backend/src/GrainLedger.Modules.Capture/Procurement.cs#L16-L35) links procurement item, plot-season, company, Capture source ID, and Ledger lot ID. | Reusable. Capture source ID and Ledger lot ID are different identifiers; resolve their mapping explicitly. |
| Sample-to-source binding | [Sample.cs:3–13](../backend/src/GrainLedger.Modules.Capture/Sample.cs#L3-L13). | Reusable, procurement-only. No approved composite or finished-product sampling is established by F11. |
| Structured laboratory evidence | [LabReport.cs:3–80](../backend/src/GrainLedger.Modules.Capture/LabReport.cs#L3-L80) stores original report, linked-report relationships, exact result text, substance mapping, qualifier, numeric text, unit, LOD, LOQ, method, and uncertainty. | Raw facts exist. Method/uncertainty are text, not an approved machine-readable interpretation rule. Missing fields remain possible. |
| Confirmed identity and transcription | [LabReportEndpoints.cs:250–277](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs#L250-L277) verifies sample/seal/laboratory-reference evidence and appends confirmed associations. Response source binding is in [lines 369–378](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs#L369-L378). | Reuse only confirmed associations as authoritative evidence. Intake proposals alone are not confirmed bindings. |
| Source-group fallback | Current source/sample models above contain no residue-group membership or approved fallback-bound records. [Lab workflow policy:54–56](lab-flow-plan.md#L54-L56) prohibits inferring sample representation from a shared farmer or plot. | Missing. Farmer, plot, season, spray declaration, or common ownership alone does not authorize residue inheritance between unrelated lots. |
| Late and amended evidence | [LabReportTests.cs:369–403](../backend/tests/GrainLedger.Api.IntegrationTests/LabReportTests.cs#L369-L403) covers source binding after drying, linked reports, and unchanged stock/HOLDs. | Useful existing regression, inspected but not run here. It does not test descendant residue calculation or decide which conflicting report wins. |

### 3.2 Existing VN comparison is not a lot residue calculation

`ResponsesAsync` selects the highest published VN version number per substance and commodity, then calls `AssessVn`: [LabReportEndpoints.cs:342–368](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs#L342-L368).

The existing routine handles numeric, below-limit, and ND comparisons, including a LOQ-before-LOD fallback: [lines 388–488](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs#L388-L488). It does not compute lot intervals, traverse descendants, apply processing factors, or select conflicting report evidence.

Important limits of reuse:

- The query does not select by effective interval or one resolved sample commodity; a substance can be compared against several commodity rows.
- Result units are checked, but the comparison branches do not validate the fetched limit unit.
- The comparison is recomputed when reading a report, not persisted as an immutable decision-time assessment.
- Existing ND behavior is implementation behavior, not evidence that residue rules have been scientifically approved.

[Architecture documentation:65–66](architecture.md#L65-L66) still says all reports remain `NOT_ASSESSED`; this is stale relative to the endpoint code. [Lab-flow-plan.md:3](lab-flow-plan.md#L3) already distinguishes the numeric VN baseline from full assessment. Preserve that distinction in residue-bounds documentation and UI; do not expand residue bounds into an unrelated rewrite of legal assessment.

### 3.3 Processing and lineage

Lot stage, product identity, originating operation, operation time/content/status, signed postings, reversal references, and root contributions are persisted in [Schema.sql:58–119](../backend/src/GrainLedger.Modules.Ledger/Sql/Schema.sql#L58-L119).

Actual mixed-bulk-aware processing records an immutable event containing operation content and resolved inputs, debits those inputs, and creates output lots: [MixedBulkCommands.sql:302–319](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkCommands.sql#L302-L319). This is useful path evidence for residue calculations; the current code subsequently allocates **mass**, not chemical concentration.

Mixed storage is not represented only by `lots.operation_id`:

- Ingress creates a new pool-generation lot, preserving incoming and previous-pool edges: [MixedBulkStorage.sql:458–480](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkStorage.sql#L458-L480).
- Withdrawal creates an aliquot and, when nonempty, a separate remainder, both linked to the old generation: [lines 560–613](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkStorage.sql#L560-L613).
- Custody events, edges, and processing allocations retain intermediate identifiers and are immutable: [lines 39–105](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkStorage.sql#L39-L105).

**Precision boundary:** processing source shares use largest-remainder integer micro-kg allocation. Zero-rounded roots can remain in ordinary contributions, while custody-processing allocation rows retain only positive quantities: [MixedBulkCommands.sql:321–355](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkCommands.sql#L321-L355). Withdrawal source rows can omit zero-rounded shares: [MixedBulkStorage.sql:585–612](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkStorage.sql#L585-L612). Existing [tiny-lineage coverage](../backend/tests/GrainLedger.Api.IntegrationTests/LedgerAcceptanceTests.cs#L222-L236) demonstrates the intended accounting quantization.

Therefore:

1. Rounded root shares are not exact chemical exposure weights. A rounded zero must not erase a positive parent dependency or justify zero residue.
2. Evaluate through committed operation inputs and custody lot-to-lot edges, retaining each intermediate concentration calculation. Do not flatten all roots and then apply one final factor.
3. Include exhausted ancestors. Current positions determine remaining stock, not which evidence affected historical material.
4. Resolve actual mixed-bulk input lots, including aliquots and remainders; draft-selected pool IDs are not sufficient.
5. Preserve correction/reversal semantics rather than treating compensation as another blend input. Current correction handling rejects downstream consumption: [CanonicalCatalog.sql:600–605](../backend/src/GrainLedger.Modules.Ledger/Sql/CanonicalCatalog.sql#L600-L605). Do not weaken that guard for residue calculations. Historical evaluation of an original corrected lot retains its original physical path; current evaluation of replacement material uses the replacement lot. If legacy/inconsistent history contains a descendant of an output later corrected, preserve evidenced physical ancestry and mark unresolved applicability where necessary; never silently substitute replacement ancestry.

### 3.4 Factors and frontend

The bundled [substance CSV:1–15](../tools/catalog-reference-data/pesticide_substances-v1.1.csv#L1-L15) already contains four fraction-specific factor columns plus `pf_basis` and `pf_source`. This is useful research material, **not** an approved production factor catalog:

- Some entries are explicitly calibration-only.
- Bases include paddy-to-fraction, husked-to-polished, and brown-rice transformations.
- The imidacloprid note distinguishes residue definitions with different factors.
- [MRL import documentation:70–73](mrl-file-import.md#L70-L73) confirms factors are not imported into operational API fields.

Do not multiply a paddy-to-finished-rice cumulative factor at every intermediate step, apply a husked-rice factor to paddy, or add a yield adjustment on top of a coefficient that already represents concentration change.

The working app is `apps/web/src`, not the nested `apps/web/grainledger` prototype. It already has functional stock and processing screens:

- [LedgerWorkspace.tsx:3–6,31–32](../apps/web/src/LedgerWorkspace.tsx#L3-L32) mounts the real components.
- [LedgerStock.tsx:15–40,63–69](../apps/web/src/LedgerStock.tsx#L15-L69) fetches scoped stock/lot detail and displays positions, source composition, and history, but no residue bounds.
- [LedgerOperations.tsx:52–91](../apps/web/src/LedgerOperations.tsx#L52-L91) loads real operation/correction data and sends commands. Replacing this screen is unnecessary.
- [LabReports.tsx:391–407](../apps/web/src/LabReports.tsx#L391-L407) displays the server's report-level comparison, not descendant residue bounds.

## 4. Policy decisions required before numeric release

These are missing business/scientific decisions, not reasons to redesign the application. Record approved answers with evidence, approving authority, version, applicability, and effective period.

| Decision group | Required answer | Safe behavior until approved |
| --- | --- | --- |
| Evidence → interval | Numeric result versus true upper bound; uncertainty interpretation; `<threshold`, ND, LOD/LOQ, unit conversion, analytical material/moisture basis, and combined residue definitions. | Preserve raw facts; return an explicit unresolved reason rather than an invented interval. Never ND = 0. |
| Source group and pessimistic fallback | Explicit company-scoped membership, valid material/time boundaries, representative evidence, fallback interval, and precedence versus direct evidence. A maximum observed sample is not automatically an upper bound for all untested lots. | Untested substance remains unresolved when no approved finite bound exists. Never use MRL, zero, or an arbitrary large number as concentration. |
| Transformation and mixing | Applicable factors by substance/residue definition, input/output basis, stage/fraction and process conditions; scalar versus conservative interval factor; drying, loss, storage, pure custody split, and bagging behavior; treatment of tiny shares and directed rounding. | Missing or incompatible rule propagates unresolved status. No default factor of 1 for drying or milling; no automatic residue removal with mass loss. |
| Evidence lifecycle | Which confirmed amendments, supplements, partial/final reports and retests apply; conflict handling; effective-time selection versus evidence known at calculation time; approval roles for rules and source-group evidence. | Conflicts remain explicit. Do not silently choose latest, lowest, average, or tightest evidence. Earlier stored calculations remain unchanged. |

Market coverage, legal-version selection, and Manager safety margins must also be approved **before a consumer issues a compliance decision**. They are not prerequisites for storing an honest unresolved residue result, and are not default inputs to its physical concentration calculation.

### Recommended policy baseline

Use the following direction for implementation. These are proposed project rules, not scientific approval of missing coefficients, laboratory methods, uncertainty conventions or source-group evidence. Implement configurable, versioned rules rather than hardcoded scientific defaults. Unsupported cases must produce an explicit per-substance **UNRESOLVED** outcome with null unknown endpoints; they must never produce guessed concentration or PASS.

| Area | Recommended rule |
| --- | --- |
| Numeric measurements | Derive intervals from reported values and applicable, reviewed uncertainty under the approved interpretation rule. Preserve the original uncertainty text and record the normalized interpretation separately. Missing or unsupported uncertainty remains unresolved unless an evidence-backed interpretation convention explicitly covers it. Do not silently treat `[value, value]` as scientifically guaranteed bounds. |
| Below-limit and ND results | For a compatible method/material basis whose approved interpretation supports it, represent `<t` conservatively as `[0, t]`. ND requires an explicit reporting threshold or validated LOD/LOQ interpretation; do not introduce a universal LOQ-before-LOD fallback merely because the existing VN comparator uses one. Missing threshold or interpretation remains unresolved. |
| Untested source lots | Require explicit company-scoped membership, material/time applicability, and an evidence-backed fallback interval. No automatic farmer/plot grouping and no fallback derived solely from maximum observed result or MRL. Record the approved precedence between direct evidence and fallback. |
| Processing factors | Publish reviewed coefficients or coefficient intervals per substance/residue definition, input basis, output fraction and applicable process conditions. Distinguish cumulative from step-specific factors. Missing factor remains unresolved; bundled calibration values are not operational approvals, and drying/milling never receive an implicit factor of 1. |
| Mixing, splitting and precision | Use homogeneous mass-weighted interval propagation only where that material model is approved. Preserve positive parent dependencies through custody splits and accounting rounding. Round lower endpoints downward and upper endpoints upward; record the selected precision and rule version. |
| Reports and conflicts | Supplements may add coverage without silently overwriting existing evidence. Amendments supersede evidence only through an explicit reviewed relationship and applicable selection rule. Retests do not automatically replace older reports. Conflicting applicable results remain unresolved until reviewed under the approved policy; never choose the lowest result by default. |
| Permissions | Proposed responsibility split: Administrator publishes reviewed rule versions; Customer Manager maintains own-company source-group/fallback evidence and reviewed report selection. Domain review supplies scientific justification; application role alone does not establish validity. Facility staff receive only explicitly authorized derived bounds, not automatic access to laboratory originals. |

Approval means a documented project/domain decision supported by evidence, not a government approval or an extra permission prompt for routine coding. The project/business owner agrees workflow and visibility; an appropriate factory/domain expert or supervisor reviews scientific assumptions and sources. The immediate prerequisite for operational numeric results is identifying authoritative sources for laboratory interpretation and processing factors, then reviewing their actual applicability. A citation or publication label alone does not establish a conservative upper bound.

Branch creation, code changes and isolated verification can proceed when requested; missing domain inputs gate the dependent numeric behavior, not all policy-independent engineering. Shared Supabase migrations, seeds or other data changes still require separate explicit database-owner permission.

Use clearly labelled synthetic rules and coefficients for isolated tests or demonstrations. They demonstrate calculation behavior, not scientific validity or export compliance. Keep their provenance distinguishable and never auto-publish them as operational policy. No separate runtime simulation mode or duplicate calculator is required.

## 5. Smallest complete design

Everything in this section is **proposed**, not existing behavior or an approved scientific policy.

### Scope

**In:** per-substance intervals and unresolved reasons; confirmed procurement evidence; approved source-group fallback; committed drying/milling/merge/blend/bagging and custody paths; late evidence and corrections; exact conservative arithmetic; scoped API/UI; reproducible historical evaluations.

**Out:** new sampling categories, instrument integration, statistical confidence claims, automatic HOLD/release, changes to stock quantities or accounting allocation, blend optimization/approval, export orders/loading, legal certification, background jobs, response caching, new services or dependencies.

### Module ownership and database changes

Keep the existing ownership model in [architecture.md:7–11](architecture.md#L7-L11):

- **Catalog:** add an approved, versioned residue-rule set for evidence interpretation and process/substance/output-basis factors. Retain evidence references, applicability, publication actor/time, and effective intervals. Reuse catalog publication conventions; do not overload MRL limits or mass-tolerance policies as residue coefficients.
- **Capture:** add company-scoped source-group membership and fallback-evidence records, only to the extent required by the approved grouping policy. Preserve existing laboratory originals; expose bulk confirmed evidence and source-ID mappings through a module contract, not EF entities. Reuse company-scoped evidence conventions for fallback support rather than requiring an unrelated mill-facility upload.
- **Ledger:** add a focused calculator and protected bulk ancestry reader over existing operations/events/custody. Add append-only evaluation snapshots containing lot/company, substance coverage, interval results, unresolved reasons, selected report/result and rule IDs, source-group version, lineage/event dependencies, engine version, calculation time and authenticated actor.

Store per-substance results as rows or an immutable structured payload consistent with existing Ledger event conventions, not one column per pesticide. An evaluation snapshot is sufficient; do not add a second mutable per-lot “truth” table or a duplicate lineage graph. Select concentration/factor precision after the rounding policy is approved; `numeric(20,6)` is the existing **mass** contract, not automatically the correct concentration precision.

Use forward migrations owned by each affected module. Extend protected commands/reads and runtime-role validation as needed; do not grant unrestricted direct writes or expose private custody tables. Preserve the existing advisory-lock and `READ COMMITTED` stock-write contract. Shared-database migration and operational policy publication require separate explicit approval; do not seed calibration coefficients as approved policy.

### Calculation and freshness

1. Authorize the target lot using persisted role, company and facility scope before loading its dependency closure. Use narrow module-owned bulk contracts; never fetch each ancestor/report through individual HTTP requests.
2. Capture confirmed evidence, applicable policies, actual operation inputs, custody edges and correction state within one **read-only `REPEATABLE READ` transaction**, sharing its connection/transaction across module readers. Materialize the complete authorized dependency set, then close the read transaction. Calculate from that immutable set and append its evaluation in a separate protected `READ COMMITTED` command that rechecks current actor/scope. Never call stock/source write routines inside the read-snapshot transaction; their existing isolation/lock contract stays unchanged. Store both dependency-snapshot time and persistence time. A concurrent commit may be absent from this snapshot, but cannot create a hybrid result; the response describes its captured view, not guaranteed stock eligibility at a later decision.
3. Traverse the lot dependency DAG in topological order and memoize within this request. For `S` requested substances, `V` lots and `E` parent edges, target calculation work is `O(S × (V + E))`; no cross-request cache is needed.
4. Resolve each source substance to direct evidence or an explicitly approved group fallback, retaining which one was used. Apply the approved qualifier, unit, material-basis and report-selection rules. Absence from a report is not a tested zero.
5. Propagate through each actual transformation. Preserve path differences when one root reaches the target by several processes. For a same-basis homogeneous mixture **only where approved policy permits**, the mathematical interval extension is:

   `L_mix = sum(m_i × L_i) / sum(m_i)`

   `U_mix = sum(m_i × U_i) / sum(m_i)`

   Here `m_i` is committed input mass, not the mass remaining today. For a nonnegative, applicable concentration-factor interval `[f_L, f_U]`, the corresponding output is `[L_mix × f_L, U_mix × f_U]`. These equations do not authorize homogeneity, a factor, a lower bound, or a material conversion absent approved evidence. Use path-specific transformations when bases differ.
6. Preserve concentration through an unchanged-lot move without changing its identity. For custody splits/merges and bagging, apply their approved material model; a newly generated custody UUID alone is not a chemical change. Do not divide by output yield and multiply by a concentration factor unless the approved factor definition explicitly requires both.
7. Use exact decimal/rational intermediates and outward rounding: lower endpoints never round upward and upper endpoints never downward. Retain positive parent dependencies even when accounting attribution rounds their root share to zero. Unsupported/overflowing arithmetic, incompatible basis, missing factors, uncertain historical ancestry or a missing finite parent upper bound cannot produce a finite “complete” result by dropping that parent.
8. Compute on demand from current dependencies. A later confirmation, amendment, group-rule publication, correction or pool generation is visible at the next evaluation without a propagation queue. Keep the old snapshot intact and distinguish it from a fresh evaluation. Do not filter ancestors by positive current position or silently relabel an old snapshot as current.

This design provides logical propagation through the committed graph, not eager writes to every descendant whenever a source report arrives. Existing stock commits remain stock commits; incomplete residue evidence does not invent a new stock/HOLD blocking policy.

### API and frontend

Proposed minimum surface:

- A lot-bound evaluation command that computes current bounds server-side and appends an immutable snapshot; use existing idempotency/conflict conventions. A replay returns the original snapshot; a new evaluation request is required for fresh evidence.
- A scoped historical-evaluation read. Historical snapshots remain explicitly historical; do not expose an unvalidated cached “latest” as current.
- Decimal-string lower/upper values, canonical concentration unit and analytical basis, per-substance completeness/reasons, direct/group/inherited provenance, factor/rule versions, and operation/custody dependencies. Unknown endpoints are null with explicit status/reasons, never numeric sentinel values. Do not return a global PASS flag.
- Existing lot permissions govern lot access; exposure of laboratory originals still requires Capture authorization. Define derived-bound visibility explicitly for facility staff rather than giving every stock viewer `lab.read`. Revalidate owner/facility/active role on both evaluation and historical reads. Derive actors server-side.

Extend the existing lot-detail panel in `LedgerStock.tsx`, with typed shared contracts and a small residue panel if extraction is warranted. Offer explicit **Calculate / Recalculate bounds** and historical evaluation inspection. Show computation time, substance, interval, unit/basis, evidence origin, applied process rules, and missing-policy/evidence reasons. Handle loading, errors, unresolved and complete results accessibly. Do not run concentration arithmetic in JavaScript or issue one request per substance/ancestor.

Add narrow rule-maintenance and source-group/fallback controls to existing Catalog/Capture workflows using the approved role matrix, not a separate admin application. Keep laboratory confirmation and VN numeric comparison visibly separate from calculated lot bounds. No replacement of the working processing screen or prototype migration is required.

A future blend/market consumer must request fresh bounds and retain the exact evaluation ID alongside its own applicable MRL version and safety margin. It must reject incomplete required-substance evidence. Implementing that consumer is outside this residue-bounds change.

## 6. Ordered implementation plan

| Step | Work and deliverable | Dependency / completion criterion |
| --- | --- | --- |
| 1 | Record the recommended policy baseline above; identify authoritative interpretation/factor sources; obtain domain review of the four policy groups, process-basis mappings, permission matrix, precision and worked examples. | Numeric release gate, not a blanket coding gate. Missing approvals/data remain explicit unresolved dependencies; do not invent values to complete this step. |
| 2 | Add module-owned versioned rule, source-group/fallback, and immutable evaluation storage plus protected access and bulk cross-module contracts. | Use the recommended baseline for policy-independent contracts. Approved precision, roles and applicability are required before enabling dependent numeric behavior or operational publication. Migrations on isolated infrastructure only during development. |
| 3 | Implement the authorized dependency reader for ordinary processing, actual mixed-bulk inputs, ingress, aliquot/remainder, exhausted ancestors and correction state. | Can be developed from existing history before coefficients are approved. Preserve all positive parent dependencies; report genuinely incomplete ancestry. |
| 4 | Implement one request-local interval calculator and provenance builder, covering source evidence/fallback, qualifiers, path-specific factors and outward rounding. | Steps 1–3. Missing evidence/rules produce unresolved rows, never guessed numbers. |
| 5 | Expose evaluation and historical-read contracts, atomic immutable snapshot persistence, idempotency and scope checks. | Steps 2–4. Fresh evaluation and replay/history have distinct semantics; concurrency cannot create a hybrid result. |
| 6 | Extend real lot detail and existing policy/source workflows; distinguish measured facts, inherited/calculated bounds, VN baseline and historical snapshots. | Step 5 and approved visibility rules. Working desktop/mobile surface, no floating-point accounting or report leakage. |
| 7 | Run the behavioral matrix below and real isolated API/browser smoke; update architecture/API/workflow docs together. | No numeric release until policy gates and end-to-end checks pass. Record deployment approval separately; no shared-data backfill or reset. |

## 7. Behavioral validation and acceptance

Use existing backend integration-test infrastructure with isolated PostgreSQL and real permission checks. Synthetic rule values are test inputs only, never production approvals. Extend relevant existing lab/ledger tests where they exercise the same behavior; add focused residue-calculation coverage.

| Scenario | Required observable result |
| --- | --- |
| Direct confirmed evidence | Approved interpretation yields the expected interval and exact result/policy references; pending or unresolved identity cannot masquerade as confirmed evidence. Missing applicable uncertainty/interpretation cannot silently become `[value, value]`. |
| ND, `<limit`, uncertainty and units | An approved compatible `<t` rule yields `[0, t]`. ND requires its approved reporting-threshold/LOD/LOQ rule, not an implicit comparator fallback. Missing policy or threshold, unsupported uncertainty/units, or incompatible analytical basis stays unresolved. ND never becomes zero by default. |
| Untested source and incomplete panel | Explicit approved group membership/fallback works only within company/material/time scope and recorded precedence. Missing substance or fallback remains unresolved; shared farmer/plot does not imply representation, and maximum observed result or MRL alone cannot establish fallback. |
| Weighted mixture | With synthetic same-basis intervals, 60 kg at `[0.1, 0.2]` and 40 kg at `[0.3, 0.8]` yield `[0.18, 0.44]` before display rounding. A missing parent upper bound cannot be omitted from the denominator. |
| Fraction-specific and cumulative factors | Different approved fraction rules produce different bounds. A synthetic factor `[0.5, 0.5]` applied to the preceding mixture yields `[0.09, 0.22]`; a cumulative paddy-to-fraction factor is not applied twice. Wrong input basis fails closed. |
| Drying, loss, bagging and custody | Approved rules are applied once. Water/declared mass loss alone does not authorize pesticide removal. A move does not recalculate concentration from a smaller remaining balance. |
| Pool ingress and partial withdrawal | Evaluate incoming + prior pool, actual processing aliquot and remaining pool. Both aliquot and remainder retain appropriate parent residue dependencies across later pool generations. |
| Tiny quantities and reconverging paths | Positive exposure survives accounting shares rounded to zero; upper bounds never round downward. The same root arriving by two different factor paths is evaluated path-wise without losing or double-counting physical input. |
| Late evidence and retests | A new evaluation of already-processed/exhausted descendants reflects newly applicable confirmed evidence. Original snapshots and report facts remain byte-for-byte unchanged. Supplements add coverage without silent replacement; amendments require reviewed supersession, retests do not automatically win, and unresolved conflicting results cannot be resolved by selecting the lowest value. |
| Corrections and concurrency | Preserve the existing rejection of direct correction after downstream consumption. Original corrected-lot history and replacement material retain different paths. A legacy/inconsistent descendant of a later-corrected output retains evidenced ancestry or becomes unresolved, never silently rebound to replacement lineage. Concurrent report confirmation, policy publication or pool change yields one coherent read snapshot; persistence records that captured view even when a newer commit already exists. |
| Authorization and replay | Cross-company IDs, unassigned facility actors, inactive users and forged actors cannot expose/create evaluations. Snapshot reads enforce current scope. Replay is stable and changed-payload reuse conflicts. |
| Policy publication and evidence visibility | Only authorized actors publish rule versions or manage company-scoped fallback/selection evidence. Missing approval/applicability prevents operational use. Synthetic calibration fixtures are never automatically published as operational policy. Derived-bound access does not expose unauthorized laboratory originals. |
| No side effects on business state | Evaluation does not change stock/postings, source observations, original lab data, HOLD state or legal MRL versions. |
| Actual API and browser | Create a synthetic source → drying → milling → blend/pool chain through real isolated APIs; submit approved synthetic policy/evidence; inspect lot bounds; append late evidence and recalculate; compare new result with retained historical snapshot. Check unresolved and denied states, desktop/mobile rendering and accessible error feedback. |

The proposal's 50%/30%/20% lineage example is useful for mass attribution but contains no residue measurements or approved factors. It cannot supply a numeric residue-bounds acceptance result by itself.

## 8. Evidence exercised in this assessment

- Extracted text from all 12 proposal pages and inspected their rendered images, including the fraction-factor note and conservative inheritance rule.
- Traced repository source and existing regression code. Existing test names/results cited above are not claims that those tests ran in this assessment.
- Ran `python3 -B tools/import_catalog_reference.py` in offline preview mode: 14 substances, 5 active numeric VN limit rows, 9 excluded `NO_MRL` rows, and two draft groups. This verifies bundled importer behavior only; it does not approve coefficients, establish current law, or inspect the deployed Catalog.
- Validated local source/document links and their cited line ranges.
- No authenticated application/API/browser smoke, application build, integration-test run, migration, policy publication or database write was performed. No running local application listener was available when checked. Operational readiness therefore remains unverified; implementation must run the isolated smoke above.

## 9. Delivered residue-bounds contracts

### Permissions and workflow

- `residue.policy.manage`: Administrator publishes immutable reviewed Catalog rule versions. Open **Administration → Catalogs → Commodities** or **Ledger → Setup → Catalog and basic facility setup**.
- `residue.evidence.manage`: Customer Manager publishes own-company source-group/fallback and reviewed report selections. **Laboratory reports** exposes fallback controls without requiring a laboratory report; confirmed-report detail also exposes explicit result selection.
- `residue.evaluate`: Customer Manager and Export Staff for own-company lots; Mill Manager and Mill Operator only within assigned lot facilities. Administrator is not a business wildcard. Every evaluation/history read rechecks persisted active role and current scope. Derived facility access grants no laboratory original download.
- **Ledger → Stock → lot detail** offers explicit Calculate/Recalculate, historical snapshot inspection, reasons, evidence/rule references and physical dependencies. `/ledger/lots/{lotId}` also opens scoped historical/exhausted lot detail. Browser performs no concentration arithmetic.

Application roles authorize recording reviewed decisions; they do not establish scientific validity. `reviewedBy`, `reviewAuthority`, `reviewedAt`, `evidenceRef` and `applicabilityStatement` must describe actual review. Server records publication actor/time. No scientific coefficients or rules are seeded.

### Streamlined rule and evidence entry

Rule setup is reduced to two human tasks: **Interpret laboratory results** and **Define processing factors**. Each task maps to the existing rule kind; rule kind is no longer manually selected. The app generates a fresh rule code (`RES-{draft UUID}`), UUID and version 1 for a new rule, and keeps identifiers stable while that draft is edited. Published-rule lists show business summaries and read-only version numbers. Choose an eligible published rule and **Revise rule** to start a fresh version with the same code and a predecessor link; series-defining task, substance and commodity stay fixed because the backend requires them to match. Use **Create new rule** or **Cancel revision** to leave revision flow. Already-superseded historical versions remain readable but cannot be revised directly.

This removes engine/history bookkeeping from human entry; code, version and supersession remain explicit API contract fields, automated by the UI rather than removed from backend behavior. Scientific gates stay explicit: applicability, reviewed values, unit, precision, uncertainty, evidence, effective dates and reviewer approval. Revision clears reviewer/date and effective dates; retained applicability/evidence remain editable. No numeric value or processing factor is invented.
Stage, process and `UNCONDITIONAL` condition suggestions are conveniences only; `UNCONDITIONAL` is never preselected, typed values remain allowed, and suggestions do not establish scientific approval.
Submit with **Publish reviewed rule**; success confirms **Reviewed residue rule published.** If concurrent publication returns 409, the published history refreshes while the draft remains. The app does not retry or rebase; choose latest eligible rule and **Revise rule** explicitly, then review and publish that new version.

Confirmed-report detail offers one confirmed-result picker that supplies its persisted source, substance and result IDs. Pending reports explain that confirmation must happen first. Native details wrap fallback controls in both report contexts and source-group registration, keeping first-load view compact while retaining supporting result IDs. These entry shortcuts preserve explicit review, applicability, supersession and unresolved-conflict rules.

### HTTP surface

Cookie mutations require existing CSRF protection; Bearer clients use normal authentication. Exact public records are defined in `ResidueRules.cs`, `ResidueEvidence.cs`, and `ResidueEvaluations.cs`.

| Method | Route | Contract |
| --- | --- | --- |
| POST / GET | `/api/residue-rules` | Publish reviewed immutable version / list `{items}`. |
| GET | `/api/residue-rules/{id}` | Read one authorized published rule. |
| POST / GET | `/api/residue-source-groups` | Publish own-company reviewed membership/fallback version / list `{items}`. |
| GET | `/api/residue-source-groups/{id}` | Read one currently authorized company-scoped version. |
| POST / GET | `/api/residue-report-selections` | Publish explicit reviewed result selection/supersession / list `{items}`. |
| GET | `/api/residue-report-selections/{id}` | Read one currently authorized company-scoped selection. |
| POST | `/api/ledger/lots/{lotId}/residue-evaluations` | `{id, substanceIds}`; fresh UUID and 1–200 distinct nonempty substance UUIDs. Appends and returns snapshot. |
| GET | `/api/ledger/lots/{lotId}/residue-evaluations` | Historical evaluations as `{items}`; never advertised as fresh calculations. |
| GET | `/api/ledger/lots/{lotId}/residue-evaluations/{id}` | One immutable historical snapshot, authorized under current scope. |

Evaluation returns `id`, `lotId`, `companyId`, `snapshotAt`, `persistedAt`, `engineVersion`, and `results`. Each result contains `substanceId`, `status` (`COMPLETE` or `UNRESOLVED`), decimal-string `lower`/`upper` or null, canonical `unit`/`basis`, `reasons`, `provenance`, `rules`, and `dependencies`. `COMPLETE` means a finite interval was established under the referenced reviewed inputs, not legal PASS. Derived provenance contains IDs and rule references, not laboratory file bytes/names or private report text. Historical snapshots retain their original `engineVersion`, including legacy identifier `F12-exact-1`; history/detail display maps that value to “Residue bounds exact 1” without changing the API field or saved payload. Saved values are never rewritten.

Same evaluation ID, authenticated actor, lot and canonical substance set replays the original snapshot even after evidence changes. Changed actor/payload conflicts; fresh evidence requires a new evaluation ID. Historical snapshots remain unchanged. Forged actor fields are rejected.

### Reviewed policy and evidence inputs

- Rule versions bind `code`/`versionNo`, `kind`, Catalog `commodityId`, `substanceId`, residue definition, stages, analytical bases, unit, explicit precision (0–18 decimal places), effective `[from,to)` interval and optional `supersedesId`. Supersession is explicit and transitive; overlapping applicable rules remain unresolved. Publication does not rewrite predecessors.
- `INTERPRETATION` supports `NUMERIC_ABSOLUTE_UNCERTAINTY`, `BELOW_LIMIT`, and `ND`. Exact analytical method and material/unit/definition match are required. Numeric intervals use reviewed normalized absolute uncertainty; unsupported/missing uncertainty stays unresolved. `<t` uses `[0,t]` only under its applicable rule. ND requires explicit `REPORTING_THRESHOLD`, `LOD`, or `LOQ` policy, never ND=0 or automatic LOQ-before-LOD fallback.
- `PROCESS` requires explicit process, input/output stage/basis, output fraction/product identity, conditions, nonnegative `factorLower`/`factorUpper`, and `STEP_CONCENTRATION` or `CUMULATIVE_CONCENTRATION`. `UNCONDITIONAL` explicitly records reviewed condition-independent applicability. Multiple inputs require `homogeneousMixing: true`; otherwise unresolved. Missing drying/milling/custody/bagging rules never become factor 1.
- Source groups bind explicit **Capture source IDs, not Ledger lot IDs**, company, substance, RAW_PADDY identity, material/residue definition, finite interval, reviewed precision, source-time window, supporting confirmed result IDs and/or external evidence. `DIRECT_FIRST`/`UNTESTED_ONLY` cannot hide unresolved/conflicting confirmed direct evidence; only explicitly reviewed `GROUP_FIRST` prioritizes fallback. Shared farmer/plot/ownership never creates membership.
- Report selections bind company/source/substance, confirmed `selectedResultId`, explicit `supersededResultIds`, normalized basis/definition and optional normalized absolute uncertainty/reporting threshold. Amendments/retests never win automatically; unresolved conflicting results remain unresolved. Supplements may add substance coverage without replacing earlier evidence.
- Only exact `mg/kg` scientific rules are supported. Other units remain raw F11 facts and unresolved until a supported reviewed interpretation exists. No automatic unit/moisture/residue-definition conversion is guessed.

### Physical paths, precision and consistency

Ledger follows committed resolved inputs and custody lot-to-lot edges, including ingress, aliquots, remainders, exhausted lots and positive inputs whose accounting source share rounds to zero. It never substitutes rounded root attribution for chemical exposure weights. Correction snapshots retain original physical ancestry; replacements use their own ancestry. Inconsistent corrected ancestors fail closed without weakening downstream correction guards.

Exact `BigInteger` rationals retain intermediates. Only result endpoints round outward under approved precision. Step-specific factors apply along each physical path before mass-weighted mixture combination; there is no extra yield adjustment. Cumulative factors can rewind an unbranched compatible path without double-applying intervening factors. Rewinding a mixture across changed historical mass bases remains explicitly `UNRESOLVED`; current input masses cannot establish old source-basis weights.

One read-only `REPEATABLE READ` transaction materializes authorized ancestry, rules and confirmed evidence using the same connection/transaction. Calculation uses that captured set. A separate protected `READ COMMITTED` append takes the existing authorization lock and rechecks current scope, then stores persistence time. Concurrent updates cannot produce hybrid evidence; snapshot is not guaranteed to represent later stock eligibility. No cross-request cache, eager descendant rewrite, stock/HOLD mutation, or market-assessment side effect exists.

### Scientific review sources and deployment gate

The [European Commission guidance index](https://food.ec.europa.eu/plants/pesticides/maximum-residue-levels/guidelines-maximum-residue-levels_en) identifies analytical quality-control/validation guidance (SANTE/11312/2021 v2026), processing studies (Appendix E), and the processing-factor information note (SANTE/10704/2021). These are review starting points, **not** approved GrainLedger interpretations, conservative coefficient intervals, group representativeness, or factory applicability. Bundled calibration CSV values remain research only. Domain review of actual methods, material/fraction definitions and authoritative studies is still required before operational numeric publication.

Forward migrations: Catalog `20261022000000_ReviewedResidueRules`, Capture `20261022000000_ReviewedResidueEvidence`, Ledger `20261022030000_ResidueBounds`. SQL-owned immutable payload tables require no replacement EF entity model or duplicate lineage graph. Runtime grants and validation cover new protected tables/functions; runtime cannot directly mutate reviewed versions/evaluations or read private Ledger snapshots/custody tables. Apply only through approved migration-owner workflow; no normal-startup migration, shared-data seed, backfill, reset, or automatic operational publication.

### Isolated acceptance evidence

Actual Testcontainers PostgreSQL fixture created procurement sources → drying → milling → approved blend → mixed pool → aliquot/remainder through real APIs, with explicitly labelled synthetic policy/evidence. Real cookie/CSRF browser actions showed unresolved missing evidence, then `[0.09, 0.22] mg/kg` after late confirmation, with original unresolved snapshot retained. Remainder and exhausted blend yielded the same interval through their own physical dependencies. Assigned operator read derived bounds but original report download returned 403; foreign-company lot/history access returned 403 with visible error. Administrator policy and Customer Manager multi-source fallback publication were exercised through existing UI. Desktop and 375px mobile views were inspected; document width stayed within viewport.

Regression coverage includes direct qualifiers/uncertainty, explicit ND thresholds, unsupported basis/unit/policy, fallback precedence/membership, fraction/cumulative/reconverging/tiny paths, late evidence/supplements/retests, original/replacement corrections, coherent read snapshots, current-scope persistence rejection, replay, immutability, runtime grants and unchanged business state. Explicit report-selection publication was also exercised through confirmed-report UI.

Original implementation checks on 2026-10-22 (before workflow streamlining):

- `dotnet build GrainLedger.slnx`: passed; existing unread `LedgerService.access` warning appears on nonincremental builds.
- Focused residue-calculator/evaluation, permission and OpenAPI tests: **59 passed, 0 failed**, isolated PostgreSQL.
- Full `dotnet test GrainLedger.slnx --no-build`: **381 passed, 1 failed of 382**. Existing `IdentityTests.Concurrent_admin_demotions_leave_one_active_administrator` observed `200 + 401` where its assertion expects a conflict; the same unrelated race is recorded in earlier repository verification. Not changed or weakened for residue bounds. An earlier full run passed that race but exposed a residue-bounds OpenAPI optional-Guid schema error; nullable omitted server publication metadata fixed it, and focused OpenAPI returned 200 afterward.
- `npm run test --workspace @grainledger/web -- --maxWorkers=2`: **112 passed**; `npm run build`: TypeScript and production Vite build passed. The unrestricted concurrent run hit two existing 5-second test timeouts under backend/browser workload; worker-bounded verification passed without changing test assertions/timeouts. Vite retains its large-chunk warning.
- Browser smoke used Development Vite and real restricted-runtime API via the test-only loopback host, with a disposable PostgreSQL 17 container. It establishes functional behavior, not production performance, external lab validity, domain approval or live Google/HTTPS setup. No shared Supabase migration, seed, stock write or scientific publication occurred.

## 10. Workflow streamlining verification

Verified on 2026-10-22 using the real application, synthetic reviewed inputs and disposable Testcontainers PostgreSQL only:

- `dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj --filter 'FullyQualifiedName~Residue|FullyQualifiedName~Permission|FullyQualifiedName~OpenApi' --logger 'console;verbosity=minimal'`: **61 passed, 0 failed**. The full backend suite was not rerun for this change.
- `npm run test --workspace @grainledger/web -- --maxWorkers=2`: **119 passed, 0 failed** across 22 files, without unhandled errors. New regressions cover conditional fields, kind/ND transitions, explicit unit and precision, new-version review requirements and supersession chains, confirmed-result bindings and fallback disclosure access. Existing lab submission fixtures now return valid detail responses and await the created report view; no assertions were weakened.
- `npm run build`: TypeScript and production Vite build passed. Vite still warns about chunks larger than 500 kB.
- Real cookie/CSRF browser publication created a fresh interpretation rule from one reviewed material stage/basis, with explicit unit/precision and no process factors; omission of the concentration unit prevented submission. A new milling-rule version retained factor `[0.5, 0.5]`, required fresh reviewer/date fields, and left its predecessor unchanged. Already-superseded predecessors could not start another version.
- Confirmed-result selection published the exact persisted source/substance/result binding with reviewed uncertainty `0.25`; its predecessor remained unchanged. Pending reports exposed no selectable result, and confirmed supporting results remained available in fallback maintenance. Native fallback disclosures were accessible in register and report contexts.
- Late confirmed evidence changed a fresh aliquot evaluation from `UNRESOLVED` to `[0.090000000000, 0.220000000000] mg/kg`. Original historical response remained byte-identical, and replay returned the original snapshot. A later evaluation after reviewed-selection replacement retained the same finite interval.
- Assigned operator history returned 200 while original report download returned 403. Foreign-company lot and evaluation-history reads returned 403 with visible “Lot access denied” feedback.
- Desktop 1440px and mobile 375px rule/evidence/result surfaces were inspected. Mobile report identity badges now wrap instead of overflowing; inspected pages stayed within viewport width. Feature headings and history display use descriptive names; stored engine identifiers and migration history remain unchanged.
- Validated 35 relative source/document links, renamed-plan anchors and removal of the old plan path. Test-only browser bridge now uses `/__browser/residue-bounds` and `residueBounds` metadata.

No shared Supabase migration, seed, publication or business-data write was performed. These checks establish application behavior, not scientific approval, legal compliance or production performance.

## 11. Task-based rule setup verification

On 2026-10-22, rule setup was simplified without changing the publication API, scientific rules, authorization, migrations or calculation workflow. Users choose **Interpret laboratory results** or **Define processing factors**, enter applicability, scientific settings and review, then **Publish reviewed rule**. Codes, version numbers and predecessor links are generated by the UI; published versions remain immutable.

- `dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj --no-build --filter 'FullyQualifiedName~Residue|FullyQualifiedName~Permission|FullyQualifiedName~OpenApi' --logger 'console;verbosity=minimal'`: **61 passed, 0 failed** against disposable PostgreSQL. Backend code was unchanged; the full backend suite was not rerun.
- `npm run test --workspace @grainledger/web -- --maxWorkers=2`: **121 passed, 0 failed** across 22 files. Coverage includes generated identity stability after failed publication, automatic revision lineage, independent new series after cancelling a revision, locked series scope, required scientific/review inputs and stale-history recovery.
- `npm run build`: TypeScript and production Vite build passed; existing large-chunk warning remains.
- Real cookie/CSRF browser actions created an ND interpretation rule with generated `RES-{UUID}` code, version 1 and no predecessor. Missing ND threshold blocked publication. Revision retained the series code, created a new UUID/version and predecessor link, required new review, and preserved the original record.
- After cancelling a revision, the processing task created an independent rule series with explicitly entered `[0.5, 0.6]` step-factor interval, explicit reviewed conditions and no interpretation/ND fields. Factors, conditions, unit and precision were not silently supplied.
- A real concurrent API publication advanced a processing rule while the browser held an older draft. The browser conflict refreshed history and preserved entered scientific/review values without retrying or rebasing. Explicitly revising the latest version published version 3 against version 2; versions 1 and 2 stayed unchanged.
- Desktop 1440px and mobile 375px setup surfaces were inspected, including keyboard focus through task choices. Both stayed within viewport width. No editable code, version or supersession controls remained.
- Customer Manager publication returned 403. Existing historical evaluation remained readable as `UNRESOLVED` with null endpoints. No shared Supabase writes, scientific defaults, policy-engine additions or wizard were introduced; lab and lot-calculation flows stayed unchanged.

Browser verification used the existing opt-in Testcontainers host and synthetic reviewed records only. Temporary browser session and smoke services were removed after verification. Results establish application behavior, not scientific approval or production performance.

## 12. Company-owned policy, system defaults and company overrides

Status: implemented locally and isolated API/browser verified on 2026-10-23; see section 13. This replaces the original manual-entry direction for reusable policy, company-scoped policy management and numeric uncertainty configuration. The original rationale, safeguards and acceptance scenarios remain. Existing lineage, exact arithmetic, evidence confirmation, conflict handling, history and authorization safeguards remain required. This implementation and isolated verification do not mean shared deployment, operational scientific approval or release approval.

**Historical planning note:** the preceding 2026-10-23 update was documentation-only and correctly described this work as planned at that point. The six implementation steps below have since been delivered locally; this plan edit does not itself implement code.

### Delivered semantics and bounded differences

- `SYSTEM_DEFAULT` is an explicit company setting that adopts or returns a profile series to its system-scoped rule. `COMPANY_OVERRIDE` selects a same-code company-scoped rule and fails closed if that intended override is missing or inapplicable; it does not fall back to the system rule. No setting means no implicit adoption.
- Rule versions are complete reviewed records. Company rules retain matching system-series substance, kind and commodity identity. Override applicability is resolved as a whole rule, not assembled from fields across versions. Concurrent applicable rules/settings and missing intended policy remain unresolved.
- Policy snapshots include company-visible rule/settings versions published by the captured snapshot time. Interpretation applicability uses source received time; process applicability uses each physical event time. Effective windows and explicit supersession determine applicable versions.
- Numeric relative uncertainty is an `INTERPRETATION` rule mode. Entered percentage is exact and nonnegative; `10` means 10%. It derives uncertainty from the reported numeric value, with exact arithmetic and outward rounding at final lot endpoints. Zero value is supported only when the reviewed rule explicitly enables `RelativeUncertaintyAtZero`.
- Explicit normalized report absolute uncertainty remains authoritative when supplied. Reported raw uncertainty without reviewed normalization stays unresolved; a relative profile does not silently replace or narrow it. Relative mode does not apply to below-limit or ND evidence, untested fallback, or missing process factors.
- Report-selection UI offers an adopted profile to prefill reviewed basis, residue definition and evidence references; result binding, review metadata and selection publication remain explicit. It does not silently publish a selection or remove all per-report review work.
- Verification evidence below is distinct from local implementation, deployment and scientific approval. Section 13 records observed build, focused test, frontend test/build and isolated browser outcomes; no shared-environment result is claimed.

### Responsibility and scope

**Company defines and takes responsibility for reviewed scientific assumptions. GrainLedger follows applicable company policy, calculates and explains results; it does not invent scientific policy or certify compliance.**

- Administrator publishes reusable reviewed system defaults. A company can adopt those defaults instead of entering every rule again.
- Customer Manager records own-company adoption or publishes a reviewed company override, supported by a qualified reviewer. Application role authorizes publication; it does not supply scientific validity.
- GrainLedger preserves laboratory originals, validates evidence and policy applicability, performs exact interval calculations over actual physical lineage, and records the inputs and versions used.
- This resembles the system-default/company-override hierarchy used for business tolerances, not their scientific meaning. Receiving shortage tolerance is a business acceptance threshold; residue uncertainty is an evidence-backed interpretation assumption. Do not reuse shortage percentages or MRL limits as concentration bounds.

Defaults are scoped scientific profiles, not one universal percentage or coefficient. Retain substance/residue definition, commodity, exact analytical method, stage/material basis, unit, precision, effective dates and supporting review. Processing rules additionally retain process, input/output basis and stage, fraction/product identity, conditions, factor semantics and reviewed mixing applicability. Group fallback membership and evidence remain separate company-scoped records.

In scope: reusable reviewed profiles using existing interpretation/process rule structures, company adoption/override settings, relative uncertainty, resolved-policy provenance and reduced routine entry. Out of scope: a generic settings engine, new calculation service, arbitrary per-lot manual bounds, automatic approval of scientific defaults, statistical confidence claims, market certification or changes to stock/HOLD behavior.

### Policy resolution and safeguards

1. Determine company scope from persisted lot/source ownership, not a request-supplied company, facility operator's company or selected UI filter. Load only authorized company settings and applicable shared defaults.
2. Resolve confirmed result selection and any explicit reviewed report uncertainty first. Preserve original uncertainty text and normalized interpretation. A profile must not silently replace laboratory-provided/reviewed uncertainty with a smaller assumed value; incompatible or conflicting uncertainty conventions require explicit reviewed resolution, otherwise `UNRESOLVED`.
3. For each required interpretation or processing applicability, resolve **applicable reviewed company override → applicable reviewed system default adopted by that company → `UNRESOLVED`**. An override replaces a complete scoped rule, not a mixture of arbitrary fields from different versions. Unoverridden applicability continues to inherit adopted defaults.
4. A company override may be tighter or wider only when its actual evidence and review support it; choosing a lower number is not proof. Require the same scientific metadata and applicability gates for both scopes. No hardcoded ±10%, drying/milling factor 1, ND zero or MRL-derived fallback.
5. Multiple unsuperseded applicable rules at the selected scope, unresolved report conflicts, incomplete evidence or an invalid intended override fail closed. Never hide these problems by dropping to a lower-precedence default or choosing latest/lowest. Another company's override cannot participate.
6. Keep current effective-time semantics: interpretation rules cover source received time; process rules cover their physical event time; policy settings/versions must have been published and visible in the captured evaluation view. Adoption/override settings are versioned with explicit effective intervals. Snapshot records the exact adoption/override setting and selected rule versions; no ambiguous "current default" reference.
7. New evaluations resolve applicable published versions from their coherent snapshot. A later policy change affects only new evaluations when applicable; replay and historical reads retain original values, rules, scope and policy origin. Removing an override requires an explicit reviewed, versioned return to inheritance; do not delete history.

### Relative uncertainty example

Add a reviewed numeric uncertainty mode alongside existing report-specific absolute uncertainty. A relative percentage is applied to the reported numeric concentration, not to an MRL, remaining mass, factor or already-propagated lot interval.

For reported value `v ≥ 0` and reviewed relative uncertainty percentage `p ≥ 0`:

- `u = v × p / 100`
- `lower = max(0, v - u)`
- `upper = v + u`

Synthetic example only: `v = 0.15 mg/kg`, `p = 10` gives `u = 0.015 mg/kg` and interval `[0.135, 0.165] mg/kg`. This is **±10% of measured value**, not ±0.10 mg/kg, a processing factor, a legal tolerance or an established confidence level. Ten percent is illustrative, not a seeded operational default. Use exact decimal/rational arithmetic and existing outward rounding; record entered percentage, derived absolute uncertainty and originating profile version.

Relative uncertainty applies only where reviewed method/material applicability explicitly supports that convention. It does not overwrite a reported result or automatically override its uncertainty. Absence of laboratory uncertainty can be covered only by an applicable evidence-backed profile convention. A numeric zero is not permission to invent certainty: use percentage-only interpretation at zero only if the reviewed convention explicitly supports that boundary; otherwise require a supported absolute/threshold interpretation or return `UNRESOLVED`.

Below-limit and ND interpretation remain threshold-based. ND still needs its reviewed reporting-threshold/LOD/LOQ policy and positive value; no automatic threshold choice or ND × percentage. Untested sources still require explicit reviewed source-group fallback. Processing factors remain independently reviewed and fraction-specific; measurement uncertainty cannot supply missing milling/drying factors.

### Reduced-entry workflow

- **Administrator:** configure and review reusable system rules using existing rule tasks and metadata. No automatic scientific publication or universal seeded coefficients.
- **Customer Manager:** publish own-company adoption/return settings or choose a company override, with explicit review. Setting history and available rule applicability are visible; role grants publication capability, not scientific validity.
- **Report review:** choose a confirmed result and optionally select an adopted profile to prefill basis, residue definition and evidence references. Identity confirmation, result selection, review, conflict/supersession and report-specific exceptions remain explicit. Preview remains non-mutating.
- **Lot detail/history:** show interval and provenance, including policy origin, setting ID/version, rule version and uncertainty mode/value where present. Do not expose another company's policies or laboratory originals through derived provenance.

Suggested routine summary for an applicable adopted profile: **System default — method X; relative uncertainty ±10% of reported value; company override: none; review: view profile**. This remains a display example, not a claim that method X or 10% is approved.

### Implementation and verification status

| Step | Change | Completion criterion | Implementation status | Verification status |
| --- | --- | --- | --- | --- |
| 1 | Extend Catalog's existing immutable rules with explicit system/company scope, reusable profile applicability, company adoption/override settings and relative-uncertainty fields. Keep complete rule versions; no generic key/value policy engine or duplicate calculator. | Contract defines exact percentage units, supported zero boundary, full-rule override scope, effective dates, review and supersession. Existing published records retain meaning as system-scope rules; do not rewrite saved payloads or silently adopt them for companies. | Implemented locally; existing rules remain system scope and are not auto-adopted. | Verified in focused tests and isolated API/browser path; see section 13. |
| 2 | Add narrow own-company profile-management authorization for Customer Manager, distinct from Administrator's global publication permission. Extend protected Catalog reads/commands and module-owned forward migrations as needed. | Persisted active role and company ownership enforced on publish/list/detail/revision/adoption; global publication and foreign-company access denied. Supersession/version uniqueness remains confined to its company/system series. | Implemented locally; separate `residue.company-policy.manage` permission and company-scoped setting publication/read paths. | Verified in focused tests and isolated API/browser path; see section 13. |
| 3 | Extend shared snapshot materialization and the existing resolver/calculator to load company settings, resolve precedence and derive exact relative uncertainty. Retain Capture's confirmed selection and fallback contracts. | No per-record HTTP fetches, cross-request cache, stock writes or hybrid read snapshots. Explicit uncertainty, ND/untested behavior, conflicts and process-factor independence remain fail-closed. | Implemented locally; existing resolver and calculator load scoped policy snapshots and apply explicit precedence. | Verified in focused tests and isolated API/browser path; see section 13. |
| 4 | Extend immutable evaluation provenance to store resolved scope, setting/profile/rule versions, uncertainty mode, percentage and derived absolute uncertainty where used. | New evaluations reproduce the chosen policy; old payloads, history and idempotent replay remain unchanged and readable without fabricated metadata. | Implemented locally; policy origin/setting and rule versions plus uncertainty provenance are recorded when used. | Verified in isolated API/browser path; see section 13. |
| 5 | Update existing Administrator/company setup, report-selection and lot-detail surfaces for inherit/override and derived uncertainty preview. Update API contracts, architecture, entry guide and manual test together. | Existing surfaces expose adoption/override and policy provenance; report profile choice can reuse reviewed basis, definition and evidence references, while result binding, review and publication remain explicit. Use existing screens/dependencies. | Implemented locally; existing screens expose setting and profile choice, and report profile choice prefills reviewed basis/definition/evidence. Review and result binding remain explicit; profile choice does not auto-publish. | Verified in isolated browser path at desktop/mobile widths; see section 13. |
| 6 | Verify focused behavioral regressions and actual isolated API/browser flows, then record deployment gates separately. | Exercise adoption, default inheritance, reviewed override, percentage arithmetic, denied foreign/global writes, unresolved cases, policy change/recalculate/history and desktop/mobile UI. No shared Supabase migration, seed or data-changing smoke without explicit database-owner approval. | Implementation delivered locally; no shared deployment or scientific release gate cleared. | Focused API tests and real isolated browser path passed; see section 13. |

### Acceptance additions

| Scenario | Required result |
| --- | --- |
| Company adopts applicable system profile | Routine report review reuses its scientific context; evaluation records system-default origin and exact adoption/profile/rule versions. |
| Company publishes applicable reviewed override | Own-company calculation uses override; another company's otherwise identical lot continues under its own adopted/default policy. |
| Relative 10% synthetic example | `0.15 mg/kg` produces `[0.135, 0.165]`; derived `0.015 mg/kg` uncertainty and percentage/profile version retained. |
| Explicit report uncertainty differs from configured percentage | No silent narrowing or replacement. Apply compatible reviewed report interpretation; unsupported/conflicting conventions remain `UNRESOLVED` until reviewed. |
| No company adoption, missing applicability, invalid override or same-scope conflict | `UNRESOLVED`; no universal fallback or lower-precedence conflict masking. |
| ND, zero, untested substance or missing processing factor | Percentage cannot erase uncertainty or invent a measurement/factor. Approved applicable boundary/threshold/fallback rule required; otherwise `UNRESOLVED`. |
| Override ended or profile changed | Explicit versioned inheritance/effective dates determine fresh evaluation; original snapshots and replays stay unchanged. |
| Authorization and side effects | Customer Manager cannot publish global rules or access another company's overrides. No new access to laboratory originals; stock, postings, HOLDs and raw evidence unchanged. |

The original planning-only update did not implement code. Section 12's six steps are now implemented locally and verified in isolation as recorded in section 13. Shared Supabase migration, seed, data-changing smoke and operational scientific publication remain separate approval gates.

## 13. Section 12 isolated verification

Verified 2026-10-23 against disposable PostgreSQL 17 and local Testing API/Vite services using synthetic reviewed records. No shared Supabase migration, seed, business-data write, operational scientific publication, commit or push.

### Build and tests

- `dotnet build GrainLedger.slnx --no-restore`: passed, 0 warnings and 0 errors.
- `GRAINLEDGER_TEST_HOST_NETWORK=true TESTCONTAINERS_RYUK_DISABLED=true dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj --no-build --filter 'FullyQualifiedName~Residue|FullyQualifiedName~Permission|FullyQualifiedName~OpenApi' --logger 'console;verbosity=minimal'`: 79 passed, 0 failed, 0 skipped. Full backend suite not rerun.
- `npm run test --workspace @grainledger/web -- --maxWorkers=2`: 125 passed across 23 files.
- `npm run build`: passed. Existing Vite chunk warning remains (634.64 kB); not fixed here.

### Isolated API/browser scenarios

- Real cookie/CSRF UI flow began with no adoption; lot evaluation returned `UNRESOLVED`. Publishing company `SYSTEM_DEFAULT` setting v1 made evaluation return `[0.135, 0.165] mg/kg` from synthetic `0.15 mg/kg` evidence under reviewed 10% uncertainty (`0.015 mg/kg`).
- A same-code complete company rule with reviewed 20% uncertainty and `COMPANY_OVERRIDE` setting v2 changed fresh evaluation to `[0.12, 0.18] mg/kg` (`0.03 mg/kg` uncertainty). Explicit `SYSTEM_DEFAULT` return setting v3 restored `[0.135, 0.165] mg/kg`.
- Earlier evaluation snapshots reopened through UI remained unchanged; original pre-adoption GET result remained JSON-equivalent after policy changes. This demonstrates isolated history behavior, not shared-data or production behavior.
- Report-selection UI bound a confirmed result and prefilled profile basis, residue definition and evidence. Preview without explicit supersession returned a conflict; adding the reviewed superseded-selection ID returned `[0.12, 0.18] mg/kg` under the 20% policy. Preview did not change published selections; editing the draft cleared preview.
- Setup and report surfaces were checked at 320, 375, 768 and 1440px without horizontal document overflow; lot history was visually inspected at 375 and 1440px. Keyboard Tab moved from profile to mode. Long policy-history codes wrap on mobile; report fieldset sizing avoids min-content overflow.

These synthetic scenarios verify application mechanics only. They do not establish laboratory-method validity, representativeness, approved operational coefficients, legal compliance, production performance or deployment readiness.
