# F12 residue bounds: readiness assessment and implementation plan

Status: F12 implemented locally on 2026-10-22; forward migrations and synthetic HTTP/browser scenarios exercised only on disposable PostgreSQL. No shared Supabase deployment, backfill, scientific approval, commit, or push. Sections 2–8 retain the pre-implementation assessment and design rationale; section 9 documents the delivered contracts and verification.

## 1. Verdict

**GrainLedger now implements scoped, reproducible F12 lot calculations. It still lacks approved operational scientific policy/data sufficient to establish finite bounds for every lot. Missing evidence, interpretation or applicable process rules produce explicit per-substance `UNRESOLVED` results, with null unknown endpoints—not guessed numbers or PASS.**

The existing procurement/laboratory/physical-ledger foundation is retained. Catalog now owns reviewed rule versions; Capture owns explicit source-group fallback and report-selection evidence; Ledger reads physical ancestry and appends immutable calculation snapshots. These engineering capabilities do not approve laboratory methods, representativeness, coefficients, or export compliance.

Do not confuse these three outputs:

- **F11 fact:** what a laboratory reported about a particular procurement sample.
- **F12 calculation:** a substance-specific residue interval for a target lot, with its evidence, transformation path, and unresolved dependencies.
- **Market assessment:** whether sufficient suitable evidence establishes compliance for a particular commodity, market, legal version, date, and safety margin. This consumes F12; it is not F12 itself.

Confidence is high for the source-code findings below. Deployed schema state, production data completeness, scientific validity of reference coefficients, and current legal applicability were not verified.

## 2. Proposal requirements

Page numbers refer to the 12-page [proposal PDF](GrainLedger_V2.pdf), not a separate SRS or prototype UI.

| Requirement | Proposal evidence | F12 implication |
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

### 3.2 Existing VN comparison is not F12

`ResponsesAsync` selects the highest published VN version number per substance and commodity, then calls `AssessVn`: [LabReportEndpoints.cs:342–368](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs#L342-L368).

The existing routine handles numeric, below-limit, and ND comparisons, including a LOQ-before-LOD fallback: [lines 388–488](../backend/src/GrainLedger.Modules.Capture/LabReportEndpoints.cs#L388-L488). It does not compute lot intervals, traverse descendants, apply processing factors, or select conflicting report evidence.

Important limits of reuse:

- The query does not select by effective interval or one resolved sample commodity; a substance can be compared against several commodity rows.
- Result units are checked, but the comparison branches do not validate the fetched limit unit.
- The comparison is recomputed when reading a report, not persisted as an immutable decision-time assessment.
- Existing ND behavior is implementation behavior, not evidence that the F12 policy has been scientifically approved.

[Architecture documentation:65–66](architecture.md#L65-L66) still says all reports remain `NOT_ASSESSED`; this is stale relative to the endpoint code. [Lab-flow-plan.md:3](lab-flow-plan.md#L3) already distinguishes the numeric VN baseline from full assessment. Preserve that distinction in F12 documentation and UI; do not expand F12 into an unrelated rewrite of legal assessment.

### 3.3 Processing and lineage

Lot stage, product identity, originating operation, operation time/content/status, signed postings, reversal references, and root contributions are persisted in [Schema.sql:58–119](../backend/src/GrainLedger.Modules.Ledger/Sql/Schema.sql#L58-L119).

Actual mixed-bulk-aware processing records an immutable event containing operation content and resolved inputs, debits those inputs, and creates output lots: [MixedBulkCommands.sql:302–319](../backend/src/GrainLedger.Modules.Ledger/Sql/MixedBulkCommands.sql#L302-L319). This is the useful F12 path evidence; the current code subsequently allocates **mass**, not chemical concentration.

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
5. Preserve correction/reversal semantics rather than treating compensation as another blend input. Current correction handling rejects downstream consumption: [CanonicalCatalog.sql:600–605](../backend/src/GrainLedger.Modules.Ledger/Sql/CanonicalCatalog.sql#L600-L605). Do not weaken that guard for F12. Historical evaluation of an original corrected lot retains its original physical path; current evaluation of replacement material uses the replacement lot. If legacy/inconsistent history contains a descendant of an output later corrected, preserve evidenced physical ancestry and mark unresolved applicability where necessary; never silently substitute replacement ancestry.

### 3.4 Factors and frontend

The bundled [substance CSV:1–15](../tools/catalog-reference-data/pesticide_substances-v1.1.csv#L1-L15) already contains four fraction-specific factor columns plus `pf_basis` and `pf_source`. This is useful research material, **not** an approved production factor catalog:

- Some entries are explicitly calibration-only.
- Bases include paddy-to-fraction, husked-to-polished, and brown-rice transformations.
- The imidacloprid note distinguishes residue definitions with different factors.
- [MRL import documentation:70–73](mrl-file-import.md#L70-L73) confirms factors are not imported into operational API fields.

Do not multiply a paddy-to-finished-rice cumulative factor at every intermediate step, apply a husked-rice factor to paddy, or add a yield adjustment on top of a coefficient that already represents concentration change.

The working app is `apps/web/src`, not the nested `apps/web/grainledger` prototype. It already has functional stock and processing screens:

- [LedgerWorkspace.tsx:3–6,31–32](../apps/web/src/LedgerWorkspace.tsx#L3-L32) mounts the real components.
- [LedgerStock.tsx:15–40,63–69](../apps/web/src/LedgerStock.tsx#L15-L69) fetches scoped stock/lot detail and displays positions, source composition, and history, but no F12 bounds.
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

Market coverage, legal-version selection, and Manager safety margins must also be approved **before a consumer issues a compliance decision**. They are not prerequisites for storing an honest unresolved F12 result, and are not default inputs to its physical concentration calculation.

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

This design provides logical propagation through the committed graph, not eager writes to every descendant whenever a source report arrives. Existing stock commits remain stock commits; incomplete F12 evidence does not invent a new stock/HOLD blocking policy.

### API and frontend

Proposed minimum surface:

- A lot-bound evaluation command that computes current bounds server-side and appends an immutable snapshot; use existing idempotency/conflict conventions. A replay returns the original snapshot; a new evaluation request is required for fresh evidence.
- A scoped historical-evaluation read. Historical snapshots remain explicitly historical; do not expose an unvalidated cached “latest” as current.
- Decimal-string lower/upper values, canonical concentration unit and analytical basis, per-substance completeness/reasons, direct/group/inherited provenance, factor/rule versions, and operation/custody dependencies. Unknown endpoints are null with explicit status/reasons, never numeric sentinel values. Do not return a global PASS flag.
- Existing lot permissions govern lot access; exposure of laboratory originals still requires Capture authorization. Define derived-bound visibility explicitly for facility staff rather than giving every stock viewer `lab.read`. Revalidate owner/facility/active role on both evaluation and historical reads. Derive actors server-side.

Extend the existing lot-detail panel in `LedgerStock.tsx`, with typed shared contracts and a small residue panel if extraction is warranted. Offer explicit **Calculate / Recalculate bounds** and historical evaluation inspection. Show computation time, substance, interval, unit/basis, evidence origin, applied process rules, and missing-policy/evidence reasons. Handle loading, errors, unresolved and complete results accessibly. Do not run concentration arithmetic in JavaScript or issue one request per substance/ancestor.

Add narrow rule-maintenance and source-group/fallback controls to existing Catalog/Capture workflows using the approved role matrix, not a separate admin application. Keep laboratory confirmation and VN numeric comparison visibly separate from calculated lot bounds. No replacement of the working processing screen or prototype migration is required.

A future blend/market consumer must request fresh bounds and retain the exact evaluation ID alongside its own applicable MRL version and safety margin. It must reject incomplete required-substance evidence. Implementing that consumer is outside this F12 change.

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

Use existing backend integration-test infrastructure with isolated PostgreSQL and real permission checks. Synthetic rule values are test inputs only, never production approvals. Extend relevant existing lab/ledger tests where they exercise the same behavior; add focused F12 coverage for the new calculation semantics.

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

The proposal's 50%/30%/20% lineage example is useful for mass attribution but contains no residue measurements or approved factors. It cannot supply a numeric F12 acceptance result by itself.

## 8. Evidence exercised in this assessment

- Extracted text from all 12 proposal pages and inspected their rendered images, including the fraction-factor note and conservative inheritance rule.
- Traced repository source and existing regression code. Existing test names/results cited above are not claims that those tests ran in this assessment.
- Ran `python3 -B tools/import_catalog_reference.py` in offline preview mode: 14 substances, 5 active numeric VN limit rows, 9 excluded `NO_MRL` rows, and two draft groups. This verifies bundled importer behavior only; it does not approve coefficients, establish current law, or inspect the deployed Catalog.
- Validated local source/document links and their cited line ranges.
- No authenticated application/API/browser smoke, application build, integration-test run, migration, policy publication or database write was performed. No running local application listener was available when checked. Operational readiness therefore remains unverified; implementation must run the isolated smoke above.

## 9. Delivered F12 contracts

### Permissions and workflow

- `residue.policy.manage`: Administrator publishes immutable reviewed Catalog rule versions. Open **Administration → Catalogs → Commodities** or **Ledger → Setup → Catalog and basic facility setup**.
- `residue.evidence.manage`: Customer Manager publishes own-company source-group/fallback and reviewed report selections. **Laboratory reports** exposes fallback controls without requiring a laboratory report; confirmed-report detail also exposes explicit result selection.
- `residue.evaluate`: Customer Manager and Export Staff for own-company lots; Mill Manager and Mill Operator only within assigned lot facilities. Administrator is not a business wildcard. Every evaluation/history read rechecks persisted active role and current scope. Derived facility access grants no laboratory original download.
- **Ledger → Stock → lot detail** offers explicit Calculate/Recalculate, historical snapshot inspection, reasons, evidence/rule references and physical dependencies. `/ledger/lots/{lotId}` also opens scoped historical/exhausted lot detail. Browser performs no concentration arithmetic.

Application roles authorize recording reviewed decisions; they do not establish scientific validity. `reviewedBy`, `reviewAuthority`, `reviewedAt`, `evidenceRef` and `applicabilityStatement` must describe actual review. Server records publication actor/time. No scientific coefficients or rules are seeded.

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

Evaluation returns `id`, `lotId`, `companyId`, `snapshotAt`, `persistedAt`, `engineVersion`, and `results`. Each result contains `substanceId`, `status` (`COMPLETE` or `UNRESOLVED`), decimal-string `lower`/`upper` or null, canonical `unit`/`basis`, `reasons`, `provenance`, `rules`, and `dependencies`. `COMPLETE` means a finite interval was established under the referenced reviewed inputs, not legal PASS. Derived provenance contains IDs and rule references, not laboratory file bytes/names or private report text.

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

Final checks on 2026-10-22:

- `dotnet build GrainLedger.slnx`: passed; existing unread `LedgerService.access` warning appears on nonincremental builds.
- Focused F12 calculator/evaluation, permission and OpenAPI tests: **59 passed, 0 failed**, isolated PostgreSQL.
- Full `dotnet test GrainLedger.slnx --no-build`: **381 passed, 1 failed of 382**. Existing `IdentityTests.Concurrent_admin_demotions_leave_one_active_administrator` observed `200 + 401` where its assertion expects a conflict; the same unrelated race is recorded in earlier repository verification. Not changed or weakened for F12. An earlier full run passed that race but exposed an F12 OpenAPI optional-Guid schema error; nullable omitted server publication metadata fixed it, and focused OpenAPI returned 200 afterward.
- `npm run test --workspace @grainledger/web -- --maxWorkers=2`: **112 passed**; `npm run build`: TypeScript and production Vite build passed. The unrestricted concurrent run hit two existing 5-second test timeouts under backend/browser workload; worker-bounded verification passed without changing test assertions/timeouts. Vite retains its large-chunk warning.
- Browser smoke used Development Vite and real restricted-runtime API via the test-only loopback host, with a disposable PostgreSQL 17 container. It establishes functional behavior, not production performance, external lab validity, domain approval or live Google/HTTPS setup. No shared Supabase migration, seed, stock write or scientific publication occurred.
