# GrainLedger

GrainLedger is a modular monolith for rice supply-chain traceability. It provides authentication, access control, Capture procurement source registration, and Ledger receiving, stock, processing and history workflows.

## Structure

```text
backend/src/GrainLedger.Api/                 Single ASP.NET Core 10 host
backend/src/GrainLedger.Modules.Identity/    Identity module, PostgreSQL schema, migrations
backend/src/GrainLedger.Modules.Catalogs/    Catalog module, PostgreSQL schema, migrations
backend/src/GrainLedger.Modules.Capture/     Farmer and procurement APIs, PostgreSQL schema, migrations
backend/src/GrainLedger.Modules.Ledger/      Protected stock, processing, history and evidence
backend/tests/GrainLedger.Api.IntegrationTests/
apps/web/                                     React + TypeScript + Vite
docs/architecture.md                        Module boundaries and authorization rules
```

Identity, Catalog, Capture and Ledger run in one host and own separate schemas/contracts. Mobile workspace remains placeholder; React web app works on mobile screens. Capture contracts: [farmer API](docs/farmer-api.md) and [procurement API](docs/procurement-api.md).

In Development, API serves Scalar at `http://localhost:5080/scalar/v1` and OpenAPI at `http://localhost:5080/openapi/v1.json`. Use an access token in Scalar's Bearer field to test authenticated APIs.

## Run locally

Requires .NET SDK 10, Node.js 24, npm and Docker (or PostgreSQL).

1. If `.env` does not exist, copy `.env.example` to repository root. Keep existing `.env`; do not overwrite it.

2. Create distinct PostgreSQL migration-owner and runtime logins before first migration. For a **fresh** Compose database, replace all password placeholders in `.env` and this SQL with matching local-only values:

   ```bash
   docker compose up -d --wait postgres
   docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U grainledger -d grainledger <<'SQL'
   CREATE ROLE grainledger_runtime LOGIN PASSWORD 'replace-with-runtime-password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS;
   CREATE ROLE grainledger_migration LOGIN PASSWORD 'replace-with-migration-password' NOSUPERUSER NOCREATEDB NOCREATEROLE INHERIT NOBYPASSRLS;
   GRANT CONNECT ON DATABASE grainledger TO grainledger_runtime;
   GRANT CONNECT, CREATE ON DATABASE grainledger TO grainledger_migration;
   GRANT USAGE, CREATE ON SCHEMA public TO grainledger_migration;
   SQL
   ```

   Compose creates privileged `grainledger` only for local database administration. Neither app connection uses that login on a fresh install. Migration owner needs database `CREATE` for module schemas and `public` schema `USAGE, CREATE` for Identity's existing EF migration-history table. As object owner, it can grant runtime access without role membership. Runtime must not own database/module schemas/tables or belong to any owner/elevated role.

   Set `ConnectionStrings__GrainLedgerMigration`, `ConnectionStrings__GrainLedger`, and `Ledger__RuntimeRole=grainledger_runtime` in local `.env`, matching `.env.example` for fresh Compose setup (host `localhost`, port `5433`). Existing database: do not rerun `CREATE ROLE`, reset volume, change passwords or rewrite ownership implicitly. Use only its existing object-owner credential for migration; grants alone do not let a new login alter existing owner tables/history. Keep existing connection settings unchanged. Do not point Development migration at production or a teammate-owned database. Keep credentials private; use a separate Development database you control.

3. Configure Google values and bootstrap email in `.env`:

   ```dotenv
   Authentication__Google__ClientId='YOUR_GOOGLE_CLIENT_ID'
   Authentication__Google__ClientSecret='YOUR_GOOGLE_CLIENT_SECRET'
   Bootstrap__AdminEmail='YOUR_VERIFIED_GOOGLE_EMAIL'
   ```

   Single quotes preserve literal `$` and `#`. Backend loads root `.env` in Development when launched from repository/API project; environment and command-line overrides win. Tests use isolated config. Never prefix backend secrets with `VITE_` or commit `.env`.

4. Create **Web application** OAuth client in Google Cloud, configure consent and test users if needed. Register exact redirect URI:

   ```text
   http://localhost:5173/signin-google
   ```

   Vite on port 5173 proxies `/api` and `/signin-google` to backend port 5080. Use `localhost` consistently, not `127.0.0.1`. Production redirect: `https://YOUR_HOST/signin-google`. See [ASP.NET Core Google authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0).

5. Run migrations, then choose an optional Development seed. Migration updates Identity, Catalog, Capture and Ledger in order; it does not convert or seed historical records:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --migrate
   # Accounts only; use --seed-ledger instead for accounts, demo mill setup and stock.
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --seed-dev
   dotnet run --project backend/src/GrainLedger.Api
   ```

   Keep existing `.env` and connection strings unchanged. Before migration or seed, confirm `ConnectionStrings__GrainLedger` and `ConnectionStrings__GrainLedgerMigration` target your intended Development database. Both `--seed-dev` and `--seed-ledger` need `DevelopmentSeed__Password`; see [development accounts and demo Ledger data](docs/development-accounts.md). `--seed-ledger` configures a synthetic mill and creates 600 kg RAW_PADDY through official procurement and accepted arrival using the restricted runtime connection. Its facility-scoped 0% DRYING policy is synthetic, not factory-approved; other processing policies remain unconfigured. Reruns preserve existing setup edits and never replenish stock. Migration preserves existing Ledger stock/history; no automatic conversion of prior synthetic source records into official Capture records occurs. Normal API startup does not migrate or seed. Do not run migrations or seeds against shared Supabase without explicit database-owner approval.

6. Start frontend:

   ```bash
   npm ci
   npm run dev
   ```

   Open **http://localhost:5173**.

## Email/password API and Scalar

See [the Vietnamese authentication and Scalar guide](docs/auth-email-password-scalar.md) for SMTP/JWT configuration, registration and verification, login, refresh, logout, password recovery, and example requests.

Run the database migrations, start the backend, and open **http://localhost:5080/scalar/v1** in Development. The Scalar/API login returns `accessToken`, `refreshToken`, and the user profile. React uses a separate CSRF-protected cookie login and restores its session after reload; see [session and permissions](docs/identity-session-permissions.md). Paste the access token into Scalar's Bearer field to test protected endpoints without browser cookies or CSRF tokens. SMTP must be configured for real verification/reset email delivery. The React UI supports both Google sign-in and email/password registration, verification, login, and password recovery. See [development accounts](docs/development-accounts.md) to seed six local role accounts and one demo customer organization.

## First administrator and account lifecycle

Configure `Bootstrap__AdminEmail` in `.env` before initial signup. The first matching newly provisioned Google account, or local account after email OTP verification, becomes administrator if no administrator exists. After setup, remove that line or set it to an empty value and restart the backend. Existing administrators retain their access.

Other people sign in through Google or complete local email verification and receive a pending profile with no business permissions. The administrator creates customer organizations and assigns each account an organization and role from **Administration**. Roles must match the organization's kind. Access changes revoke sessions; disabled accounts cannot sign in.

Profiles support display name and optional phone number; editing a profile cannot grant roles or change organizations. Local passwords are hashed and support recovery through email OTP; Google manages identity and recovery for Google accounts. Accounts from different providers are not automatically linked merely because they share an email address.

## Ledger workflow

Permission-gated workspace groups **Receive stock**, **Stock & HOLDs**, **Process & correct**, and **Setup**. Administrators start in Setup; purchasing and receiving staff start in Receive stock; stock readers start in Stock & HOLDs. Purchasing separates Procurement from Source setup; inventory separates Stock, HOLDs & alerts, and History; processing separates Processing, Transfers, and Corrections. Queues appear before creation forms, which expand when needed. Setup contains Catalog master data (canonical commodity code/name and measurement units) plus facility setup; do not create Ledger commodities separately. Configure PADDY and a MASS unit before procurement. Catalog creation has no automatic seed/default writes; purchaser reference data reads persisted Catalog records. Commodity API: `/api/commodities` (`{ code, name }`); units: `/api/measurement-units` (`{ code, name, quantityKind }`), both with server-side search and 25-row pages.

Basic Ledger setup endpoints return small arrays; their UI filters, sorts and pages these locally. Catalog and Capture lists, and large Ledger queues (stock, operations, receiving, history, HOLDs, corrections and notifications), use bounded server-side search/filter/sort/pagination; paged responses include `items`, `page`, `pageSize`, `totalCount`, `hasMore`. Edit/delete controls require current creator, valid version and permitted organization/facility scope. Legacy rows with unknown creators are read-only. Basic names can change while referenced; structural changes to in-use records are rejected. Deletes are soft deletes: rows and history remain, disappear from active lists/details/selectors/counts, and identifiers stay reserved. No restore UI exists. Draft operations and replacement operation drafts can be edited; correction requests, committed records and postings are not editable. Pager is `<< < 1 2 3 > >>` with at most five numeric page links, and accessible first/previous/next/last labels.

Purchasers register farmers, plots, crop seasons and plot-season links, then register procurement with source mass, eligible receiving facility/location and exact decimal units. Registration records source expectation only: no usable stock exists until full arrival is recorded and accepted. One procurement represents one complete arrival; register separate procurement for each load. No truck entity/model.

Facilities, locations, equipment and staff assignments are basic setup; policies, blend compatibility and compartment clearance require evidence and belong to advanced setup. Stock search/history, processing, transfers, HOLD and processing-correction paths remain permission- and scope-gated. Procurement and receiving routes: `/api/procurement-reference-data`, `/api/procurements`, `/api/ledger/receiving-tasks`, `/api/ledger/receiving/{id}`, `/api/ledger/receiving/{id}/measurements`, `/api/ledger/receiving/{id}/reviews`, `/api/source-lots/{id}` (see [procurement API](docs/procurement-api.md)). Historical Ledger stock/provenance remains; legacy evidence is not converted or dropped. Retired mock routes remain absent.

No customer settlement workflow is included. Source expectation is not customer settlement authority. Existing historical procurement evidence and stock remain grandfathered and unchanged; new migration does not retroactively freeze or reinterpret legacy stocks. See [procurement API](docs/procurement-api.md).

Visited Ledger sections retain unsaved form inputs while switching tasks or sections. Returning to a section refreshes scoped data without resetting drafts; opening HOLDs & alerts refreshes its inbox. Data loading uses fresh, scoped reads without a response cache. Unfiltered stock reuses the same lot response; filtered stock, detail, source lots and permitted setup lookups load concurrently. Ledger reads validate persisted active role and scope inside `ledger.read`, avoiding redundant identity query. React StrictMode replays mount effects in Development; compare production builds for user-facing timings.

## Current verification

- Backend build passed; full suite: **228/228 passed**, none skipped. Existing NU1902/NU1903 warnings for `System.Security.Cryptography.Pkcs` remain.
- Web: **53 tests passed**, including list controls, scoped editing, soft-delete refresh and draft preservation; production build passed.
- Isolated browser: source **12** → pending **0 usable** → operator actual **10** mismatch **0 usable** → manager reason + scoped uploaded evidence → shortage approval **10 usable**. Exact-match operator submitted a complete **12 kg + 8 kg** set (**HTTP 200**, `RECEIVED_MATCHED`), confirming **20.000000 kg** usable; customer stock showed physical/available **12.000000 kg** and **8.000000 kg**, without restrictions. Receiving page at **375 px** had no body overflow; wide table overflow stayed local. Historical-data migration regression passed. No code changed during browser smoke; services, tab and manifests cleaned.
- Processing/policy UI smoke on isolated database: Admin selected policy facility/owner, uploaded evidence and recorded explicit **0%** drying policy; changing policy scope disabled recording again. Operator drying draft automatically retained Paddy commodity and derived `DRIED_PADDY` identity; **10.000000 kg** input → **9.500000 kg** output + **0.500000 kg** declared loss committed, with **9.500000 kg** available output stock and no manager variance approval. Processing page width stayed **375 px** at a 375 px viewport.
- Migration `20261014190000_PhysicalReceiving` has **not** been applied to the shared database; no shared database modifications were made. Shared migration requires explicit database-owner approval.
- Streamlined Ledger browser smoke used an isolated Testcontainers database and real cookie sessions for purchaser, operator, customer manager and mill manager; administrator setup was also checked. Verified source registration, retained unsaved input across task/section navigation, exact **12 kg + 8 kg** arrival posting **20.000000 kg**, drying **10 kg → 9.500000 kg** stock plus **0.5 kg** declared loss, correction request opening replacement editor, customer HOLD reaching manager inbox, history loading, and read-only setup without mutation controls. Layout stayed within **375 px** portrait and **812 px** landscape viewports; native advanced-setup disclosure opened by keyboard with reduced motion enabled. Shared data was untouched.
- List-management browser smoke used isolated Testcontainers and real administrator, purchaser and operator sessions. Processing showed three bounded pages, kind/owner filters and a separate committed list; deleting one draft reduced active drafts **56 → 55** and last-page rows **6 → 5**. Committed operations had no delete action. Administrator created, renamed and soft-deleted an unused facility; Catalog commodity archive disappeared from active reads. Purchaser created, renamed and archived an unused farmer; active search returned zero and detail returned **404**. Portrait **375 px** and landscape **812 px** checks showed no body overflow with reduced motion enabled.
- New Catalog attribution, Capture ownership and Ledger management/pagination migrations were exercised only on isolated databases, not shared Supabase. Deployment requires explicit database-owner approval before migration.

## Deployment

```bash
docker build -t grainledger:local .
```

`.env` excluded from image. Store runtime and migration-owner secrets in protected secret storage. Provision separate PostgreSQL roles before migration. Run one-shot image `--migrate` with migration-owner credentials and `Ledger:RuntimeRole`; migration order is Identity → Catalog → Capture → Ledger. Long-running app receives runtime credential only and does not migrate or seed on normal startup. Do not put migration-owner secret in normal app environment. Back up before migrations. Shared database migration, seed or schema/role/grant changes require explicit database-owner approval. Migrations preserve existing Ledger stock/history; no automatic synthetic-record conversion occurs.

Serve UI/API from one origin behind HTTPS reverse proxy. Configure:

| Environment variable | Purpose |
| --- | --- |
| `ConnectionStrings__GrainLedger` | Restricted PostgreSQL runtime login |
| `ConnectionStrings__GrainLedgerMigration` | Migration owner, migration job only |
| `Ledger__RuntimeRole` | Pre-created restricted role grant target |
| `Authentication__Google__ClientId` | Google Web client ID |
| `Authentication__Google__ClientSecret` | Google secret |
| `Bootstrap__AdminEmail` | Verified initial admin; clear after bootstrap |
| `ReverseProxy__KnownProxies__0` | Trusted proxy IP; indexed entries as needed |
| `DataProtection__KeysPath` | Protected persistent cookie encryption keys |
| `AllowedHosts` | Public host |

Forward original host and `X-Forwarded-Proto: https`. Production cookies require HTTPS. Compose starts only local DB, not proxy or Google credentials.
