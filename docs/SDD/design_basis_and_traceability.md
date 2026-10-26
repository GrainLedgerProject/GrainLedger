# SDD design basis and traceability

### Source Baseline and Design Decisions

| ID | Source | Use in this document |
| --- | --- | --- |
| S1 | ../Report4_Software Design Document.docx | Section structure and required diagram/specification content. |
| S2 | ../Report3_Software Requirement Specification.docx.md | Current 38 use cases, actors, FRs and MVP boundary. |
| S3 | ../erd_v2_1/BUSINESS_RULES.md and existing 01–06 ERD sources | Detailed contracts R01–R11, RV01–RV05 and existing entity names. |
| S4 | ../FA26SE272_GrainLedger_Proposal_v1.1.docx | Project scope, six connected capabilities, evidence explanation and work packages. |
| S5 | ../overall/GrainLedger_Context_UseCase.drawio and ../rev_3/plantuml/ | Actor associations, overall/detail use-case structure. |
| S6 | ../../GrainLedger_V2.pdf | Original domain context; interpreted through the current SRS and supplied rule baseline. |
| S7 | all-in-one.svg, DBDiagram.png and UseCase.png — supplied during drafting | Current supplied ERD, physical database overview and 38-use-case drawing. The logical ERD contains the same 74 entity names as S3; physical naming/storage differences are mapped below. |

The current SRS and its referenced ERD rules determine the detailed behavioral contracts. The updated proposal provides project context and added evidence explanations. Where they differ, the conflict is explicitly retained in the review notes below; this draft does not silently amend either source. Earlier SRS change-log statements about microservices or fixed solver technology are historical and do not define the selected design.

| Decision | Status and rationale |
| --- | --- |
| Logical architecture | Proposed: two browser clients and a modular core backend, sharing a protected transactional persistence boundary. Modules are logical components, not mandatory independent microservices. This permits the atomic contracts in R01/R03/R08. |
| Names and interfaces | Proposed: technology-neutral namespaces, services, method signatures and ports in this document. Domain entity names reuse the existing ERD. No framework, database product, queue, solver or hosting provider is implied. |
| Concurrency | Proposed implementation contract: transaction-scoped locks or equivalent serializable checks over affected stock, reservations, plan/container authority and evidence/restriction revisions. All commit paths participate. Revision changes force reevaluation; rejected operations do not partially post. |
| Immutable content | R05 contract: durable non-overwriting bytes, digest, schema version and scoped retrieval. Initial design may use the same transactional persistence for authoritative snapshots; a separate object-store product is not required. |
| Scope | Core MVP only. No buyer portal, automatic MRL import, ERP/customs interface, payment, AI prediction, HOLD release, standalone compensation, loss-only write-off or mandatory advanced optimization. |


## 4. Traceability and Review Notes

### 4.1 Use-Case Coverage

| UC | Use case | Design section | Class source | Sequence sources / shared structure |
| --- | --- | --- | --- | --- |
| UC-01 | Manage customer companies | 3.1 | 05_class.mmd | 05_sequence_publish.mmd |
| UC-02 | Manage accounts, roles and facility grants | 3.1 | 05_class.mmd | 05_sequence_publish.mmd |
| UC-03 | Configure facilities, equipment and capacity | 3.1 | 05_class.mmd | 05_sequence_publish.mmd |
| UC-04 | Configure balance policy | 3.1 | 05_class.mmd | 05_sequence_publish.mmd |
| UC-05 | Manage operational master data | 3.1 | 05_class.mmd | 05_sequence_publish.mmd |
| UC-06 | Manage effective MRL table versions | 3.1 | 05_class.mmd | 05_sequence_publish.mmd |
| UC-07 | Register farmers and plots | 3.2 | 06_class.mmd | 06_sequence_receipt.mmd, 06_sequence_sync.mmd |
| UC-08 | Record pesticide declaration | 3.2 | 06_class.mmd | 06_sequence_receipt.mmd, 06_sequence_sync.mmd |
| UC-09 | Record procurement receipt | 3.2 | 06_class.mmd | 06_sequence_receipt.mmd, 06_sequence_sync.mmd |
| UC-10 | Capture procurement sample | 3.2 | 06_class.mmd | 06_sequence_receipt.mmd, 06_sequence_sync.mmd |
| UC-11 | Capture field and procurement records offline | 3.2 | 06_class.mmd | 06_sequence_receipt.mmd, 06_sequence_sync.mmd |
| UC-12 | Synchronize submitted records | 3.2 | 06_class.mmd | 06_sequence_receipt.mmd, 06_sequence_sync.mmd |
| UC-13 | Record physical operation | 3.3 | 07_class.mmd | 07_sequence_post.mmd |
| UC-14 | Request operation correction | 3.4 | 08_class.mmd | 08_sequence_reconcile.mmd, 08_sequence_commit.mmd |
| UC-15 | Execute approved plan scope | 3.3 | 07_class.mmd | 07_sequence_post.mmd |
| UC-16 | Commit reconciled compensation and replacement | 3.4 | 08_class.mmd | 08_sequence_reconcile.mmd, 08_sequence_commit.mmd |
| UC-17 | Verify and reconcile physical evidence | 3.4 | 08_class.mmd | 08_sequence_reconcile.mmd, 08_sequence_commit.mmd |
| UC-18 | Confirm signed measurement variance | 3.3 | 07_class.mmd | 07_sequence_post.mmd |
| UC-19 | Inspect owner stock and source composition | 3.3 | 07_class.mmd | 07_sequence_post.mmd |
| UC-20 | Register laboratory evidence | 3.5 | 09_class.mmd | 09_sequence_assess.mmd, 09_sequence_late.mmd |
| UC-21 | Register superseding report | 3.5 | 09_class.mmd | 09_sequence_assess.mmd, 09_sequence_late.mmd |
| UC-22 | Configure safety margin policy | 3.5 | 09_class.mmd | 09_sequence_assess.mmd, 09_sequence_late.mmd |
| UC-23 | Review and approve or reject plan | 3.6 | 10_class.mmd | 10_sequence_propose.mmd, 10_sequence_review.mmd |
| UC-24 | Trace source contributions to and from containers | 3.8 | 12_class.mmd | 12_sequence_trace.mmd, 12_sequence_hold.mmd |
| UC-25 | Trace affected lots, plans and containers | 3.8 | 12_class.mmd | 12_sequence_trace.mmd, 12_sequence_hold.mmd |
| UC-26 | Issue lot or container HOLD | 3.8 | 12_class.mmd | 12_sequence_trace.mmd, 12_sequence_hold.mmd |
| UC-27 | View retained HOLD alerts | 3.8 | 12_class.mmd | 12_sequence_trace.mmd, 12_sequence_hold.mmd |
| UC-28 | Record HOLD handling | 3.8 | 12_class.mmd | 12_sequence_trace.mmd, 12_sequence_hold.mmd |
| UC-29 | Create export order and container target | 3.6 | 10_class.mmd | 10_sequence_propose.mmd, 10_sequence_review.mmd |
| UC-30 | Request blend plan | 3.6 | 10_class.mmd | 10_sequence_propose.mmd, 10_sequence_review.mmd |
| UC-31 | Record container loading and metadata | 3.7 | 11_class.mmd | 11_sequence_load.mmd, 11_sequence_close.mmd, 11_sequence_documents.mmd |
| UC-32 | Close container | 3.7 | 11_class.mmd | 11_sequence_load.mmd, 11_sequence_close.mmd, 11_sequence_documents.mmd |
| UC-33 | Export shipment documents | 3.7 | 11_class.mmd | 11_sequence_load.mmd, 11_sequence_close.mmd, 11_sequence_documents.mmd |
| UC-34 | Retrieve compliance dossier | 3.7 | 11_class.mmd | 11_sequence_load.mmd, 11_sequence_close.mmd, 11_sequence_documents.mmd |
| UC-35 | Add dossier evidence supplement | 3.7 | 11_class.mmd | 11_sequence_load.mmd, 11_sequence_close.mmd, 11_sequence_documents.mmd |
| UC-36 | Validate ledger operation | 3.3 | 07_class.mmd | 07_sequence_post.mmd |
| UC-37 | Assess current compliance | 3.5 | 09_class.mmd | 09_sequence_assess.mmd, 09_sequence_late.mmd |
| UC-38 | Search business events and history | 3.9 | 13_class.mmd | 13_sequence_history.mmd |

Coverage refers to shared feature design, not one duplicated drawing per use case. UC-01–UC-05 reuse administration with distinct validation; UC-07/08/10 reuse scoped field acceptance; UC-18 is the conditional manager-confirmed variance path; UC-19 is the protected stock query; UC-22 reuses versioned policy configuration under Customer Manager scope; UC-33 shares the document-access/versioning service. These cross-references preserve the template instruction to avoid duplicate class and sequence structures.

### 4.2 Source Conflicts and Unresolved Implementation Choices

| Item | Draft treatment and remaining action |
| --- | --- |
| Proposal loss versus SRS variance | Proposal 1.1 says in-tolerance shortfall becomes explicit loss and apparent gain needs correction. Current SRS/ERD R01/RV05 retain signed variance with Mill Manager confirmation, including negative in-tolerance variance. This SDD follows SRS/ERD for detailed posting. The team must align the proposal wording before presenting a single approved baseline. |
| Role terminology | Proposal Collection Agent / Export Staff are mapped to current SRS Customer Procurement Officer / Customer Export Officer. Six internal roles are retained. |
| Source-lot lab model | Current ERD Sample references PaddyLot. The proposal synthetic rice-bound example does not by itself authorize adding a processed-lot sampling schema. Confirm an extension explicitly if operational testing of processed rice is needed. |
| Technology and deployment | Proposed logical module/namespace/service names are not code discovered in a repository. Frameworks, persistence engine, authentication integration, hosting and concrete API routes remain to be selected. |
| Evidence calibration | Validate cohort sampling/coverage and upper-bound method, ND/LT interpretation, output-fraction processing rules and quality aggregation. No evidence means INCOMPLETE, not a zero or a default factor. |
| Numeric/storage details | The supplied physical model gives kg NUMERIC(20,6) and measurement/concentration NUMERIC(24,12); confirm unit conversion and input rounding at that precision. Pin balance/margin policy values and content digest/schema/version conventions. |
| Concurrency implementation | All evidence publication, restriction changes and stock/reservation writes must participate in the same freshness/locking scheme. Confirm lock order, retry and deadlock handling before coding. |
| Access and performance acceptance | Confirm concrete grants, responsible HOLD recipient assignments and browser/device support. Benchmark documented seasonal workload; inherited 30-second traceback and one-second ledger targets are not measured results. |

### 4.3 Design Validation Scenarios

| Scenario | Required observable result |
| --- | --- |
| Two concurrent withdrawals exceed one allocation | Only permitted available quantity commits; no negative balance or partial posting. |
| Same offline key and same/changed payload | Same payload returns retained outcome; changed payload is conflict and never another receipt. |
| Nonzero signed variance | Within confirmed percent tolerance requires Mill Manager confirmation; outside tolerance rejected. |
| Correction with downstream use | BLOCKED_DOWNSTREAM; remaining affected stock stays restricted; no standalone reversal. |
| Correction reservation accounting | Old CONSUME reversal plus replacement CONSUME counted net exactly once; no temporary reusable raw stock. |
| Untested lot without supported cohort/propagation | Compliance INCOMPLETE; valid unrelated physical recording remains possible. |
| Synthetic blend example from proposal | 0.078 mg/kg passes 0.08 threshold; 0.084 fails. Values are fixtures only, not regulatory claims. |
| Wrong plan output reservation at loading | Rejected despite apparently sufficient lot stock. |
| Late evidence during loading or CLOSE | Current decision invalidated/recomputed; unfinished authority blocked/staled as applicable. |
| CLOSE with P1 and P2 committed loading | Assess all loading versions; exact target and current PASS; commit CLOSED and one dossier together. |
| HOLD with missing required recipients | Configuration error; no false successful notice result. |
| Cross-company query or guessed dossier/event ID | Access denied or scoped result; no leaked farmer/lab/shipment details. |


## Sources.docx alignment

The supplied HomeCycle SDD is used only as a presentation example: architecture with component explanations; backend, website and field-client package diagrams with No / Package / Description; a domain class diagram followed by separately numbered sequence sections per feature. Class operations and interface names remain proposed design contracts for GrainLedger. No HomeCycle business feature or external integration is adopted. The example contains some duplicate subsection numbers; the revised document uses consecutive numbering.

## Revision 0.3 — repository context overrides earlier implementation assumptions

The full SRS/use-case target scope is retained. The repository supplies observed technology/module names, Minimal API/EF/Npgsql boundaries, typed CLR properties and source-registration versus accepted-arrival behavior. Earlier Clean Architecture package names in the presentation revision were proposed and are superseded by the current module package diagrams. Earlier source-baseline notes are context, not claims that all logical ERD entities are implemented CLR classes. See repository_context_vi.md for exact read-only sources and unresolved documentation alignment. Migration source presence is not evidence of deployment or test success in this session.
