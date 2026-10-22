# Architecture and access control

## Baseline

This implementation follows `Week1_report/GrainLedger_V2.pdf` (September 30, 2026) and the V2 role matrix in `GLM/03_Nguoi_dung_va_personas.md`. Those documents supersede earlier SRS role names: the mill owns the platform, and customer companies own their business data. Farmer accounts and buyer/auditor links are not internal roles in this baseline. The qualified ledger workflows below are implemented engineering defaults, not claims of factory or supervisor approval.

## One application, explicit modules

`GrainLedger.Api` is the composition root. Identity owns the `identity` schema, Catalog owns catalog data, Capture owns farmer/procurement records, samples and external laboratory reports in `capture`, and Ledger owns protected stock, operations, postings, lineage, facility evidence, processing corrections, HOLD, notifications and history in `ledger`. React uses this same-origin host with HttpOnly session cookies; API clients can use Bearer access tokens.

The modules are class libraries in one process. Each owns its tables and migrations; cross-module calls use in-process contracts and share one Npgsql connection/transaction for source/stock changes. No EF entities cross module boundaries. Procurement registration writes source only; accepted arrival measurement and Ledger lot/posting are atomic through the registered Ledger port. Source registration, operator measurement, manager review and accepted posting acquire PostgreSQL transaction advisory lock `714928352` before source/stock reads at `READ COMMITTED`. No broker, gateway, service discovery or independently deployed business API is required.

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

Email/password API authentication is described in [the OTP/JWT and Scalar guide](auth-email-password-scalar.md). It uses JWT access tokens plus rotating opaque refresh tokens, with server-side revocation. Successful Bearer authentication does not require CSRF. The React web app instead uses `/api/auth/web-login` with CSRF and the existing application session cookie; reload restores the user via `/api/auth/session`. Permission constants and role grants live in `Authorization/Permissions.cs`; `Domain/Roles.cs` retains role names and organization kinds. See [session and permission design](identity-session-permissions.md).

Google OAuth uses the backend authorization-code flow with PKCE. Google credentials and provider tokens are not exposed to React or stored in local storage. A stable Google subject identifies the account; a different subject with the same email is not automatically linked to an existing account. Verified email is required for provisioning. Google and email/password browser sessions use HttpOnly, SameSite=Lax cookies and Secure in production; the database validates session activity, expiry and revocation on authenticated requests. Sessions expire after 15 minutes inactivity with sliding renewal on continued requests. Logout revokes current session; logout-all revokes all. Access changes and account disabling invalidate sessions.

Cookie-authenticated mutating API calls require `X-CSRF-TOKEN`, fetched from `/api/auth/csrf` for the current identity. OAuth uses the authentication middleware's state/correlation protection. Development supports HTTP only on localhost; production uses HTTPS and an explicitly trusted proxy list.

## Implemented module workflow map
- Catalog and setup: `/api/ledger/setup`, `/api/ledger/lookups` plus Catalog APIs such as `/api/commodities` and `/api/measurement-units`; configuration requires persisted platform permissions.
- Capture procurement: `POST /api/procurements` registers source expectation; no stock is created.
- Migration `20261014190000_PhysicalReceiving` stores immutable measurements/reviews and nullable `capture.procurements.receiving_workflow`; `NULL` is legacy committed procurement, while new workflows use `PHYSICAL_ARRIVAL_V1`.
- Receiving creates no stock at registration. Exact match posts actual mass as `RECEIVED_MATCHED`; a complete positive set with at least one strict shortage, every actual at or below expected and every line within the owning company's effective threshold posts actual mass as `RECEIVED_WITHIN_TOLERANCE`. Surplus or over-threshold shortage remains no-stock pending assigned-manager review; approved shortage posts actuals only, remeasurement appends, unresolved discrepancy stays blocked.
- `/api/ledger/receiving-thresholds` exposes Administrator configuration/history and persisted own-company effective read. Default starts at 2%; authorized administrators maintain optional company overrides. Configuration history is append-only; each new measurement set snapshots applied value, source, owning company and revision. Company roles see only own effective setting; no broad Setup access. Exact route/payload/version rules: [procurement API](procurement-api.md).
- Operations/stock: `/api/ledger/operations`, transfers, lots and history. Draft → optional manager evidence confirmation → operator commit; policy, stages, compatibility and stock eligibility checked server-side.
- Processing corrections/HOLD: `/api/ledger/corrections`, `/holds`, notifications. Processing correction request blocks outputs; manager reconciles current positions and bound replacement. HOLD never releases.
- Evidence upload/download is durable and immutable, authorized by current owner/facility scope. Operator-visible details are intentionally minimized.
- Capture `/api/samples` and `/api/lab-reports` preserve procurement-only sample custody and immutable company-scoped laboratory originals/results. Purchasing Staff collect and hand over samples; Customer Manager registers/confirms reports; Export Staff reads reports and supporting sample history. Confirmation appends verified sample associations without changing original evidence, stock, or HOLDs. Amendments, supplements and retests are linked new reports. Whole-file company-only attestation is required; mixed-company reports must be split/redacted before upload. See [laboratory API](sample-api.md#laboratory-report-routes).
- Capture migration `20261020000000_ExternalLaboratoryWorkflow` adds laboratory records and database immutability guards. Exact decimal strings retain reported values, LOD and LOQ without conversion. Confirmed reports may show the existing read-time numeric VN baseline against published rows; this is not a persisted legal assessment or F12 interval. Missing limits/evidence remain explicitly unassessed. No automatic disposition route exists.

Route/DTO implementations live in [Capture](../backend/src/GrainLedger.Modules.Capture/), [Catalog](../backend/src/GrainLedger.Modules.Catalogs/) and [Ledger](../backend/src/GrainLedger.Modules.Ledger/); API contract errors use ProblemDetails. Historical synthetic source evidence is not converted or dropped automatically. Test-only cookie/auth/browser harness belongs to integration tests, not production bypass.

## Read scalability and tracing readiness

`20261014000000_OptimizeLedgerReads` is a forward migration adding indexes for HOLD lookup, reverse source contributions, event/posting lookup and originating-operation events. Single-lot history selects candidate event IDs before applying row-level visibility and facility filters. Ordering and operator redaction remain unchanged. No response cache or write-lock change is introduced.

Observed verification used disposable PostgreSQL 17.11 and the actual forward migration on populated databases, plus real HTTP requests through the restricted-runtime test host. Both 10k and 100k core-row fixtures passed exact before/after response, quantity, history, restriction and scope assertions. The larger fixture contained 100,010 lots, 115,020 events, 100,014 postings, 100,012 contribution rows and 10,006 receiving blocks. Warm median measurements:

| Path | Before | After |
| --- | ---: | ---: |
| Processed-lot detail HTTP | 147.763 ms | 10.070 ms |
| Single-lot history SQL | 150.876 ms | 2.215 ms |
| Reverse source-contribution SQL | 4.598 ms | 0.119 ms |

The reverse-source measurement is a database query, not a new tracing API. Timings exclude Supabase network latency, cold connections and whole-page rendering; they are not a concurrent-throughput guarantee. Seven indexes added approximately 19.8 MiB in the larger fixture; production migration locks and write-throughput overhead were not measured. The backend suite passed 119 tests, including two focused history regressions. The throwaway scale harness was removed after verification.

Ledger collection APIs for large queues now use bounded server-side filtering, scope, ordering and pagination before detail projection. Lists return `items`, `page`, `pageSize`, `totalCount`, `hasMore`; page size defaults to 25 and caps at 100. Search/sort and supported filters are role/scope checked; sort fields use per-list allowlists. Catalog/Capture lists are server-paged; basic Ledger setup endpoints return small arrays filtered, sorted and paged locally. Authorized pinned selections remain available without fetching entire lot collections. Pagers show `<< < 1 2 3 > >>`, at most five numeric page links, and accessible first/previous/next/last names. Legacy records with unknown creators are read-only; edits/deletes require current creator, current permission/scope and matching version. Operation drafts and replacement operation drafts may be edited; correction requests, committed records, postings and dependent history remain immutable. Soft-deleted setup/Capture resources stay persisted, are hidden from active lists/details/selectors/counts, reserve identifiers, and have no restore action. Stock ownership/scope filters and active-resource filters apply before paging. Large source fan-in/output counts and concurrent writes still need separate workload measurements; global stock-write lock remains intentional.

Proposal F18 (PDF pp. 5, 9) requires bad-test-to-container and container-to-plot tracing with complete affected sets and exact proportions. Root-source contribution rows support indexed mass attribution; F12 separately follows committed physical inputs and custody edges for residue intervals, without replacing chemical weights with rounded accounting shares. Procurement samples stay bound to their original source after processing; late evidence is consumed by a new on-demand evaluation, never copied into original facts or earlier snapshots. Market assessments, export and container-loading workflows remain separate. Current positive-ancestry HOLD propagation follows existing mixed-bulk/processing safeguards; F12 does not create or release HOLDs.

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

## F12 residue bounds (implemented locally; not deployed)

Catalog owns reviewed, effective-dated interpretation/process versions; Capture owns company-scoped explicit source-group/fallback and reviewed result-selection evidence; Ledger owns physical ancestry reads, exact request-local calculation and immutable evaluation snapshots. SQL-owned payload tables are installed by Catalog `20261022000000_ReviewedResidueRules`, Capture `20261022000000_ReviewedResidueEvidence`, and Ledger `20261022030000_ResidueBounds`. No operational scientific rules or coefficients are seeded.

An evaluation captures ancestry, policies and confirmed evidence on one shared connection in a read-only `REPEATABLE READ` transaction. Once materialized, that transaction closes. A separate protected `READ COMMITTED` append acquires the existing authorization lock, rechecks current actor/scope and persists the captured result with both snapshot and persistence times. Stock/source write commands retain their existing isolation and locking contract. Concurrent publications cannot create hybrid evidence, and a historical snapshot never masquerades as fresh calculation.

The calculator retains exact rational intermediates, committed physical mass weights, path-specific factors, exhausted ancestors and positive dependencies even when accounting attribution rounds to zero. Final endpoints round outward under explicit reviewed precision. Missing/incompatible interpretation, unit, material basis, process conditions/factors, finite parent bounds or evidence coverage yields per-substance `UNRESOLVED` with null unknown endpoints. Cumulative unbranched rewind avoids double factors; changed-mass-basis mixture rewind is unresolved rather than guessed. Original corrected lots retain original ancestry, replacement material its own; downstream correction restrictions are unchanged.

`residue.policy.manage` belongs to Administrator; `residue.evidence.manage` to own-company Customer Manager. `residue.evaluate` grants own-company Customer Manager/Export Staff and assigned-facility Mill Manager/Mill Operator derived calculation/history access. It does not grant access to original laboratory reports. Role authorization records reviewed decisions, not scientific approval. Numeric output still needs real reviewed sources/applicability; no MRL fallback, ND=0, generic drying factor or global PASS exists.

Existing lot detail exposes Calculate/Recalculate and historical inspection; existing Catalog/Capture screens expose reviewed policy/evidence maintenance. Full route, publication, precedence, precision, deployment and isolated verification contracts: [F12 plan and delivered API](f12-residue-bounds-plan.md#9-delivered-f12-contracts). No evaluation changes stock, HOLDs, original reports or legal limits. This is not market certification or shared-database deployment.

## External authentication verification

Run a real Google consent flow using your project's client ID and secret, including cancellation, an unverified identity rejection, pending onboarding, first-admin onboarding, logout, and a disabled account. Automated tests substitute provider identities; they cannot verify your Google Cloud consent configuration. Future business modules still need resource-specific authorization tests when their endpoints are implemented. Buyer links, offline synchronization, and domain workflows are outside this authentication/profile slice.

## Identity module layout

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
