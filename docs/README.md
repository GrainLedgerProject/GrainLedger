# GrainLedger Documentation

Index of GrainLedger architecture, specifications, API routes, operational guides, and implementation plans.

## System Reference

- [Architecture and access control](architecture.md): module boundaries, authorization, and data rules
- [Setup and deployment](setup-and-deployment.md): local provisioning, configuration, migrations, seeds, and deployment
- [Diagram rendering guide](diagram-rendering.md): Mermaid, PlantUML, and draw.io rendering workflows
- [Database diagrams and data dictionary](dbdiagram/README.md): deployed-schema ERD, DBML, table descriptions, and data dictionary
- [Software Design Description (SDD)](SDD/README.md): system decomposition, class diagrams, sequence diagrams, and traceability

## API Specifications (`docs/api/`)

- [Catalog API](api/catalog-api.md): active substances, commodities, units, and system master data
- [Farmer API](api/farmer-api.md): farmer profiles, household registry, and farm registration
- [MRL API](api/mrl-api.md): maximum residue limit versions, draft limits, and publication lifecycle
- [Plot & Season API](api/plot-season-api.md): agricultural plots, season timelines, and geographic boundaries
- [Procurement API](api/procurement-api.md): purchasing registration, receiving measurements, and variance review
- [Sample & Laboratory API](api/sample-api.md): sample custody, laboratory reports, and residue evidence
- [Spray Declaration API](api/spray-declaration-api.md): farmer chemical application logging and spray windows
- [Traceability API](api/traceability-api.md): forward and backward lot tracing, custody graph, and audit records

## Guides & Walkthroughs (`docs/guides/`)

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
- [Container Loading Plan](plans/container-loading-plan.md): container stuffing, sealing, export dispatch, and seal verification
- [F07 Milestone Backlog](plans/f07-backlog.md): ledger processing milestone scope and remaining acceptance items
- [Laboratory Flow Plan](plans/lab-flow-plan.md): sample lifecycle, laboratory integration architecture, and policy gates
- [Ledger Implementation Plan](plans/ledger-implementation-plan.md): core ledger workflows, mass balance, stock postings, and audits
- [Mixed Bulk Storage Plan](plans/mixed-bulk-storage-plan.md): silo pooling, proportional source attribution, and custody accounting
- [Receiving Loss Threshold Plan](plans/receiving-loss-threshold-plan.md): configurable receiving variance thresholds and approval rules
- [Residue Bounds Plan](plans/residue-bounds-plan.md): substance matrix calculation, confidence intervals, and evaluation history
- [Traceability Implementation Plan](plans/traceability-implementation-plan.md): bidirectional graph traversal, custody tracking, and milestones
- [Unified Lot Code Implementation Plan](plans/unified-lot-code-implementation-plan.md): unified token format migration and deployment safety

## Reports & Reviews (`docs/reports/`)

- [Admin Capture Enhancements Report](reports/admin-capture-enhancements-report.md): implementation review and verification report
- [Authentication QA Report (2026-10-10)](reports/auth-qa-report-2026-10-10.md): auth flow audit, session management, and test evidence
- [Traceability Feasibility Review (2026-10-17)](reports/traceability-feasibility-review-2026-10-17.md): graph engine options and performance evaluation
