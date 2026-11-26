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
   SQL
   ```

   The migration owner creates and owns application objects in `grainledger`; no `public` migration-history grants are needed. Runtime must not be superuser, own application objects, or belong to owner/elevated roles. The migration grant command configures explicit runtime access.

   Runtime validation checks both the authenticated session role and the current role, since `SET ROLE NONE` can restore the session identity and `RESET ROLE` can restore a configured default. It rejects excess application privileges reachable through inherited, `SET`, or administrative memberships, including multi-hop paths and column-level grants. `ADMIN TRUE` can enable `SET` even when both `INHERIT` and `SET` are initially false. Review PostgreSQL 17 membership options and effective grants of reachable roles when provisioning the runtime login.

3. Set root `.env` connection strings for this fresh Compose database (port `5433`) and restricted role:

   ```dotenv
   ConnectionStrings__GrainLedgerMigration='Host=localhost;Port=5433;Database=grainledger;Username=grainledger_migration;Password=replace-with-migration-password'
   ConnectionStrings__GrainLedger='Host=localhost;Port=5433;Database=grainledger;Username=grainledger_runtime;Password=replace-with-runtime-password'
   Ledger__RuntimeRole='grainledger_runtime'
   ```

   These example connections are for the new local Compose database only. For an existing database, retain its current connection settings and ask its owner for the intended migration procedure. Runtime role must remain restricted; startup rejects elevated or owner credentials.

### Runtime grants when feature branches share a database

Use a runtime-role configurator that knows every installed public entry point. F12 propagation and the container/history extension must coexist: the approved grant set includes the residue job/read routines and, when installed, `ledger_closed_order_kg(uuid,uuid)` and `ledger_business_history(uuid,jsonb)`.

An older branch can remove another feature's EXECUTE grants when it runs `--configure-runtime-role`, because the command revokes application privileges before rebuilding its allowlist. A branch that does not recognize newer public routines can also reject their existing grants at startup. Compare the exact missing and unexpected signatures with reviewed feature code; do not disable runtime validation, allow arbitrary helpers, or rerun migrations to repair an EXECUTE-only mismatch. Reconfigure through the approved owner procedure using the combined code. Shared-database permission changes still require database-owner approval.

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

## Approved cutover target (pending deployment confirmation)

The approved target is one `GrainLedger.Api` project, one `AppDbContext`, one EF migration baseline, and one PostgreSQL application schema, `grainledger`. Migration creates the baseline and then applies explicit restricted-runtime grants. Normal startup does not migrate, seed, or repair grants. This target does not establish that any database reset or deployment has occurred; check with the deployment owner before relying on it.

Objects from original schemas map to `grainledger.<schema>_<name>` using schema tokens `identity`, `catalog`, `capture`, `ledger`, `exports`. Database object names, including indexes, constraints, triggers and routines, are transformed; API contracts and business values are unchanged. The single EF history table is `grainledger."__EFMigrationsHistory"`. The baseline is intentionally non-rollbackable; recovery requires the explicit database backup/reset procedure, not `Down`.

### Local migration and seeds

Run migration only against a Development database you own and control. Verify migration and runtime connections address same intended database. Migration credentials stay out of normal application environment; runtime credentials remain restricted. Do not run these commands against shared Supabase without separate explicit database-owner approval and completed backup/dependency preflight.

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --migrate
```

Seeds are separate, Development-only commands and require `DevelopmentSeed__Password` plus the restricted runtime connection. Choose one:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --seed-dev
# Setup accounts, facility, locations, equipment and assignments only:
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --seed-setup
# Explicit full demo, including synthetic policy/evidence and 600 kg receipt:
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --seed-ledger
```

`--seed-dev` creates six development accounts only. `--seed-setup` creates those accounts plus two organizations, one synthetic facility, three locations, two equipment rows and two assignments. It does not create demo commodities/units, farmer or procurement records, lab reports, policy/evidence, stock, postings, or export business records. Existing migration-installed reference/configuration rows, including receiving threshold baseline/history, remain. `--seed-ledger` remains separate opt-in full-demo behavior. Do not combine migration or seed flags; do not run both seed modes during deployment.

Setup-only seed is intended for Development bootstrap, not proof of a completed deployment. Detailed account names, password constraints, rerun behavior and scope: [development accounts guide](guides/development-accounts.md).

Normal application startup remains separate:

```bash
dotnet run --project backend/src/GrainLedger.Api
```

### Runtime grants after branch or deployment changes

PostgreSQL grants persist across machine and API restarts. If startup rejects missing or excess runtime grants, compare the configured login, installed migration history and application version before changing anything. A database upgraded by another teammate can be incompatible with an older feature branch.

When migrations are already installed and only runtime configuration needs repair, the approved migration owner can run this command instead of migrating or seeding:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api --no-launch-profile -- --configure-runtime-role
```

The command requires `ConnectionStrings__GrainLedgerMigration` and `Ledger__RuntimeRole`. Obtain explicit database-owner approval for shared databases. It revokes existing application grants and reapplies the finite list defined by the executing code: **use a branch/build containing every installed feature**, including container entry points when container is deployed. Running it from an older or incomplete branch can remove grants needed by other API instances. Keep normal startup on the restricted runtime connection; do not bypass validation or auto-repair using owner access.

### Physical stock by location

Forward AppDbContext migration `20261126000000_LocationStock` creates `grainledger.ledger_location_stock(uuid,uuid,jsonb)`; `20261127000000_LocationStockVarietyIdentity` adds each lot's catalog variety ID to its response without changing stock. The function revokes PUBLIC access; the matching runtime-role configuration grants the restricted runtime role EXECUTE on this entry function, not direct access to private tables or helpers. Configuration and validation allow the function to be absent before its migration; once installed, validation requires its runtime grant. Migration `Down` is unsupported; plan recovery through an approved backup/restore procedure.

Before applying to a shared database, obtain explicit database-owner approval for both migration and grant changes; confirm target, installed history, compatible builds, backup and recovery. Apply through the approved migration-owner workflow above, then ensure the matching restricted runtime grant is configured before deploying the matching API/web builds. Normal startup neither migrates nor repairs grants. Use a complete current build for any role-configuration command, since it reapplies the finite grant list. No shared-database deployment is implied by this feature.

### Container preparation on the monolith branch

`feat/container` adds forward AppDbContext migration `20261113000000_ContainerPreparation`, embedded `Data/Sql/ContainerPreparation.sql` and the finite runtime grant for `grainledger.ledger_container_action(uuid,text,jsonb)`. The four preparation tables and private helpers remain inaccessible directly to runtime. An absent optional feature migration does not newly block normal startup; its endpoint returns 503. If installed, its entry grant must pass runtime validation. Run the approved migration-owner workflow above only after separate environment approval. Old `20261021`/`20261025` Ledger-context migrations are historical, not an upgrade command for the monolith.

This merge does not migrate/reset shared Supabase, transfer old-schema preparation data or claim the monolith cutover is deployed. Migrating between the old multi-schema database and the new baseline needs a separately reviewed backup/cutover/data-retention procedure. See [completion review](reports/container-completion-review-2026-11-13.md).

### Direct-stock container execution

Forward AppDbContext migration `20261113000100_ContainerExecution` adds private actual loading/source complement/HOLD/closure/request records and the protected planned-container command/read/target routines. Explicit runtime setup grants only these public entries, retaining no direct table/helper writes. It does not migrate legacy preparation into actuals, implement approved blend production or auto-repair startup grants. Apply only through the approved owner migration workflow after environment-specific approval; no shared deployment is asserted. [Execution API](api/container-execution-api.md) and [web guide](guides/container-execution-web-test.md) describe current supported stock and limits.

### Approved container blend production

Forward migration `20261117000000_ContainerProduction` implements one-batch approved production, actual-output review and output reservation for loading. It adds the finite runtime entry `grainledger.ledger_container_production(uuid,uuid,text,jsonb,jsonb)`; private tables/helpers stay denied. It was tested on disposable PostgreSQL, then applied with the single new grant to the configured shared Supabase on 18/11/2026 after explicit user approval; see the [deployment record](reports/container-production-deployment-2026-11-18.md). For other environments follow the [production deployment checklist](guides/container-production-deployment.md), including owner approval, backup and compatible API instances; old code does not understand the new RESERVE events/output authorization. Normal startup does not migrate or seed. See [verification](reports/container-production-implementation-2026-11-18.md).

### Historical module-era deployment notes

The following migration-specific instructions preserve prior release history only. They describe old per-module schemas/migrations and must not be used as current cutover or deployment instructions. Confirm current database status independently; no reset or deployment is asserted here.

## Container S2 workflow deployment — 18 November 2026

Forward migration `20261118000000_ContainerWorkflow` and the matching API/web build add partial reviews/batches, plan activation/whole-manifest CLOSE, reviewed pool/transfer/bagging handoff, HOLD notices and versioned shipment records. It was verified on disposable PostgreSQL and subsequently applied to the configured shared Supabase after the user's separate explicit S2 approval; see the [deployment record](reports/container-s2-deployment-2026-11-18.md). Existing S1 approval was not reused; no broad ACL rebuild, shared seed or fixture copy was run. Approval for this deployment does not authorize future changes or other environments. Review the [S2 package](guides/container-s2-deployment.md) and obtain explicit approval before this shared migration and the single new EXECUTE grant on `grainledger.ledger_container_workflow(uuid,uuid,text,jsonb,jsonb)`. Do not run seeds, broad runtime-role reconfiguration or owner-credential application startup to enable it. Older writers are incompatible with S2 scope/reservation semantics.

## Historical mixed-bulk migration and activation


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

### Receiving and code cutover

Apply the current monolith migrations through an approved migration-owner job before deploying the matching API. Pause Capture and Ledger writes during cutover; inventory pending receipts without lot-code reservations and older processing drafts first. Those records cannot proceed through the new write paths without an explicit remediation plan. Stored records are not rewritten. See [lot-code guide](guides/unified-lot-code-guide.md) and [Capture code guide](guides/companion-capture-codes.md).

### Rice variety segregation cutover

Forward migration `20261125000000_RiceVarietySegregation` and its embedded `Data/Sql/RiceVarietySegregation.sql` cover both Capture and Ledger. Deploy the matching API and web together; do not allow old API instances or clients to continue writing against the new schema. This migration is forward-only: agree on backup/restore recovery, not a code-only downgrade. Implementation does not imply deployment.

Before each environment rollout:

1. Obtain separate explicit database-owner approval for the shared environment before migration, grants, seeds, or data-changing verification. Confirm the target database, installed migration history, compatible API/web builds, deployment window and recovery procedure.
2. Pause Capture and Ledger writes, including background writers and old instances. Back up the database and ensure the approved restore procedure is available before migration; keep writes paused until the coordinated cutover is complete.
3. Inventory legacy pending procurements and their receiving destinations. Resolve pending legacy receipts destined for pools without variety IDs and multi-line receipts destined for non-mixed physical compartments through an approved process **before** cutover. If any remain unresolved, do not roll out that environment. Do not automatically reroute receipts or rewrite their snapshots.
4. Inspect positive balances by physical location, including `SILO`, `COMPARTMENT` and mixed-bulk locations. A non-mixed silo or compartment with multiple positive-balance lots needs an operational plan: after cutover, withdraw or transfer stock to separated storage until the single-lot guard is satisfied; do not backfill stock. Non-mixed `STORE` holds physically separated lots and is not a shared bulk compartment.
5. Identify legacy Plot Seasons and lots without verified catalog variety IDs. Existing lots become `UNKNOWN`; old text is not evidence for assigning a variety. Do not pool or merge unknown lots. Eligible single-lot drying, milling or bagging retains `UNKNOWN`; use permitted separated or isolation flows where appropriate. Assign an ID to an eligible legacy Plot Season only through the authorized application workflow; it does not rewrite old procurement snapshots or lot identities.
6. Review previously used physical locations and arrange evidence-backed clearance when required. An emptied location is not automatically cleared: previously used locations with unknown last contents require clearance before refill, and changes of contents may also require it. Use the existing clearance workflow with an empty location, evidence and reason; do not treat setup edits or zero balance as clearance.
7. Apply the migration through the approved object-owner job and configure only the matching build's finite restricted-runtime grants. Private tables and helpers remain inaccessible directly to runtime; never substitute owner credentials, broad grants or startup auto-repair. Start only matching API/web instances, then resume writes under the approved deployment procedure.

Migration does not automatically rewrite historical receipts, events, postings, quantities, lot codes or procurement snapshots. Existing mixed or unknown stock is not silently converted to a verified single variety.

### External laboratory workflow

Deploy the API/web changes with Capture migration `20261020000000_ExternalLaboratoryWorkflow` through the approved migration-owner procedure above. It adds sample material/laboratory handover fields, company-scoped original report bytes/result rows, and append-only confirmation guards. Historical sample material remains unknown (`NULL`); old handovers remain `HANDOVER`. It does not rewrite stock, receiving history or HOLDs, and does not install legal limits or assessment policy.

Follow with Capture migration `20261020120000_TrimLegacyLabIdentityComparisons` when upgrading existing lab installations. It replaces only the confirmed-association guard so database comparisons use the same leading/trailing whitespace rules as the API; original evidence, stock, and HOLDs are not rewritten. Deploy the matching API and restart existing backend processes. Restarting alone does not install the database function upgrade.

Unconfirmed-report editing/deletion requires the forward Capture migration `20261023000000_LabReportDraftRevisions` and matching API/web build. It adds append-only `capture.lab_report_retirements` records and guards that prevent retiring confirmed reports or confirming retired drafts. An edit appends a replacement report; deletion appends a retained tombstone. Existing original files, observations, confirmations and residue history are not rewritten or removed.

Apply through the approved migration-owner job, then refresh the restricted runtime role using the existing role-configuration procedure before restarting the matching API. Runtime validation requires the new table's intended least-privilege grants. Do not grant elevated application credentials or bypass validation. Verify against disposable PostgreSQL; feature implementation does not authorize shared Supabase migrations, grants, seeds or report mutations.

Original files are stored durably in PostgreSQL, limited to 10 MiB per report, and served only through scoped authenticated downloads. Include Capture laboratory tables in database backup/restore procedures. No facility or external laboratory account is required. Applying migrations to shared Supabase requires explicit approval; use disposable test containers for write-based verification unless separately approved. See [laboratory API](api/sample-api.md#laboratory-report-routes).

### Residue bounds

Whole-flow residue cutover requires forward migration `20261104000000_WholeFlowResidueProfiles`, its embedded SQL resource, and matching API/web versions. Check actual installed migration history and obtain explicit database-owner approval before any shared migration, grants or writes. Back up the database and agree on recovery before cutover. Migration `Down` is unsupported: the old application cannot consume new contracts, so rollback requires an approved backup/restore procedure, not a code-only downgrade. Normal startup does not migrate.

Migration reuses existing append-only profile, setting, evidence and evaluation storage and replaces protected routines; it does not backfill, combine, delete or convert old approvals. Legacy profile/setting/selection/group records remain stored but are invisible to active readers. Old-engine evaluations remain stored but are absent from new history; detail is not found and replay of their IDs conflicts. Existing laboratory originals and results remain usable after fresh reviewed binding. Include all these records in backup/restore.

Plan fresh reviewed `FLOW` schema-2 profiles with new Codes, explicit company adoption or same-Code company override, and fresh schema-2 source selections/groups. Scientific profile publication and evidence binding require explicit as-received confirmation; identity/transcription confirmation can still succeed without it, but cannot automatically bind residue. Do not infer as-received equivalence from old records. Settings and evidence must cover relevant source and event times. Missing policy or evidence remains `UNRESOLVED`.

Keep runtime credentials restricted; publication and evaluation use protected commands rather than direct table mutation. Apply any necessary runtime grants only through the approved migration-owner procedure; never give owner credentials to normal application startup. Synthetic examples are not operational policy. Export market `residueBasis` is independently reviewed and must be `AS_RECEIVED` for compatible coverage; unsupported conventions remain incomplete. Residue bounds are not legal PASS, HOLD release or export certification. See [profile field guide](guides/residue-rules-entry-guide.md) and [laboratory API](api/sample-api.md#residue-bounds-for-derived-lots).

Profile retirement requires forward migration `20261114000000_ResidueProfileRetirement`, its embedded `ResidueProfileRetirement.sql` resource and matching API/web builds. After backup and deployment-owner approval, apply it through the existing migration-owner job and configure restricted runtime grants through the established role procedure before starting the matching API. Normal startup does not migrate or repair grants. Migration rollback is unsupported; plan recovery using an approved backup/restore procedure. No shared database migration, grant change or write is implied by this implementation; check installed migration history and obtain explicit database-owner approval before touching shared Supabase.

The migration adds append-only retirement history and protected retirement commands and guards. Include retirement records in backups. Retirement preserves published versions and saved evaluation snapshots; new evaluations cannot use a retired series or fall back to an older version. See the [retirement API](api/sample-api.md#residue-bounds-for-derived-lots) and [profile field guide](guides/residue-rules-entry-guide.md#retire-a-profile-series).

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
