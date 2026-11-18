# GrainLedger Documentation

Index of GrainLedger architecture, specifications, API routes, operational guides, and implementation plans.

## System Reference

- [Architecture and access control](architecture.md): approved single-project/single-schema target, authorization, and historical architecture snapshot
- [Setup and deployment](setup-and-deployment.md): pending cutover target, local provisioning, restricted credentials, migrations, and setup/full-demo seeds
- [Diagram rendering guide](diagram-rendering.md): Mermaid, PlantUML, and draw.io rendering workflows
- [Database diagrams and data dictionary](dbdiagram/README.md): existing database-schema snapshot ERD, DBML, table descriptions, and data dictionary
- [Software Design Description (SDD)](SDD/README.md): system decomposition, class diagrams, sequence diagrams, and traceability

## API Specifications (`docs/api/`)

- [Container S2 workflow API](api/container-workflow-api.md): partial reviews/batches, multi-plan CLOSE, reviewed handoffs, HOLD notices and shipment records
- [Container production API](api/container-production-api.md): approved one-batch production, actual-output review and output reservation/loading
- [Container execution API](api/container-execution-api.md): approved direct-stock loading, HOLD, closure dossier and actual container tracing
- [Container preparation API](api/container-api.md): draft-only preparation; actual loading/CLOSE remain blocked
- [Catalog API](api/catalog-api.md): active substances, commodities, units, and system master data
- [Farmer API](api/farmer-api.md): farmer profiles, household registry, and farm registration
- [MRL API](api/mrl-api.md): maximum residue limit versions, draft limits, and publication lifecycle
- [Plot & Season API](api/plot-season-api.md): agricultural plots, season timelines, and geographic boundaries
- [Procurement API](api/procurement-api.md): purchasing registration, receiving measurements, and variance review
- [Sample & Laboratory API](api/sample-api.md): sample custody, laboratory reports, and residue evidence
- [Spray Declaration API](api/spray-declaration-api.md): farmer chemical application logging and spray windows
- [Traceability API](api/traceability-api.md): forward and backward lot tracing, custody graph, and audit records

## Guides & Walkthroughs (`docs/guides/`)

- [Demo container và tracing cho giảng viên](guides/container-tracing-lecturer-demo.md): one fixed seed/demo walkthrough using DEMO-CONTAINER/PADDY on Supabase, preselected stock and bidirectional tracing
- [Container S2 deployment](guides/container-s2-deployment.md) · [S2 web walkthrough](guides/container-s2-web-test.md)
- [Container production deployment](guides/container-production-deployment.md): migration/grant review, shared-environment approval and compatible-instance checklist
- [Container execution web tests](guides/container-execution-web-test.md): direct-stock loading/closure and trace/exception scenarios
- [Authentication & Scalar API Guide](guides/auth-email-password-scalar.md): email/password auth, JWT/refresh tokens, OTP, and Scalar UI
- [Companion Capture Codes](guides/companion-capture-codes.md): offline client synchronization codes and short token generation
- [Development Accounts & Demo Data](guides/development-accounts.md): seeded personas, test passwords, and demo mill stock
- [Identity & Session Permissions](guides/identity-session-permissions.md): role-based permissions, session lifetimes, and security controls
- [Laboratory Workflow Guide](guides/lab-flow-guide.md): sample collection, handover, external lab test intake, and report review
- [MRL Reference File Import](guides/mrl-file-import.md): substance import from CSV/Excel, draft limits, and import safeguards
- [Residue Bounds Manual Test Guide](guides/residue-bounds-manual-test-guide.md): end-to-end verification walkthrough for residue intervals
- [Residue Rules Entry Guide](guides/residue-rules-entry-guide.md): profile creation, rule definition, uncertainty, and processing factors
- [Synthetic Lab Report Guide](guides/synthetic-lab-report-guide.md): test report generation, sample matching, and PDF/CSV fixtures
- [Traceability Web Test Guide](guides/traceability-web-test-guide.md): step-by-step browser verification of lot genealogy and graph views
- [Traceability Web Walkthrough](guides/traceability-web-walkthrough.md): reference execution flows and graph traversal verification
- [Unified Lot Code Guide](guides/unified-lot-code-guide.md): standard lot identifier format, pool revisions, and barcode parsing

## Plans & Backlogs (`docs/plans/`)

- [Admin Capture Enhancements Plan](plans/admin-capture-enhancements-plan.md): administrative capture tooling, review queue, and bulk updates
- [Container tracing Flowchart plan](plans/container-tracing-flowchart-plan-2026-11-18.md): planned forward container nodes, backward graph from container detail and navigation; not implemented
- [Container S2 implementation plan](plans/container-s2-implementation-plan-2026-11-18.md): seven authorized requirement groups and focused verification
- [Container core completion plan (2026-11-17, scope updated 2026-11-18)](plans/container-core-completion-plan-2026-11-17.md): completed S1, requirement-mapped S2 tasks and excluded extensions; two implementation stages
- [Container execution plan](plans/container-execution-plan-2026-11-13.md): implemented DIRECT_STOCK baseline and deferred blend/output dependencies
- [Container completion review](reports/container-completion-review-2026-11-13.md): current post-merge gaps, questions and ordered acceptance plan
- [Container Loading Plan](plans/container-loading-plan.md): historical scope and requirements
- [Container implementation history](plans/container-loading-implementation-plan.md) · [previous execution plan](plans/container-loading-next-plan.md) · [integration checklist](plans/container-integration-handover.md)
- [F07 Milestone Backlog](plans/f07-backlog.md): ledger processing milestone scope and remaining acceptance items
- [Laboratory Flow Plan](plans/lab-flow-plan.md): sample lifecycle, laboratory integration architecture, and policy gates
- [Ledger Implementation Plan](plans/ledger-implementation-plan.md): core ledger workflows, mass balance, stock postings, and audits
- [Mixed Bulk Storage Plan](plans/mixed-bulk-storage-plan.md): silo pooling, proportional source attribution, and custody accounting
- [Receiving Loss Threshold Plan](plans/receiving-loss-threshold-plan.md): configurable receiving variance thresholds and approval rules
- [Residue Bounds Plan](plans/residue-bounds-plan.md): substance matrix calculation, confidence intervals, and evaluation history
- [Traceability Implementation Plan](plans/traceability-implementation-plan.md): bidirectional graph traversal, custody tracking, and milestones
- [Unified Lot Code Implementation Plan](plans/unified-lot-code-implementation-plan.md): unified token format migration and deployment safety

## Reports & Reviews (`docs/reports/`)

- [Container S2 shared deployment](reports/container-s2-deployment-2026-11-18.md): approved migration/single grant applied, preserved existing data/ACLs, runtime read checks and local-versus-Supabase fixture explanation
- [Container S2 additional browser walkthroughs](reports/container-s2-web-walkthroughs-2026-11-18.md): P1/P2 and pool/transfer/bagging through CLOSE, both PASS on fresh isolated fixtures
- [Container S2 implementation and verification](reports/container-s2-implementation-2026-11-18.md): actual test/browser results, 1,000-container benchmark, deployment status and retained limits
- [Container production S1 verification (2026-11-18)](reports/container-production-implementation-2026-11-18.md): implemented approved blend → output review/reservation → loading/CLOSE/trace; isolated tests/browser PASS
- [Container production deployment (2026-11-18)](reports/container-production-deployment-2026-11-18.md): approved shared migration and single runtime grant applied; backup, unchanged existing ACLs and read-only runtime verification

- [Container end-to-end business flow (2026-11-16)](reports/container-end-to-end-business-flow-2026-11-16.md): luồng chính từ export order qua duyệt, blend, bàn giao thành phẩm, loading, CLOSE và trace; ai làm và điểm cần nối tiếp
- [Container web test report (2026-11-16)](reports/container-web-test-report-2026-11-16.md): 30 nhóm ca manual với dữ liệu đầu vào, từng bước web, stock/reservation reconciliation, blockers, retry, scope và các nhánh chưa hỗ trợ; checklist, chưa phải kết quả nghiệm thu
- [Admin Capture Enhancements Report](reports/admin-capture-enhancements-report.md): implementation review and verification report
- [Authentication QA Report (2026-10-10)](reports/auth-qa-report-2026-10-10.md): auth flow audit, session management, and test evidence
- [Traceability Feasibility Review (2026-10-17)](reports/traceability-feasibility-review-2026-10-17.md): graph engine options and performance evaluation
