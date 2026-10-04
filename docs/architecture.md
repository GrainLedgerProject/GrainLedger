# Architecture and access control

## Baseline

This implementation follows `Week1_report/GrainLedger_V2.pdf` (September 30, 2026) and the V2 role matrix in `GLM/03_Nguoi_dung_va_personas.md`. Those documents supersede the earlier SRS role names: the mill owns the platform, and customer companies own their business data. Farmer accounts and buyer/auditor links are not internal roles in this baseline.

## One application, explicit modules

`GrainLedger.Api` is the composition root. It registers and maps the Identity class library. Identity owns the `identity` PostgreSQL schema, its EF Core context, migrations, session records, and audit records. React communicates with the same application origin using cookies. No service discovery, message broker, gateway, or distributed identity service is needed.

Future Capture, Ledger, Compliance, Orders, and Traceback modules should be class libraries loaded by this host, with their own tables and application interfaces. Cross-module operations use in-process interfaces. Do not expose another module's EF entities as its public API or add another independently deployed API project. Only the Identity module is implemented in this change; business permission names provide the foundation for future endpoints.

## Roles

| Role | Organization | Responsibility |
| --- | --- | --- |
| Administrator | Mill | Accounts, organizations, platform configuration, global trace read |
| MillManager | Mill | Mill operations, physical hold processing, reconciliation |
| MillOperator | Mill | Receiving, conversions, operational container processing |
| CustomerManager | Customer | Lab results, blend approval, hold decisions, own-company trace |
| ExportStaff | Customer | Export planning, blend proposals, containers, own-company trace |
| PurchasingStaff | Customer | Farmer records and purchasing |

No role is assigned by the browser, Google domain, or a user's email address. The sole exception is the explicitly configured, verified first-administrator bootstrap. Administrator is not a wildcard permission: it cannot approve blends or issue quality holds just because it can manage accounts.

Future business endpoints must check **both** the named permission and the resource's organization scope. Obtain organization ownership from persisted resource data, never trust a request's `organizationId` as proof of ownership. Use `ICurrentAccess` to check the signed-in user's current database access. `CanAccessOrganizationAsync` checks only the user's assigned organization, with no administrator bypass. A mill's operational role is not blanket permission to inspect every customer record: processing assignments and any deliberate global-read permission must be enforced by the relevant business module for that specific operation. Do not implement customer isolation by hiding React controls.

## Authentication

Google OAuth uses the backend authorization-code flow with PKCE. Google credentials and provider tokens are not exposed to React or stored in local storage. A stable Google subject identifies the account; a different subject with the same email is not automatically linked to an existing account. Verified email is required for provisioning.

The application cookie carries the account/session identifiers and is HttpOnly, SameSite=Lax, and Secure in production. The database validates activity, expiry, and revocation on authenticated requests. Sessions expire after 15 minutes of inactivity; cookie sliding renewal requires continued requests. Logout revokes the current session, and logout-all revokes all the user's sessions. Access changes and account disabling invalidate sessions.

Mutating API calls require `X-CSRF-TOKEN`, fetched from `/api/auth/csrf` for the current identity. OAuth uses the authentication middleware's state/correlation protection. Development supports HTTP only on localhost; production uses HTTPS and an explicitly trusted proxy list.

## HTTP contract

| Method | Path | Access / behavior |
| --- | --- | --- |
| GET | `/api/auth/google` | Begin Google sign-in |
| GET | `/signin-google` | Google middleware callback; not a React route |
| GET | `/api/auth/google/complete` | Consume external identity and create app session |
| GET | `/api/auth/csrf` | Get a request token and anti-forgery cookie |
| GET | `/api/auth/session` | Current profile and permissions, or 401 |
| POST | `/api/auth/logout` | Revoke current session |
| POST | `/api/auth/logout-all` | Revoke all current user's sessions |
| GET / PUT | `/api/profile` | Read/update personal profile |
| GET | `/api/admin/users` | List accounts and access-edit versions |
| PUT | `/api/admin/users/{id}/access` | Change role, organization, active status; requires `If-Match` |
| GET / POST | `/api/admin/organizations` | List/create organizations |
| GET | `/api/admin/roles` | Valid roles, organization kinds, permissions |

Admin endpoints require `identity.manage`. An authenticated user lacking permission receives 403; an anonymous or expired session receives 401. Access updates with a stale version receive 412. The React interface tells the administrator to reload before retrying. Account access changes and audit records are persisted together, and administrator safeguards prevent removing the last active administrator.

## Remaining integration work

Run a real Google consent flow using your project's client ID and secret, including cancellation, an unverified identity rejection, pending onboarding, first-admin onboarding, logout, and a disabled account. Automated tests substitute provider identities; they cannot verify your Google Cloud consent configuration. Future business modules still need resource-specific authorization tests when their endpoints are implemented. Buyer links, offline synchronization, and domain workflows are outside this authentication/profile slice.
