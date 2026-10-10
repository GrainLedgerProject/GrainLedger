# Architecture and access control

## Baseline

This implementation follows `Week1_report/GrainLedger_V2.pdf` (September 30, 2026) and the V2 role matrix in `GLM/03_Nguoi_dung_va_personas.md`. Those documents supersede earlier SRS role names: the mill owns the platform, and customer companies own their business data. Farmer accounts and buyer/auditor links are not internal roles in this baseline. The qualified ledger workflows below are implemented engineering defaults, not claims of factory or supervisor approval.

## One application, explicit modules

`GrainLedger.Api` is the composition root. Identity owns the `identity` schema, EF context, migrations, sessions and audit records. Ledger owns protected stock, operation, posting, lineage, evidence, correction, HOLD, notification and history commands in `ledger`. Capture owns source provenance, receipts, receiving observations, receipt corrections and persisted command records in `capture`. React communicates with this same-origin host using cookies.

The modules are class libraries in one process. Each owns its tables and migrations; cross-module calls use in-process contracts and share a single Npgsql connection/transaction for source/stock changes. No EF entities cross module boundaries. Receipt source records, lot credit, postings and package guards commit atomically. Separate durable evidence upload has its own Ledger transaction. No broker, gateway, service discovery or independently deployed business API is required.

Host startup maps Identity, Ledger and Capture. Explicit `--migrate` applies Identity, Ledger, then Capture migrations using the migration-owner connection, then configures restricted runtime grants. Normal startup uses only the runtime connection and rejects superuser, elevated role membership, schema/table/database ownership, unsafe Ledger write privileges or schema creation. Runtime can read Ledger tables and call only finite protected commands; it cannot directly mutate Ledger tables or invoke generic posting. Identity and Capture retain required runtime table privileges. Database-owned SECURITY DEFINER routines use fixed search paths and revoked PUBLIC execution; protected constraints and triggers enforce complete durable packages, immutability and accounting invariants independently of API authorization.

### Local and deployment database roles

Follow [local provisioning and deployment instructions](../README.md#run-locally). Fresh installs pre-create separate logins: migration owner has database `CONNECT, CREATE` and `public` schema `USAGE, CREATE`; runtime has `LOGIN`, database `CONNECT`, no elevated attributes and no owner-role membership. Identity's existing `public."__EFMigrationsHistory"` requires the `public` grants; Ledger/Capture use their own history schemas. `Ledger:RuntimeRole` names the restricted grant target; `ConnectionStrings:GrainLedgerMigration` is required for `--migrate`, and `ConnectionStrings:GrainLedger` is runtime only.

Migration owner owns migrated objects and grants runtime access as object owner; no membership between migration and runtime roles is required. Runtime must not belong to an owner/elevated role. An existing database requires its current object owner as migration-only credential or a deliberate administrator-reviewed ownership migration, not an automatic ownership rewrite. A fresh non-owner login plus schema grants cannot alter existing owner tables/history. Production migration secrets belong only to the one-shot migration job; the app receives runtime secrets only. Runtime validation rejects superuser/elevated attributes or membership, module schema/table/database ownership or owner-role membership, direct Ledger mutation, Ledger schema creation and private generic posting access.

## Transaction and numeric contracts

All source/stock-changing commands acquire PostgreSQL transaction advisory lock `714928352` before source/stock rows. They require `READ COMMITTED`, so each statement sees current data after lock wait; other isolation levels fail closed. One global lock intentionally serializes stock operations and bounds throughput. Replace only if measured contention warrants ordered per-lot/location locks with equivalent race protection. Capture must acquire same lock before reading/writing source rows and pass its open connection/transaction into Ledger; module routines never commit the caller's transaction.

Mass crosses API as decimal strings and is normalized to exact kg `numeric(20,6)`: `kg → quantity`, `g → quantity / 1000`, `t → quantity × 1000`. Original receipt quantity text/unit (`kg`, `g`, `t`) is retained, including leading zeroes. Original text allows at most six fractional digits, no exponent notation; source quantities must be positive and normalize without rounding to at most six kg fractional digits and maximum `99999999999999.999999` kg. Thus `0.001 g = 0.000001 kg` is valid; `0.0001 g` is rejected. No JS floating-point mass arithmetic. Coordinates are signed decimal strings to six places. Lot identities are UUIDs; human codes identify immutable provenance chains. A reviewed replacement in the same receipt/processing correction chain may retain its human code with a new lot UUID; unrelated duplicate codes are rejected.

Postings are immutable; protected routines update projections atomically. Processing variance is signed input − output − declared loss, distinct from stock and declared loss. Effective, evidence-backed tolerance policy is required even for exact balance; no configured policy means commit denied. Blend compatibility likewise requires explicit evidence-backed current rule. Lineage uses integer micro-kg largest-remainder allocation: each output is allocated across input lots, then each input share across its source contributions; ties use UUID order, equal sources combine, and contribution totals exactly equal output quantity.

Committed source receipt/header/line records, original arrival observations, correction/investigation records, evidence bytes and postings remain immutable. Editable drafts have no stock effect. Corrections append evidence-backed records and one-time reversal/replacement postings; they do not erase source or transfer history. Processing reconciliation binds current physical positions and the complete reviewed replacement version/content; editing the replacement invalidates reconciliation. Receipt replacement preserves owner/location/farmer/season/ordered source identities with fresh receipt/line/lot UUIDs. Arrival correction retains the original observation, clears only an eligible receiving restriction and never changes procurement stock or releases HOLD. A `REAL_DIFFERENCE` diagnosis stays blocked.

## Roles and scope

| Role | Organization | Responsibility |
| --- | --- | --- |
| Administrator | Mill | Accounts, organizations, platform configuration, global trace read |
| MillManager | Mill | Assigned-facility operations, physical HOLD handling, variance and correction/receiving reconciliation |
| MillOperator | Mill | Assigned-facility receiving, conversions and operational stock tasks |
| CustomerManager | Customer | Own-company decisions and HOLD creation; no mill authority |
| ExportStaff | Customer | Own-company export planning (outside this ledger milestone) |
| PurchasingStaff | Customer | Own-company farmer records and purchasing |

No role comes from browser, Google domain or email. Only explicitly configured, verified first-administrator bootstrap is exception. Administrator is not wildcard: no processing confirmation, reconciliation, HOLD response or customer-business authority solely from administrator role. Every endpoint checks named persisted permission and resource organization/facility scope. Ownership and actor authority come from persisted state, not request-supplied `ownerId`, `confirmedBy` or organization IDs. Mill operational reads are facility-assignment scoped and omit farmer contacts, capture details, evidence/reconciliation secrets and unrelated customer data. Safe lookups expose only names/IDs permitted for task. Customer data is not protected by hiding React controls.

## Identity and authentication

Email/password API authentication is described in [the OTP/JWT and Scalar guide](auth-email-password-scalar.md). It uses JWT access tokens plus rotating opaque refresh tokens, with server-side revocation. Successful Bearer authentication does not require CSRF.

Google OAuth uses the backend authorization-code flow with PKCE. Google credentials and provider tokens are not exposed to React or stored in local storage. A stable Google subject identifies the account; a different subject with the same email is not automatically linked to an existing account. Verified email is required for provisioning. Google browser sessions use HttpOnly, SameSite=Lax cookies and Secure in production; the database validates session activity, expiry and revocation on authenticated requests. Sessions expire after 15 minutes inactivity with sliding renewal on continued requests. Logout revokes current session; logout-all revokes all. Access changes and account disabling invalidate sessions.

Cookie-authenticated mutating API calls require `X-CSRF-TOKEN`, fetched from `/api/auth/csrf` for the current identity. OAuth uses the authentication middleware's state/correlation protection. Development supports HTTP only on localhost; production uses HTTPS and an explicitly trusted proxy list.

## Implemented module workflow map

- Setup and lookups: `/api/ledger/setup`, `/api/ledger/lookups`; facilities, locations, equipment, commodities, assignments, evidence-backed policies and compatibility; occupancy clearance is explicit.
- Source capture: `/api/capture/farmers`, `/plots`, `/seasons`, `/plot-seasons`, `/receipts`; draft has no stock effect, commit creates provenance-linked paddy lots/postings atomically.
- Receiving: `/arrivals` records original measurement/evidence as Mill Operator with `lots.receive`; manager `receiving.reconcile` appends corrections/investigations. Mismatch blocks source and descendants; only exact corrected measure/proven procurement correction resolves eligible restriction. Real transport difference stays blocked.
- Operations/stock: `/api/ledger/operations`, transfers, lots and history. Draft → optional manager evidence confirmation → operator commit; policy, stages, compatibility and stock eligibility checked server-side.
- Corrections/HOLD: `/api/ledger/corrections`, `/holds`, notifications. Processing correction request blocks outputs; manager reconciles current positions and bound replacement. Receipt physical reconciliation and replacement commit require manager `corrections.reconcile`. Transfer history remains explicit. HOLD never releases.
- Evidence upload/download is durable and immutable, authorized by current owner/facility scope. Operator-visible details are intentionally minimized.

Route/DTO implementations live in the [Ledger module](../backend/src/GrainLedger.Modules.Ledger/) and [Capture module](../backend/src/GrainLedger.Modules.Capture/); API contract errors use ProblemDetails. Test-only cookie/auth/browser harness belongs to integration tests, not production bypass.

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
