# Setup and deployment

Use this guide for local provisioning, application configuration, explicit database changes, and production deployment. Routine startup uses the restricted runtime database login and does not migrate or seed.

## Prerequisites

- .NET SDK 10 (repository `global.json` selects the SDK), Node.js 24, npm.
- PostgreSQL for the application. Docker is optional when using another PostgreSQL instance; Compose provides a local PostgreSQL service.
- Google OAuth Web client for Google sign-in. A real SMTP provider is required for email verification and password recovery.

From repository root, preserve any existing `.env`. If none exists, copy `.env.example` to `.env` and fill only the values needed for your setup. Do not overwrite existing connection strings, reset database volumes, or expose secrets. Backend loads root `.env` in Development; process environment variables override `.env`, and command-line configuration has higher precedence. Tests use isolated configuration. Never expose backend secrets through `VITE_` variables or commit `.env`.

Single quotes in `.env` preserve literal `$` and `#`. Restart backend after changing configuration.

## Fresh local Compose database

This section provisions a **new, local-only** Compose database. Do not run its role-creation SQL against an existing, teammate-owned, or shared database. Existing databases need their existing object-owner credential for migrations; grants do not make a new login owner of existing objects. Do not rewrite ownership or reset a volume implicitly. For any shared database, obtain explicit database-owner approval before migrations, seeds, role/grant/schema changes, or data-changing verification.

1. Set a private local `POSTGRES_PASSWORD` in `.env` matching neither app login. Start PostgreSQL:

   ```bash
   docker compose up -d --wait postgres
   ```

2. Create separate runtime and migration logins. Replace SQL password placeholders with private local values. Compose's `grainledger` account is privileged local database administration only; do not use it as either application connection.

   ```bash
   docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U grainledger -d grainledger <<'SQL'
   CREATE ROLE grainledger_runtime LOGIN PASSWORD 'replace-with-runtime-password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;
   CREATE ROLE grainledger_migration LOGIN PASSWORD 'replace-with-migration-password' NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOBYPASSRLS;
   GRANT CONNECT ON DATABASE grainledger TO grainledger_runtime;
   GRANT CONNECT, CREATE ON DATABASE grainledger TO grainledger_migration;
   GRANT USAGE, CREATE ON SCHEMA public TO grainledger_migration;
   SQL
   ```

   Migration owner needs database `CREATE` to create module schemas and `public` `USAGE, CREATE` for Identity's EF migration-history table. The migration owner owns migrated objects and can grant runtime access. No membership between these roles is needed. Runtime must not be superuser, own database/module schemas/tables, or belong to owner/elevated roles.

3. Set root `.env` connection strings for this fresh Compose database (port `5433`) and restricted role:

   ```dotenv
   ConnectionStrings__GrainLedgerMigration='Host=localhost;Port=5433;Database=grainledger;Username=grainledger_migration;Password=replace-with-migration-password'
   ConnectionStrings__GrainLedger='Host=localhost;Port=5433;Database=grainledger;Username=grainledger_runtime;Password=replace-with-runtime-password'
   Ledger__RuntimeRole='grainledger_runtime'
   ```

   These example connections are for the new local Compose database only. For an existing database, retain its current connection settings and ask its owner for the intended migration procedure. Runtime role must remain restricted; startup rejects elevated or owner credentials.

## Authentication and application configuration

Set only features you intend to use. Key names below correspond to `.env.example`:

| Key | Purpose |
| --- | --- |
| `ConnectionStrings__GrainLedger` | Restricted runtime PostgreSQL login |
| `ConnectionStrings__GrainLedgerMigration` | Existing object owner / migration owner; explicit migration job only |
| `Ledger__RuntimeRole` | Pre-created restricted role receiving grants during explicit migration setup; configure for the runtime connection login |
| `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` | Google OAuth Web client credentials |
| `Bootstrap__AdminEmail` | Verified email allowed to receive first administrator role |
| `Authentication__Jwt__SigningKey` | Secret signing key for email/password API tokens; use a random value of at least 32 characters and keep stable across restarts |
| `Authentication__Jwt__Issuer`, `Authentication__Jwt__Audience` | JWT issuer and audience; defaults shown in `.env.example` are `GrainLedger` and `GrainLedger.Api` |
| `Mail__FromAddress`, `Mail__FromName` | Approved sender address and display name |
| `Mail__Smtp__Host`, `Mail__Smtp__Port`, `Mail__Smtp__Username`, `Mail__Smtp__Password`, `Mail__Smtp__UseStartTls` | SMTP provider connection; port 587 commonly uses STARTTLS, port 465 commonly uses implicit TLS (`UseStartTls=false`) |
| `DevelopmentSeed__Password` | Shared password for explicit Development seed accounts; never configure/use for Production |
| `ReverseProxy__KnownProxies__0` | Trusted reverse proxy IP; add indexed entries for additional trusted proxies |
| `DataProtection__KeysPath` | Persistent protected ASP.NET DataProtection key directory |
| `AllowedHosts` | Allowed public host name(s) |

See [email/password, OTP, and Scalar guide](guides/auth-email-password-scalar.md) for JWT and SMTP setup, OTP behavior, registration, and API login. Missing SMTP does not enable a bypass or default OTP; email-dependent flows need real SMTP. API Bearer tokens and React's CSRF-protected cookie session are distinct authentication flows.

For Google sign-in, create a **Web application** OAuth client, configure consent and test users as needed, then register exact local redirect URI `http://localhost:5173/signin-google`. Vite proxies `/api` and `/signin-google` to backend `http://localhost:5080`; use `localhost` consistently rather than `127.0.0.1`. Production callback is `https://YOUR_HOST/signin-google`. Follow [Google authentication for ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0).

### Administrator bootstrap

Set `Bootstrap__AdminEmail` before first onboarding. If no administrator exists, the matching newly provisioned Google account or email/password account verified by OTP can receive the first administrator role. After bootstrap, clear the setting and restart backend. Existing administrators retain access. Other verified sign-ups receive no business permissions until administrator assigns organization and role. See [architecture and access control](architecture.md) and [authentication guide](guides/auth-email-password-scalar.md).

## Explicit migrations and Development seeds

Migration and seed commands mutate their target database. First verify both connection strings point to the intended Development database that you own and control. Development environment alone does not prove database isolation. Never target production or a teammate/shared Supabase database without explicit database-owner approval.

Run migrations only when deliberately installing/updating schema. This updates Identity → Catalog → Capture → Ledger; it does not seed data or automatically convert historical source records. Existing Ledger stock/history remains unchanged.

The trace starting-point/lab-entry release requires Ledger `20261024000000_TraceEntryPoints` after the preceding migrations. Its approved migration workflow grants the configured restricted role EXECUTE on `ledger.trace_starts(uuid,text,text,uuid,uuid,integer,integer)` and retains the four-argument `trace_lot` grant; the metadata helper `ledger.trace_lot_metadata(uuid,uuid[])` stays private. New startup validates these boundaries and does not repair grants. This release was tested in isolated PostgreSQL and then deployed to the configured shared database on 24/10/2026 following the user’s explicit repair request; postflight runtime validation and backend `/health` passed. Future deployments remain separate approved actions. See [trace API/deployment contracts](api/traceability-api.md#starting-points-và-lab-entry--24102026).

### Shared database: avoid resetting grants from an older branch

`ConfigureRuntimeRoleAsync` revokes Ledger function privileges and reapplies a finite entry-point list. Running this workflow from an older branch whose list omits tracing can remove trace EXECUTE even though the database still contains the trace function. Merely switching Git branches or restarting the API does not itself run this reset. A read-only check on 24/10/2026 found the configured runtime login matched `Ledger:RuntimeRole`, `trace_lot` existed without EXECUTE, and `trace_starts`/`trace_lot_metadata` were absent. This establishes missing grants and an unapplied feature upgrade; it does not identify who last changed the ACL.

For a shared database, designate one migration/deployment owner or CI job running the agreed integrated revision. Merge the feature's migrations **and** runtime-grant configuration into that revision together, then run its approved `--migrate` workflow once and validate with the restricted runtime login. Other developers use ordinary `dotnet run` and the same agreed runtime configuration; they need no owner privileges for application use. Do not run grant reconciliation from stale branches after this deployment. Each future schema/function upgrade requires its own coordinated deployment; ordinary restarts require none. `dotnet ef database update` alone does not invoke the application's runtime-grant configuration.

Do not grant EXECUTE on every Ledger function or use default EXECUTE grants as a workaround: private helpers intentionally remain inaccessible to the runtime role. Do not add owner-driven grant repair to normal startup. If permission disappears after deployment, check which migration/grant workflow ran against the shared database before applying another manual grant.

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --migrate
```

Seeds are optional, separate commands. `--seed-dev` creates local Identity accounts. `--seed-ledger` creates demo mill setup and stock through official workflows, and creates missing demo accounts; it requires restricted runtime connection plus `DevelopmentSeed__Password`. Do not combine flags. Seed only a development/test database you own. Detailed account, password, demo data, rerun and limitation guidance: [Development accounts and demo Ledger data](guides/development-accounts.md).

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --seed-dev
# Or, instead, opt in to demo mill setup and stock:
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --seed-ledger
```

Normal application startup is separate and neither migrates nor seeds:


```bash
dotnet run --project backend/src/GrainLedger.Api
```

## Mixed-bulk forward migration and activation

Ledger migration `20261020000000_MixedBulkStorage` installs mixed-bulk location/pool state, protected command/safety/read routines, and restricted grants through four embedded SQL resources. It is forward-only; rollback is unsupported because custody history is immutable. Implementation is local and is not deployed to shared Supabase or production. Do not run `--migrate`, seeds, grant changes or schema changes against any shared database without explicit database-owner approval.

No reset, stock backfill, conversion, or default mixed-bulk policy is part of deployment. Existing locations remain `mixedBulk: false`; explicit location opt-in alone does not authorize ingress. An Administrator must record append-only evidence-backed policy for exact location kind, owner, commodity, stage, product identity and effective time, with `identityScopeConfirmed: true`. No factory default/seed policy. Supported kinds are `STORE`, `SILO`, `COMPARTMENT`; `ISOLATION` is only for held-stock isolation, not pooling. Runtime credentials must remain restricted; migration owner must apply generated grants and private helper/table privileges via the reviewed migration path.

In Setup → Mixed-bulk location policies, **Policy owner** lists customer organizations only. Select the customer that owns the stock, not the operating mill, then upload evidence for that customer and the location's facility and confirm the exact identity scope. A mill organization is not an eligible stock owner; API callers submitting one are rejected even when every policy field is populated.

## Local web and API

From repository root, start backend and frontend in separate terminals. Run `npm ci` before the first web launch or after lockfile changes.

```bash
dotnet run --project backend/src/GrainLedger.Api
npm ci
npm run dev --workspace @grainledger/web
```

Web app: `http://localhost:5173`. In Development, Scalar: `http://localhost:5080/scalar/v1`; OpenAPI: `http://localhost:5080/openapi/v1.json`. Scalar/OpenAPI are not production documentation endpoints. See [README](../README.md#run-locally) for concise quickstart.

## Production deployment

Build container image from repository root:

```bash
docker build -t grainledger:local .
```

The Dockerfile builds frontend assets and ASP.NET app into a single image exposing port `8080`. `.env` is excluded from image. Provide secrets through protected deployment secret storage, not image layers or source. Run a one-shot migration job with `ConnectionStrings__GrainLedgerMigration`, `Ledger__RuntimeRole`, and other required configuration; give the long-running app only `ConnectionStrings__GrainLedger` runtime credentials. Do not put migration-owner secret in normal app environment. Back up database before applying migrations. Migration owner must have required object ownership/rights; runtime validation is a security boundary, not a configuration warning to bypass.

Serve app through HTTPS reverse proxy on one origin. Configure trusted proxy addresses in `ReverseProxy__KnownProxies__0` (and indexed entries), preserve original `Host`, and forward `X-Forwarded-Proto: https`. Set `AllowedHosts` to deployed host. Production cookies require HTTPS. Configure `DataProtection__KeysPath` to durable storage with access restricted to the app identity and protected backups; instances that share cookie authentication need access to the same protected key ring. Set production Google callback to `https://YOUR_HOST/signin-google`. Compose starts local PostgreSQL only; it does not provide proxy, production secrets, or Google setup.

### Receiving and unified lot-code compatibility

For releases containing the current receiving/code behavior, ensure Ledger migrations include `20261017000000_UnifiedLotCodes`, `20261019000000_ReceivingLossThreshold`, and forward repair `20261019010000_RepairReceivingLotCodeBoundary` (`RepairReceivingLotCodeBoundary.sql`) in order. A database that already recorded the threshold migration may still have earlier functions that hardcode `LL-…`, rejecting schema-version-2 procurement with `Pending source package incomplete`. Editing an applied migration does not update installed functions. The forward repair replaces threshold-aware package validation and receiving detail with unified-code versions without rewriting stock, evidence, or threshold snapshots. Run through approved migration-owner job; do not rerun historical SQL or delete migration-history rows. Confirm applied migrations against target database before deployment; source code alone does not mean repair has run there.

The API and database must be cut over together: pause intake and processing writes during unified-code deployment; old API instances cannot issue or validate new codes. Do not roll back to an API version that only understands `LL-…` after unified codes exist. Existing historical `LL-…` records remain unchanged. New generated codes use `TOKEN-YYYYMMDD-SERIAL`; see [unified lot-code guide](guides/unified-lot-code-guide.md).

### External laboratory workflow

Deploy the API/web changes with Capture migration `20261020000000_ExternalLaboratoryWorkflow` through the approved migration-owner procedure above. It adds sample material/laboratory handover fields, company-scoped original report bytes/result rows, and append-only confirmation guards. Historical sample material remains unknown (`NULL`); old handovers remain `HANDOVER`. It does not rewrite stock, receiving history or HOLDs, and does not install legal limits or assessment policy.

Follow with Capture migration `20261020120000_TrimLegacyLabIdentityComparisons` when upgrading existing lab installations. It replaces only the confirmed-association guard so database comparisons use the same leading/trailing whitespace rules as the API; original evidence, stock, and HOLDs are not rewritten. Deploy the matching API and restart existing backend processes. Restarting alone does not install the database function upgrade.

Unconfirmed-report editing/deletion requires the forward Capture migration `20261023000000_LabReportDraftRevisions` and matching API/web build. It adds append-only `capture.lab_report_retirements` records and guards that prevent retiring confirmed reports or confirming retired drafts. An edit appends a replacement report; deletion appends a retained tombstone. Existing original files, observations, confirmations and residue history are not rewritten or removed.

Apply through the approved migration-owner job, then refresh the restricted runtime role using the existing role-configuration procedure before restarting the matching API. Runtime validation requires the new table's intended least-privilege grants. Do not grant elevated application credentials or bypass validation. Verify against disposable PostgreSQL; feature implementation does not authorize shared Supabase migrations, grants, seeds or report mutations.

Original files are stored durably in PostgreSQL, limited to 10 MiB per report, and served only through scoped authenticated downloads. Include Capture laboratory tables in database backup/restore procedures. No facility or external laboratory account is required. Applying migrations to shared Supabase requires explicit approval; use disposable test containers for write-based verification unless separately approved. See [laboratory API](api/sample-api.md#laboratory-report-routes).

### Residue bounds

Residue bounds deployment requires Catalog migrations `20261022000000_ReviewedResidueRules` and forward-only `20261023000000_CompanyResiduePolicies`, Capture `20261022000000_ReviewedResidueEvidence`, and Ledger `20261022030000_ResidueBounds` in existing module migration order. Check actual installed migration history before applying anything; configured-database inspection on 2026-10-23 found these residue tables already present, but this work did not deploy or migrate features. Apply missing migrations only through explicitly approved migration-owner job. The company-policy migration adds append-only company-scoped rule/settings support and runtime grants; it does not rewrite existing rule payloads or seed/adopt policies. Downgrade is unsupported because company policy history cannot be flattened into global scope; use approved database backup restore if rollback is required. Restart with matching API/web build and reviewed restricted-runtime grant/validation setup. Routine startup must not migrate.

New reviewed system/company rule versions, policy settings, source-group/report-selection payloads and evaluation snapshots are append-only. Include them in backup/restore. Runtime access remains protected: no direct Ledger snapshot writes or private-table reads; Catalog/Capture publication uses narrowly granted protected commands. Existing runtime grants must be refreshed using migration-owner deployment procedure, not owner credentials in normal application startup.

No shared database migration, seed, authorization grant or deployment was performed or authorized by this work. Do not treat isolated test data as seed data. Normal use requires explicit reviewed system-profile adoption or company override setting; no default adoption exists. A company override requires full compatible same-Code rule and never falls back when missing/inapplicable. Policy setting/rule versions and provenance are retained.

Section 15's matrix, combined report confirmation, exact concentration-unit normalization and live preview use existing storage/grants; they add no migration or dependency. A separately approved one-time owner transaction removed exactly 12 verified unreferenced mock rule versions from the configured database on 2026-10-23, retained all three evaluation snapshots and restored immutability guards before commit. This is not standing deletion permission or feature deployment. Cleanup and isolated verification: [section 15](plans/residue-bounds-plan.md#15-substance-matrix-single-action-report-confirmation-and-live-bounds).

Operational numeric use requires actual reviewed interpretation, material/method/residue-definition applicability, source representation, precision and process/fraction factors. `NUMERIC_RELATIVE_UNCERTAINTY` requires an exact nonnegative percent (`10` = 10%), and zero requires explicitly reviewed `RelativeUncertaintyAtZero`; normalized reviewed absolute uncertainty takes priority. Raw nonblank uncertainty without normalized absolute uncertainty remains unresolved. Missing evidence/rules/adoption yields `UNRESOLVED` with null unknown endpoints. Synthetic rules used by isolated tests/browser harness must never be published operationally. Residue bounds are not legal PASS, HOLD release or export certification. Routes, permission matrix and behavior: [residue bounds delivered contracts](plans/residue-bounds-plan.md#9-delivered-residue-bounds-contracts).

For isolated Linux tests when Docker bridge/veth creation is unavailable, opt in to a disposable PostgreSQL container using host networking, a distinct port per fixture and `listen_addresses=127.0.0.1`:

```bash
GRAINLEDGER_TEST_HOST_NETWORK=true TESTCONTAINERS_RYUK_DISABLED=true \
  dotnet test backend/tests/GrainLedger.Api.IntegrationTests/GrainLedger.Api.IntegrationTests.csproj \
  --filter 'FullyQualifiedName~Residue|FullyQualifiedName~Permission|FullyQualifiedName~OpenApi'
```

This test-only option does not use configured connection strings or an existing database. Normal fixture disposal removes its containers; with Ryuk disabled, interrupted runs may require cleanup of those test-owned containers. Default tests still use bridge networking. The opt-in browser host accepts loopback-only `GRAINLEDGER_BROWSER_API_URL` and `GRAINLEDGER_BROWSER_UI_URL` alongside its private `GRAINLEDGER_BROWSER_SESSION_FILE`; use separate ports and a temporary Vite proxy configuration rather than replacing a running application. Never publish the session file or test cookies.

The opt-in browser host configures console logger queue overflow as `DropWrite` for both its upstream TestHost and loopback bridge. Long-running `dotnet test` output capture can fill console pipes; this test-only policy keeps console backpressure from blocking HTTP request handling. Overflow console messages may be dropped. Application logging configuration is unchanged.

### Equipment retirement

Ledger migration `20261023000000_RetireEquipment` adds protected referenced-equipment retirement without adding a second lifecycle flag or rewriting historical operations. Apply through the approved migration-owner deployment job and refresh restricted runtime grants before using the matching API build. Runtime validation requires the new private helpers and predecessor command/read routines to remain ungranted. Normal startup does not migrate.

Unused equipment still uses versioned `DELETE`; referenced equipment uses versioned `POST /api/ledger/equipment/{id}/retire`. Both retain stored rows; retirement blocks new use and conflicts while active drafts or pending corrections remain. See [Ledger setup and permissions](plans/ledger-implementation-plan.md#setup-and-permissions).

For this overhaul, the user approved creating the forward migration and applying it only to disposable verification PostgreSQL. No shared Supabase migration, grant, seed or business-data mutation is authorized or performed by that approval.


### Dependency patch pins

Backend projects using Npgsql `10.0.3` pin `Microsoft.EntityFrameworkCore.Relational` to `10.0.13`; keep EF Core runtime packages aligned at `10.0.13` across API and transitive project graphs. Identity pins `System.Security.Cryptography.Pkcs` to `10.0.13` for the MailKit/MimeKit dependency chain. Preserve these explicit patch pins when updating dependencies unless a reviewed dependency change supersedes them.
