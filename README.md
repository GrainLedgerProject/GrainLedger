# GrainLedger

GrainLedger is a modular monolith for rice supply-chain traceability. It provides Google sign-in and local email/password registration with email OTP, JWT access/refresh tokens, and password recovery. Identity supports session management, user profiles and organization administration with server-enforced access control. Ledger provides protected stock accounting and provenance. Development and Testing hosts also expose explicitly synthetic, durable MockCapture workflows for source registration and receipt/receiving corrections.

## Structure

```text
backend/src/GrainLedger.Api/                 Single ASP.NET Core 10 host
backend/src/GrainLedger.Modules.Identity/    Identity schema, authorization, migrations
backend/src/GrainLedger.Modules.Ledger/      Protected stock, processing, history and evidence
backend/src/GrainLedger.Modules.MockCapture/   Development/Testing synthetic source and receipt workflows
backend/tests/GrainLedger.Api.IntegrationTests/
apps/web/                                     React + TypeScript + Vite
docs/architecture.md                        Module boundaries and authorization rules
```

All modules run in one host and own separate PostgreSQL schemas/contracts. Mobile workspace remains placeholder; React web app works on mobile screens.

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

5. Run Development migrations, then seed local accounts and optional mock stock. Commands are separate; `--seed-mock` requires migration and seed-owner credentials, applied migrations, and `--seed-dev` accounts first:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --migrate
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --seed-dev
   ASPNETCORE_ENVIRONMENT=Development dotnet run --project backend/src/GrainLedger.Api -- --seed-mock
   dotnet run --project backend/src/GrainLedger.Api
   ```

   Keep existing `.env` and connection strings unchanged. Before either seed, confirm `ConnectionStrings__GrainLedger` and `ConnectionStrings__GrainLedgerMigration` point to your intended Development database; `--seed-mock` writes through configured migration-owner connection. Commands use configured database and never create/reset a local database. `--seed-dev` needs `DevelopmentSeed__Password` configured as documented in [development accounts](docs/development-accounts.md). Mock seed requires migration-owner credentials but does not migrate; it reuses active seeded actors and does not change credentials. It writes durable synthetic Ledger stock; it does not reset or delete records. Reruns do not overwrite edited sample data. Synthetic stock persists in shared database; ensure you have permission to add it. Seed leaves teammate `capture` schema and captures untouched. Mock seed neither auto-seeds accounts nor approves tolerances or creates operational policies. MockCapture owns isolated `capture_mock` schema and distinct migration-history table. Its schema, migrations, routes, and seed command are Development/Testing-only. Never inspect, grant, repair, alter, or drop teammate `capture` schema. No automatic migration exists from synthetic MockCapture data to future real Capture. Normal startup uses `ConnectionStrings:GrainLedger` only and never migrates or seeds.

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

Permission-gated workspace provides setup/lookups; explicitly synthetic, durable source records, receipts and receiving corrections in Development/Testing; stock search/detail/history; processing drafts, manager confirmation, transfers and corrections; customer HOLD, manager response/isolation and notifications. Committed synthetic receipts create receipt-derived Ledger stock, which remains durable Ledger data and is not automatically migrated to future Capture. The Sources & receiving section appears only when `/api/capture-mock/status` reports `{ "synthetic": true }`; its absence does not block other Ledger sections. UI gates are not authorization boundary; backend checks persisted role, permission, owner and facility.

Data loading uses fresh, scoped reads without a response cache. Unfiltered stock reuses the same lot response; filtered stock, detail, receiving sources and permitted setup lookups load concurrently. MockCapture receipt/arrival/correction lists filter company/facility scope before bulk hydration rather than querying each record separately. Ledger reads validate persisted active role and scope inside `ledger.read`, avoiding a redundant identity query. These fetch changes require no database migration or `.env` changes. Supabase connection/network latency still affects cold loads; React StrictMode also replays mount effects in Development, so compare production builds for user-facing timings.

Nine-step executable smoke scenario uses integration-test host and PostgreSQL, not a production auth bypass:

```bash
dotnet test backend/tests/GrainLedger.Api.IntegrationTests --filter FullyQualifiedName~Pdf_nine_step_smoke_proves_real_stock_and_authorization
```

1. Configure facility/location/commodity/assignments, labeled synthetic policies/evidence, farmer, plot, season and plot-season.
2. Commit receipt lines of 50,000, 30,000 and 20,000 kg.
3. Dry 100,000 → 88,000 kg; declared loss 12,000 kg, variance 0.
4. Mill into actual head rice 58,000, broken rice 8,000, bran 6,000 and husk 15,700 kg; declared loss 300 kg, variance 0. Byproducts remain stock.
5. Verify every milling output's exact 50/30/20 source attribution and contribution total.
6. Transfer 1,000 kg head rice without changing lot identity; isolate held 8,000 kg broken rice without release; atomically correct a separate drying operation.
7. On separate receipt, record 19 kg arrival against 20 kg source; verify unavailable, evidence-correct measurement to 20 kg, retain original 19 kg.
8. Correct unused 30 kg receipt to 29 kg with evidence and atomic reversal/replacement. Verify subsequent transfer prevents further simple receipt correction.
9. Verify assigned-facility operator reads; foreign-facility, farmer-contact and pending-user denials.

Historical pre-cutover verification reported by implementation owner: `dotnet test GrainLedger.slnx --no-build` passed 87 tests, `npm test` passed 14 tests, and backend/frontend builds passed. Latest MockCapture cutover verification reported by implementation owner: 113 backend and 23 frontend tests passed, frontend build passed; a real browser workflow registered a synthetic farmer and committed a 10 kg receipt that appeared in Ledger stock. The old `/api/capture/farmers` route returned 404. Fresh Production migrations created no `capture_mock`, Production status returned 404, and a forward upgrade was verified against an isolated initial Ledger database. An incompatible teammate farmers schema/row and a Production teammate fixture remained unchanged. `.env` and Supabase were unchanged.

Build/test commands:

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

`.env` excluded from image. Store runtime and migration-owner secrets in protected secret storage. Provision separate PostgreSQL roles before migration. Run one-shot image `--migrate` with migration-owner credentials and `Ledger:RuntimeRole`; Production migration order excludes MockCapture, which is Development/Testing-only. Long-running app receives runtime credential only. Do not put migration-owner secret in normal app environment. Back up before migrations. Never migrate or modify a teammate/production Supabase database for MockCapture.

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
