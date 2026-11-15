# Architecture and access control

## Baseline

This implementation follows `Week1_report/GrainLedger_V2.pdf` (September 30, 2026) and the V2 role matrix in `GLM/03_Nguoi_dung_va_personas.md`. Those documents supersede earlier SRS role names: the mill owns the platform, and customer companies own their business data. Farmer accounts and buyer/auditor links are not internal roles in this baseline. The qualified ledger workflows below are implemented engineering defaults, not claims of factory or supervisor approval.

## Approved cutover target (deployment status unconfirmed)

The intended application is one ASP.NET Core project, `GrainLedger.Api`, with feature folders `Identity`, `Catalogs`, `Capture`, `Ledger`, and `Exports`. `Data/AppDbContext.cs` is the sole EF Core context and sets PostgreSQL default schema `grainledger`; one scoped context uses one shared restricted Npgsql data source. React remains separate. This is an approved target, not evidence that reset, migration, or deployment has occurred.

All application-owned database objects move into `grainledger` using deterministic names: `grainledger.<original-schema>_<original-object>`, with original tokens `identity`, `catalog`, `capture`, `ledger`, `exports`. This also applies to indexes, constraints, triggers and routines; oversized names use the approved SHA-256 suffix rule. Columns, enum values, permissions, API contracts and business behavior do not change because of renaming.

The approved `--seed-setup` command provisions accounts and mill setup only in Development; full demo data remains a separate explicit `--seed-ledger` choice. The selected initial data excludes demo policies/evidence and business workflows; migration-installed reference/configuration data is preserved. See deployment guide for exact counts and guardrails.

Source/stock workflows retain their operation-specific transaction boundaries. Capture and protected Ledger routines borrow the caller's connection and transaction; no request-wide transaction is introduced. Stock-changing workflows retain advisory lock `714928352` and `READ COMMITTED`; residue materialization retains read-only `REPEATABLE READ` followed by separate validated append.

Within `Features`, Capture groups farmer, season, spray declaration, procurement, sample, lab report and residue-evidence workflows; Catalogs groups commodities, reference data, MRLs, residue rules and blend assessment. Ledger groups endpoints, planning, residue and traceability; Exports groups orders and planning. Shared feature composition and persistence files remain at their feature root. Folder moves retain the existing feature namespaces; `Data/Migrations` and embedded `Data/Sql` remain unchanged.

Current Capture creation assigns farmer, plot, procurement and sample codes server-side; these four create requests require `payloadSchemaVersion: 2`, while other Capture operations retain their own version contracts. Procurement and sample codes include `YYYYMMDD` and independent continuous per-company/type counters. Stored codes remain unchanged. Ledger reserves source and processing lot codes; legacy processing drafts and unreserved source receipts fail closed. Migration and cutover require database-owner review and explicit shared-database approval; deployment status is unconfirmed. See [Capture code guide](guides/companion-capture-codes.md) and [lot-code guide](guides/unified-lot-code-guide.md).

## Historical module-era architecture snapshot

The following sections record behavior and deployment details from the prior multi-context/multi-schema implementation. Treat old schema and migration names as historical identifiers, not instructions for the approved single-schema baseline. Current reset/deployment status must be established independently.

### Historical module composition

New procurement registration creates durable source expectation only; no Ledger lot, position, posting or usable stock exists until a complete arrival is accepted. One procurement equals one complete arrival; use separate procurement per load. No truck model, customer settlement workflow, partial arrival or surplus acceptance. Assigned MillOperator records actual mass for every line in one complete set: exact match confirms and posts measured mass; a complete positive set with at least one shortage, every actual at or below expected, and every line within the owning company's effective receiving threshold posts measured actual mass as `RECEIVED_WITHIN_TOLERANCE`; any surplus or over-threshold shortage remains without stock for assigned MillManager review. Manager requires reason and owner/facility-scoped evidence: shortage approval posts actual mass only when no line exceeds expected and at least one is short; remeasurement appends new observations/request; keep-blocked leaves unresolved discrepancy without stock. Threshold escalates discrepancy only; it does not settle customer payments.

Historical procurement evidence and stock are grandfathered unchanged. `NULL` in `capture.procurements.receiving_workflow` marks a legacy committed row; new workflow marker is `PHYSICAL_ARRIVAL_V1`. Receiving migration does not retroactively freeze or reinterpret legacy stock. Retired `/api/capture-mock/*` routes, including `/api/capture-mock/status`, remain unavailable and return 404.

Migration order is Identity → Catalog → Capture → Ledger. Relevant forward migrations: Ledger `20261014000000_OptimizeLedgerReads` (read indexes), `20261014010000_UseOfficialCaptureBoundary`, `20261014020000_CanonicalCatalogCommodities`, `20261014190000_PhysicalReceiving`, `20261014200000_ManageLedgerRecords`, `20261014210000_PaginateLedgerLists`, `20261019000000_ReceivingLossThreshold` (`ReceivingLossThreshold.sql`); Catalog `20261014120000_AddCatalogAttributionAndCommodityArchive`; Capture `20261014170000_CaptureReferenceOwnership`. The threshold migration initializes 2% with migration-origin configuration history, leaves company overrides absent and historical measurement snapshots absent; it does not backfill policy or change legacy receiving state/stock. The read-index migration is separate from server-side paging; benchmark timings below measure indexed detail/history queries, not paginated collection throughput. Review migrations and back up before applying. Any shared Supabase migration, seed, schema/role/grant change or data-changing check requires explicit database-owner approval. Normal startup uses runtime connection only and does not migrate or seed; no threshold migration was applied to shared Supabase.

### Local and deployment database roles

Follow [local provisioning and deployment instructions](../README.md#run-locally). Fresh installs pre-create separate logins: migration owner has database `CONNECT, CREATE` and `public` schema `USAGE, CREATE`; runtime has `LOGIN`, database `CONNECT`, no elevated attributes and no owner-role membership. Identity's existing `public."__EFMigrationsHistory"` requires the `public` grants; each module retains its migration-history conventions. `Ledger:RuntimeRole` names the restricted grant target; `ConnectionStrings:GrainLedgerMigration` is required for `--migrate`, and `ConnectionStrings:GrainLedger` is runtime only.

Migration owner owns migrated objects and grants runtime access as object owner; no membership between migration and runtime roles is required. Runtime must not belong to an owner/elevated role. An existing database requires its current object owner as migration-only credential or a deliberate administrator-reviewed ownership migration, not an automatic ownership rewrite. A fresh non-owner login plus schema grants cannot alter existing owner tables/history. Production migration secrets belong only to the one-shot migration job; the app receives runtime secrets only. Runtime validation rejects superuser/elevated attributes or membership, module schema/table/database ownership or owner-role membership, direct Ledger mutation, Ledger schema creation and private generic posting access.

## Transaction and numeric contracts

All source/stock-changing commands acquire PostgreSQL transaction advisory lock `714928352` before source/stock rows. They require `READ COMMITTED`, so each statement sees current data after lock wait; other isolation levels fail closed. One global lock intentionally serializes stock operations and bounds throughput. Replace only if measured contention warrants ordered per-lot/location locks with equivalent race protection. Capture procurement and Ledger share this lock, open Npgsql connection and caller-owned transaction; module routines never commit that transaction.

Mass crosses API as decimal strings and is normalized to exact kg `numeric(20,6)`: `kg → quantity`, `g → quantity / 1000`, `t → quantity × 1000`. Original receipt quantity text/unit (`kg`, `g`, `t`) is retained, including leading zeroes. Original text allows at most six fractional digits, no exponent notation; source quantities must be positive and normalize without rounding to at most six kg fractional digits and maximum `99999999999999.999999` kg. Thus `0.001 g = 0.000001 kg` is valid; `0.0001 g` is rejected. No JS floating-point mass arithmetic. Coordinates are signed decimal strings to six places. Lot identities are UUIDs; human codes identify immutable provenance chains. A reviewed replacement in the same receipt/processing correction chain may retain its human code with a new lot UUID; unrelated duplicate codes are rejected.

Postings are immutable; protected routines update projections atomically. Processing variance is signed input − output − declared loss, distinct from stock and declared loss. Effective, evidence-backed tolerance policy is required even for exact balance; no configured policy means commit denied. Blend compatibility likewise requires explicit evidence-backed current rule. Lineage uses integer micro-kg largest-remainder allocation: each output is allocated across input lots, then each input share across its source contributions; ties use UUID order, equal sources combine, and contribution totals exactly equal output quantity.

Committed source expectations, arrival observations, manager decisions and postings remain immutable. New procurement starts pending and adds append-only actual-measurement and review evidence. Exact operator match or a complete positive within-threshold operator set posts measured actual mass; the latter records `RECEIVED_WITHIN_TOLERANCE` and snapshots applied threshold/source/revision. Surplus or over-threshold discrepancy stays pending without stock for review; manager-approved shortage posts actual mass. Remeasurement retains prior observations, while unresolved discrepancy posts no stock. Processing corrections append evidence-backed records and one-time reversal/replacement postings; they do not erase source or transfer history. Processing reconciliation binds current physical positions and the complete reviewed replacement version/content; editing replacement invalidates reconciliation. One procurement is one complete arrival, not a truck/load grouping; no customer settlement, partial arrival or surplus acceptance. Legacy rows with a NULL receiving marker and existing Ledger stock remain grandfathered unchanged.

## Roles and scope

| Role | Organization | Responsibility |
| --- | --- | --- |
| Administrator | Mill | Accounts, organizations, platform configuration, global trace read |
| MillManager | Mill | Assigned-facility operations, physical HOLD handling, variance and processing correction reconciliation |
| MillOperator | Mill | Assigned-facility complete arrival measurement, exact-match or within-threshold acceptance, operations and unchanged-lot transfers |
| CustomerManager | Customer | Own-company decisions and HOLD creation; no mill authority |
| ExportStaff | Customer | Own-company export planning (outside this ledger milestone) |
| PurchasingStaff | Customer | Own-company farmer records and purchasing |

No role comes from browser, Google domain or email. Only explicitly configured, verified first-administrator bootstrap is exception. Administrator is not wildcard: no processing confirmation, reconciliation, HOLD response or customer-business authority solely from administrator role. Every endpoint checks named persisted permission and resource organization/facility scope. Ownership and actor authority come from persisted state, not request-supplied `ownerId`, `confirmedBy` or organization IDs. Mill operational reads are facility-assignment scoped and omit farmer contacts, capture details, evidence/reconciliation secrets and unrelated customer data. Safe lookups expose only names/IDs permitted for task. Customer data is not protected by hiding React controls.

## Identity and authentication

Email/password API authentication is described in [the OTP/JWT and Scalar guide](guides/auth-email-password-scalar.md). It uses JWT access tokens plus rotating opaque refresh tokens, with server-side revocation. Successful Bearer authentication does not require CSRF. The React web app instead uses `/api/auth/web-login` with CSRF and the existing application session cookie; reload restores the user via `/api/auth/session`. Permission constants and role grants live in `Authorization/Permissions.cs`; `Domain/Roles.cs` retains role names and organization kinds. See [session and permission design](guides/identity-session-permissions.md).

Google OAuth uses the backend authorization-code flow with PKCE. Google credentials and provider tokens are not exposed to React or stored in local storage. A stable Google subject identifies the account; a different subject with the same email is not automatically linked to an existing account. Verified email is required for provisioning. Google and email/password browser sessions use HttpOnly, SameSite=Lax cookies and Secure in production; the database validates session activity, expiry and revocation on authenticated requests. Sessions expire after 15 minutes inactivity with sliding renewal on continued requests. Logout revokes current session; logout-all revokes all. Access changes and account disabling invalidate sessions.

Cookie validation renews both session clocks in one conditional database update bound to the claimed user and an active, unexpired, non-revoked session. Zero updated rows rejects and signs out the cookie; concurrent logout cannot be undone by renewal.

Cookie-authenticated mutating API calls require `X-CSRF-TOKEN`, fetched from `/api/auth/csrf` for the current identity. OAuth uses the authentication middleware's state/correlation protection. Development supports HTTP only on localhost; production uses HTTPS and an explicitly trusted proxy list.

## Implemented module workflow map
- Catalog and setup: `/api/ledger/setup`, `/api/ledger/lookups` plus Catalog APIs such as `/api/commodities` and `/api/measurement-units`; configuration requires persisted platform permissions.
- Equipment setup distinguishes unused-only removal from versioned `POST /api/ledger/equipment/{id}/retire`. Retirement retains IDs/content and historical references, rejects active drafts/pending corrections, and removes equipment from new-use lookups. Forward Ledger migration `20261023000000_RetireEquipment` wraps existing protected read/command routines, keeps predecessor/helpers private and preserves shared `READ COMMITTED` locking. Administrator permission, creator and row version remain required; no shared deployment was performed.
- Capture procurement: `POST /api/procurements` registers source expectation; no stock is created.
- Migration `20261014190000_PhysicalReceiving` stores immutable measurements/reviews and nullable `capture.procurements.receiving_workflow`; `NULL` is legacy committed procurement, while new workflows use `PHYSICAL_ARRIVAL_V1`.
- Receiving creates no stock at registration. Exact match posts actual mass as `RECEIVED_MATCHED`; a complete positive set with at least one strict shortage, every actual at or below expected and every line within the owning company's effective threshold posts actual mass as `RECEIVED_WITHIN_TOLERANCE`. Surplus or over-threshold shortage remains no-stock pending assigned-manager review; approved shortage posts actuals only, remeasurement appends, unresolved discrepancy stays blocked.
- `/api/ledger/receiving-thresholds` exposes Administrator configuration/history and persisted own-company effective read. Default starts at 2%; authorized administrators maintain optional company overrides. Configuration history is append-only; each new measurement set snapshots applied value, source, owning company and revision. Company roles see only own effective setting; no broad Setup access. Exact route/payload/version rules: [procurement API](api/procurement-api.md).
- Operations/stock: `/api/ledger/operations`, transfers, lots and history. Draft → optional manager evidence confirmation → operator commit; policy, stages, compatibility and stock eligibility checked server-side.
- Processing corrections/HOLD: `/api/ledger/corrections`, `/holds`, notifications. Processing correction request blocks outputs; manager reconciles current positions and bound replacement. HOLD never releases.
- Evidence upload/download is durable and immutable, authorized by current owner/facility scope. Operator-visible details are intentionally minimized.
- Capture `/api/samples` and `/api/lab-reports` preserve procurement-only sample custody and immutable company-scoped laboratory originals/results. Purchasing Staff collect and hand over samples; Customer Manager registers/confirms reports; Export Staff reads reports and supporting sample history. Confirmation appends verified sample associations without changing original evidence, stock, or HOLDs. Amendments, supplements and retests are linked new reports. Whole-file company-only attestation is required; mixed-company reports must be split/redacted before upload. See [laboratory API](api/sample-api.md#laboratory-report-routes).
- Capture migration `20261020000000_ExternalLaboratoryWorkflow` adds laboratory records and database immutability guards. Exact decimal strings retain reported values, LOD and LOQ without conversion. Confirmed reports may show the existing read-time numeric VN baseline against published rows; this is not a persisted legal assessment or lot residue interval. Missing limits/evidence remain explicitly unassessed. No automatic disposition route exists.
- Unconfirmed laboratory editing appends a replacement report with fresh section/result IDs; an append-only draft lifecycle record retires the previous version. Deletion appends a tombstone and hides the draft from the active queue, never deleting original files or observations. Confirmed reports cannot be retired or reopened. API transactions and database guards serialize retirement/confirmation under the existing advisory lock; exact-operation replay and current company/permission checks remain enforced. Draft revisions are separate from actual laboratory amendment/retest document relationships.

Route/DTO implementations live in `backend/src/GrainLedger.Api/Features/{Capture,Catalogs,Ledger}`; API contract errors use ProblemDetails. Historical synthetic source evidence is not converted or dropped automatically. Test-only cookie/auth/browser harness belongs to integration tests, not production bypass.

## Read scalability and tracing readiness

`20261014000000_OptimizeLedgerReads` is a forward migration adding indexes for HOLD lookup, reverse source contributions, event/posting lookup and originating-operation events. Single-lot history selects candidate event IDs before applying row-level visibility and facility filters. Ordering and operator redaction remain unchanged. No response cache or write-lock change is introduced.

Observed verification used disposable PostgreSQL 17.11 and the actual forward migration on populated databases, plus real HTTP requests through the restricted-runtime test host. Both 10k and 100k core-row fixtures passed exact before/after response, quantity, history, restriction and scope assertions. The larger fixture contained 100,010 lots, 115,020 events, 100,014 postings, 100,012 contribution rows and 10,006 receiving blocks. Warm median measurements:

| Path | Before | After |
| --- | ---: | ---: |
| Processed-lot detail HTTP | 147.763 ms | 10.070 ms |
| Single-lot history SQL | 150.876 ms | 2.215 ms |
| Reverse source-contribution SQL | 4.598 ms | 0.119 ms |

The reverse-source measurement is a database query, not a new tracing API. Timings exclude Supabase network latency, cold connections and whole-page rendering; they are not a concurrent-throughput guarantee. Seven indexes added approximately 19.8 MiB in the larger fixture; production migration locks and write-throughput overhead were not measured. The backend suite passed 119 tests, including two focused history regressions. The throwaway scale harness was removed after verification.

Source-lot detail (`GET /api/source-lots/{id}`) resolves provenance metadata in three database queries rather than seven: the company-scoped source lookup, a scoped item/header/event join, and one archived-aware farmer/plot/season projection. Plot and season IDs still come from the immutable procurement snapshot, not mutable plot-season references. Stock, receiving state, lineage, sample and report reads remain unchanged and uncached. An isolated PostgreSQL HTTP smoke confirmed three metadata commands, exact mass and cross-company 404 behavior; the 14 procurement integration cases passed, including archived labels and snapshot-reference regression coverage. This is a round-trip reduction, not a measured Supabase latency claim.

Farmer list projects scoped plot/procurement existence with each page instead of issuing two follow-up reads. Export order detail reads the current specification and allocated mass together, then its ordered limits; export history joins current actor names to the page and retains the unknown-user fallback. These reads remain uncached and company-scoped. Isolated PostgreSQL HTTP smoke covered session, farmer list, export detail/history and cross-company denial; 55 focused integration cases passed. No Supabase latency measurement is implied.

Ledger collection APIs for large queues now use bounded server-side filtering, scope, ordering and pagination before detail projection. Lists return `items`, `page`, `pageSize`, `totalCount`, `hasMore`; page size defaults to 25 and caps at 100. Search/sort and supported filters are role/scope checked; sort fields use per-list allowlists. Catalog/Capture lists are server-paged; basic Ledger setup endpoints return small arrays filtered, sorted and paged locally. Authorized pinned selections remain available without fetching entire lot collections. Pagers show `<< < 1 2 3 > >>`, at most five numeric page links, and accessible first/previous/next/last names. Legacy records with unknown creators are read-only; edits/deletes require current creator, current permission/scope and matching version. Operation drafts and replacement operation drafts may be edited; correction requests, committed records, postings and dependent history remain immutable. Soft-deleted setup/Capture resources stay persisted, are hidden from active lists/details/selectors/counts, reserve identifiers, and have no restore action. Stock ownership/scope filters and active-resource filters apply before paging. Large source fan-in/output counts and concurrent writes still need separate workload measurements; global stock-write lock remains intentional.

Proposal F18 (PDF pp. 5, 9) requires bad-test-to-container and container-to-plot tracing with complete affected sets and exact proportions. Root-source contribution rows support indexed mass attribution; residue calculations separately follow committed physical inputs and custody edges for residue intervals, without replacing chemical weights with rounded accounting shares. Procurement samples stay bound to their original source after processing; late evidence is consumed by a new on-demand evaluation, never copied into original facts or earlier snapshots. Market assessments, export and container-loading workflows remain separate. Current positive-ancestry HOLD propagation follows existing mixed-bulk/processing safeguards; residue calculations do not create or release HOLDs.
Lot tracing step A now exposes audited `POST /api/ledger/trace`: scoped forward/backward traversal of processing and custody relations, actual resolved processing inputs, current/history correction views, effective HOLDs, exact retained source weights and Capture-owned bulk immutable provenance. CustomerManager has both directions, ExportStaff only forward affected targets, and Administrator uses its explicit global trace grant without broader stock/business access. Graph/stock weights share one query snapshot; trace does not take the stock-write lock or mutate quantities. See [API contract](api/traceability-api.md) and [implementation/dependency plan](plans/traceability-implementation-plan.md). The UI can open source samples and confirmed report sections through the existing scoped, paged Capture APIs as a separate on-demand read. This does not put laboratory records in the material graph, implement plan/container actuals, or prove the full-season performance target.

## HTTP Identity contract

| Method | Path | Access / behavior |
| --- | --- | --- |
| GET | `/api/auth/google` | Begin Google sign-in |
| GET | `/signin-google` | Google middleware callback; not a React route |
| GET | `/api/auth/google/complete` | Consume external identity and create app session |
| GET | `/api/auth/csrf` | Get request token and anti-forgery cookie |
| GET | `/api/auth/session` | Current profile and permissions, or 401 |
| POST | `/api/auth/logout` | Revoke current session |
| POST | `/api/auth/logout-all` | Revoke all current user's sessions |
| GET / PUT | `/api/profile` | Read/update personal profile |
| GET | `/api/admin/users` | List accounts and access-edit versions |
| PUT | `/api/admin/users/{id}/access` | Change role, organization, active status; requires `If-Match` |
| GET / POST | `/api/admin/organizations` | List/create organizations |
| GET | `/api/admin/roles` | Valid roles, organization kinds, permissions |

Admin endpoints require `identity.manage`. Anonymous/expired gets 401; authenticated user lacking permission gets 403. Stale access version gets 412. Account access changes and audit records persist together; administrator safeguards prevent removing last active administrator.

## Explicit non-scope and fail-closed rules

No HOLD release, generic stock adjustment, transport shortage/gain settlement, byproduct dispatch/disposal, repeated drying/remilling/unpacking, export loading, or downstream receipt-correction cascade exists. No valid policy/compatibility means relevant processing action denied. Physical blending is recorded processing, not export compliance certification. These limits are safe implementation choices, not factory approval.

## Mixed-bulk storage (implemented locally; not deployed)

Ledger mixed-bulk storage is opt-in per location (`mixedBulk`, default `false`); location kind alone never enables pooling. Eligible kinds are `STORE`, `SILO`, and `COMPARTMENT`; `ISOLATION` is unsupported. Administrator creates an append-only evidence-backed policy at `POST /api/ledger/mixed-bulk-policies` with `id`, `locationId`, `ownerId`, `commodityId`, `stage`, `productIdentity`, `effectiveFrom`, `evidenceId`, and `identityScopeConfirmed: true`. Policy binds current location kind and exact identity tuple; no factory/default policy is seeded. Setup endpoints are `POST /api/ledger/locations` and `PUT /api/ledger/locations/{id}`; updates are version-checked.

Accepted arrivals, transfers, and processing outputs entering enabled storage create successor pool generations with monotonically increasing revisions; withdrawals create aliquot and exact-share remainder. Custody uses integer micro-kg largest-remainder allocation and UUID tie-breaks; each source complement and total conserve exactly. Inputs/transfer requests carry current `poolRevision`; stale pool generation returns conflict and caller must reload, reselect and resave. One global advisory lock `714928352` and caller-owned `READ COMMITTED` transaction serialize these writes. Reads expose `mixedBulk`, `isCurrentPool`, `poolRevision`, `sourceComposition` and `custodyLinks`; composition is attribution, not independently available stock. Procurement `currentStock` distinguishes `sourceLotId`, `currentStockLotId`, `onHandKg`, `availableKg`, whole `poolTotalKg`, `isMixedBulk` and `poolRevision`; source expected mass stays unchanged.

HOLD follows only positive source ancestry through custody and processing allocations. Later top-ups do not taint prior aliquots. Lab source associations remain original-source evidence and `NOT_ASSESSED`; confirmation does not change stock or HOLD. Multi-position ambiguous residual attribution, incompatible identity, absent policy, held ingress/top-up and unsupported custody corrections fail closed. Custody-derived corrections and downstream compensation are unsupported; no implicit unmix, cascade, or original untouched-package correction is enabled without approved policy. Full drain retains last identity until recorded clearance.

Migration `20261020000000_MixedBulkStorage` loads `MixedBulkStorage.sql`, `MixedBulkCommands.sql`, `MixedBulkSafety.sql`, and `MixedBulkReads.sql`. Forward migration only; no reset, backfill, or conversion. Runtime role receives no direct effective write access to private custody tables/helpers; migration grants must be reviewed with actual owner/runtime roles. Not deployed to shared Supabase or production. Explicit database-owner approval is REQUIRED before any shared migration, seed, grant or schema change.

## Residue bounds

Catalog owns reviewed, effective-dated whole-flow profiles and company settings; Capture owns source-group and report-selection evidence; Ledger reads physical ancestry, calculates exact intervals and stores immutable snapshots. No profile is seeded or automatically adopted.

Each `FLOW`, `schemaVersion: 2` profile covers one substance and commodity, with one residue definition, `mg/kg` as-received concentrations and shared precision. Its laboratory entries match exact analytical methods and supported result forms; its processing entries describe supported physical paths, explicit factors, conditions, semantics and reviewed homogeneous mixing. A profile may cover only source evidence; missing downstream steps remain unresolved. Step IDs distinguish components even when they share a profile version.

Company settings select `SYSTEM_DEFAULT` or a complete same-Code `COMPANY_OVERRIDE`. No setting means unresolved; an intended override never falls back to system policy. Evaluation resolves the recorded profile Code and effective version at each source/event time. It never borrows missing components from another Code; positive parents with different Codes remain unresolved. Provenance records profile version, setting and laboratory component or step ID.

Evaluation captures ancestry, policy and confirmed evidence in a read-only `REPEATABLE READ` transaction. A separate protected `READ COMMITTED` append rechecks actor/scope and persists the snapshot. Exact rational intermediates, positive physical mass weights and outward rounding preserve interval bounds. Missing method, result form, evidence, process coverage, compatible definition or finite parent bounds produces `UNRESOLVED` with null unknown endpoints. Cumulative factors rewind to their reviewed ancestor without double application; unsupported branching or mass changes remain unresolved.

Laboratory report identity and transcription confirmation remains independent of residue attestation. The confirmer must explicitly attest concentrations are as received for automatic binding; false or omitted attestation still permits identity confirmation but returns unresolved residue exceptions. Manual report review also requires explicit attestation before publishing. Reviewed normalization is required for nonblank raw uncertainty; relative uncertainty is an exact percentage and applies at zero only with explicit review. ND is not zero and missing thresholds are unresolved. Supported microgram-per-kilogram units normalize exactly to `mg/kg`; no moisture or residue-definition conversion is inferred.

Forward migration `20261104000000_WholeFlowResidueProfiles` replaces protected publication and evaluation routines without rewriting old immutable records. Active profile, setting and evidence readers exclude legacy contracts; old evaluation snapshots are excluded from new-engine history, old detail is not found and replay with an old evaluation ID conflicts. Existing laboratory originals remain usable after fresh reviewed schema-2 binding. No legacy conversion or supersession is implied. Migration `Down` is unsupported; recovery requires an approved backup/restore plan.

`residue.policy.manage` authorizes system profile publication; `residue.company-policy.manage` authorizes own-company profiles and settings; `residue.evidence.manage` authorizes own-company evidence; `residue.evaluate` grants scoped derived bounds/history, not original lab downloads. `/api/residue-rules` publishes and reads profiles; `/api/residue-policy-settings` manages adoption. Report confirmation and `/api/lab-reports/{reportId}/residue-review/preview` and `/confirm` bind compatible evidence. Live lot preview is read-only; locking a snapshot is explicit. Neither changes stock, HOLDs or legal limits.

Export market policy is separate: its reviewed `residueBasis` must be `AS_RECEIVED` for compatible residue coverage. Unsupported or missing market conventions remain incomplete; profile attestation does not replace market review.

## External authentication verification

Run a real Google consent flow using your project's client ID and secret, including cancellation, an unverified identity rejection, pending onboarding, first-admin onboarding, logout, and a disabled account. Automated tests substitute provider identities; they cannot verify your Google Cloud consent configuration. Future business modules still need resource-specific authorization tests when their endpoints are implemented. Buyer links, offline synchronization, and domain workflows are outside this authentication/profile slice.

## Historical Identity module layout

```text
GrainLedger.Modules.Identity/
├── Domain/                         # Entities, organization kinds and role definitions
├── Features/
│   ├── Authentication/             # Local/Google login, OTP, sessions and auth contracts
│   ├── Profile/                    # Profile endpoints and request contract
│   └── Administration/             # Organizations and user access management
├── Infrastructure/
│   ├── Authentication/             # JWT, cookie validation and CSRF filter
│   ├── Email/                      # SMTP sender, interface and delivery errors
│   └── Persistence/                # DbContext, DevelopmentSeed and Migrations/
├── Authorization/                  # Permission policies and current organization access
├── Common/                         # API errors, no-store filter and shared helpers
├── DependencyInjection.cs          # AddIdentityModule service registration
└── EndpointMapping.cs              # MapIdentityModule route composition
```

Types keep the `GrainLedger.Modules.Identity` namespace; generated migrations retain their original namespace and contents. Endpoint files share the existing partial `IdentityEndpoints` class so route handlers and shared mappings can move without changing the public API. Contracts live alongside their features. Folder reorganization does not require a database migration or rerunning the development seed.
