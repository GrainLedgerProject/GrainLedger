# GrainLedger

GrainLedger is a modular monolith for rice supply-chain traceability. This implementation provides Google sign-in, email/password registration with email OTP, JWT access/refresh tokens, password recovery, user profiles, organization management, server-enforced access control, farmer registration and lookup, and Capture procurement integrated with protected Ledger stock.

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

5. Run migrations and seed local accounts. Migration updates Identity, Catalog, Capture and Ledger in order; it does not convert or seed historical records:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --migrate
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --seed-dev
   dotnet run --project backend/src/GrainLedger.Api
   ```

   Keep existing `.env` and connection strings unchanged. Before migration or seed, confirm `ConnectionStrings__GrainLedger` and `ConnectionStrings__GrainLedgerMigration` target your intended Development database. `--seed-dev` needs `DevelopmentSeed__Password` as documented in [development accounts](docs/development-accounts.md). Migration preserves existing Ledger stock/history; no automatic conversion of prior synthetic source records into official Capture records occurs. Do not run migrations or seeds against shared Supabase without explicit database-owner approval.

6. Start frontend:

   ```bash
   npm ci
   npm run dev
   ```

   Open **http://localhost:5173**.

## Email/password API and Scalar

See [the Vietnamese authentication and Scalar guide](docs/auth-email-password-scalar.md) for SMTP/JWT configuration, registration and verification, login, refresh, logout, password recovery, and example requests.

Run the database migrations, start the backend, and open **http://localhost:5080/scalar/v1** in Development. Login returns `accessToken`, `refreshToken`, and the user profile. Paste the access token into Scalar's Bearer field to test protected endpoints without browser cookies or CSRF tokens. SMTP must be configured for real verification/reset email delivery. The React UI supports both Google sign-in and email/password registration, verification, login, and password recovery. See [development accounts](docs/development-accounts.md) to seed six local role accounts and one demo customer organization.

## First administrator and account lifecycle

Configure `Bootstrap__AdminEmail` in `.env` before initial signup. The first matching newly provisioned Google account, or local account after email OTP verification, becomes administrator if no administrator exists. After setup, remove that line or set it to an empty value and restart the backend. Existing administrators retain their access.

Other people sign in through Google or complete local email verification and receive a pending profile with no business permissions. The administrator creates customer organizations and assigns each account an organization and role from **Administration**. Roles must match the organization's kind. Access changes revoke sessions; disabled accounts cannot sign in.

Profiles support display name and optional phone number; editing a profile cannot grant roles or change organizations. Local passwords are hashed and support recovery through email OTP; Google manages identity and recovery for Google accounts. Accounts from different providers are not automatically linked merely because they share an email address.

## Ledger workflow

Permission-gated workspace provides Catalog setup/lookups; official Capture farmer, plot, season, plot-season and procurement workflows; stock search/detail/history; processing drafts, manager confirmation, transfers and Ledger processing corrections; customer HOLD, manager response/isolation and notifications. Procurement routes: `/api/procurement-reference-data`, `/api/procurements/commit`, `/api/procurements`, `/api/procurements/{id}` and `/api/source-lots/{id}` (see [procurement API](docs/procurement-api.md)). Capture procurement commits protected Ledger stock atomically using persisted Catalog-to-Ledger commodity mapping. Historical Ledger stock/provenance remains; legacy synthetic records are not converted. Retired mock routes, including `/api/capture-mock/status`, are unavailable (404). Official Capture has no arrival-observation or receipt-correction endpoints. UI gates are not authorization boundary; backend checks persisted role, permission, owner and facility.
Data loading uses fresh, scoped reads without a response cache. Unfiltered stock reuses the same lot response; filtered stock, detail, source lots and permitted setup lookups load concurrently. Ledger reads validate persisted active role and scope inside `ledger.read`, avoiding a redundant identity query. React StrictMode replays mount effects in Development; compare production builds for user-facing timings.

Single-lot history selects relevant event IDs before joining postings, with indexed restriction, HOLD and reverse-source lookups. This optimization requires explicit `20261014000000_OptimizeLedgerReads` migration; ordinary startup does not apply it. See [read scalability and tracing readiness](docs/architecture.md#read-scalability-and-tracing-readiness) for historical isolated large-data measurements, remaining unbounded-list limits and proposal F18 gaps. Do not apply migrations to shared Supabase without approval.

Current workflow outline (not a test report): configure facilities, locations, commodities and policies; register Capture farmer/plot/season/plot-season and commit procurement; verify stock/provenance; run processing, transfer, HOLD and processing-correction paths with scoped roles. Official Capture lacks arrival-observation and receipt-correction endpoints.
Latest cutover verification: frontend 24 tests and production build passed. Backend `dotnet build GrainLedger.slnx` passed with existing CS9113 unread access warning; `dotnet test GrainLedger.slnx --no-build` passed 166/166, 0 skipped. Isolated real-browser flow registered a farmer, Point plot and plot-season, committed official procurement for `12.345678 KG`, and observed source `currentStock.onHandKg` as exact string `"12.345678"` with matching Ledger physical and available stock. Retired mock status returned 404. Smoke used isolated PostgreSQL through a test-only cookie bridge; no shared database or `.env` changes.

Build/test commands (not run for this documentation-only change):

```bash
dotnet build GrainLedger.slnx
dotnet test GrainLedger.slnx --no-build
npm test
npm run build
```

Backend integration tests require Docker/Testcontainers. Browser/auth harness is test-only/local host, never production login bypass. Live Google consent requires your credentials and is separate. Factory approval is not established by synthetic policy or tests.

## Deployment

```bash
docker build -t grainledger:local .
```

`.env` excluded from image. Store runtime and migration-owner secrets in protected secret storage. Provision separate PostgreSQL roles before migration. Run one-shot image `--migrate` with migration-owner credentials and `Ledger:RuntimeRole`; migration order is Identity → Catalog → Capture → Ledger. Long-running app receives runtime credential only. Do not put migration-owner secret in normal app environment. Back up before migrations. Shared database migration, seed or schema/role/grant changes require explicit database-owner approval. Migrations preserve existing Ledger stock/history; no automatic synthetic-record conversion occurs.

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
