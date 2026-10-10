# GrainLedger

GrainLedger is a modular monolith for rice supply-chain traceability. It provides Google sign-in, session management, user and organization administration, and server-enforced access control. Ledger and Capture provide protected stock accounting, provenance, receiving and correction workflows.

## Structure

```text
backend/src/GrainLedger.Api/                 Single ASP.NET Core 10 host
backend/src/GrainLedger.Modules.Identity/    Identity schema, authorization, migrations
backend/src/GrainLedger.Modules.Ledger/      Protected stock, processing, history and evidence
backend/src/GrainLedger.Modules.Capture/     Source records, receipts and receiving corrections
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

   Set `ConnectionStrings__GrainLedgerMigration`, `ConnectionStrings__GrainLedger`, and `Ledger__RuntimeRole=grainledger_runtime` in `.env`, matching `.env.example` (host `localhost`, port `5433`). Existing database: do not rerun `CREATE ROLE`, reset volume, or change passwords implicitly. Use its existing object-owner credential for migration only, or arrange a deliberate administrator-reviewed ownership migration covering existing Identity tables and `public."__EFMigrationsHistory"` as well as Ledger/Capture objects. Grants alone do not let a new login alter someone else's tables. Pre-create a separate restricted runtime login; no membership between migration and runtime roles is required. Never automatically rewrite ownership. Keep credentials local and uncommitted.

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

5. Apply migrations Identity → Ledger → Capture using migration owner, then start API with restricted runtime login:

   ```bash
   dotnet run --project backend/src/GrainLedger.Api -- --migrate
   dotnet run --project backend/src/GrainLedger.Api
   ```

   `--migrate` requires `ConnectionStrings:GrainLedgerMigration`; it applies ordered migrations and configures finite runtime grants for `Ledger:RuntimeRole`. Normal startup uses `ConnectionStrings:GrainLedger` only and rejects superuser/elevated membership, database/schema/table ownership, direct Ledger mutation or schema-create authority. Normal startup never migrates. Never run API with PostgreSQL `postgres` superuser.

6. Start frontend:

   ```bash
   npm ci
   npm run dev
   ```

   Open **http://localhost:5173**.

## First administrator and account lifecycle

Sign in using verified Google email configured as `Bootstrap__AdminEmail`. After setup, remove or empty it and restart. Other users receive no business permissions until admin creates customer organizations and assigns role. Roles must match organization kind. Access changes revoke sessions; disabled accounts cannot sign in.

Profiles support display name and optional phone. Google manages email and identity; profile edits cannot grant roles or change organization. GrainLedger does not maintain second password database.


## Ledger workflow

Permission-gated workspace provides setup/lookups; farmers, plots, seasons and receipt drafts/commits; arrival measurement/correction/investigation; stock search/detail/history; processing drafts, manager confirmation, transfers and corrections; customer HOLD, manager response/isolation and notifications. UI uses server records for selections and immutable evidence upload. Operator creates arrivals (`lots.receive`); manager corrects measurements/investigates (`receiving.reconcile`). Receipt physical reconciliation and replacement commit require manager `corrections.reconcile`. UI gates are not authorization boundary; backend checks persisted role, permission, owner and facility.

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

Final verification reported by implementation owner: backend build succeeded; warnings were fixed and later test builds succeeded; `dotnet test GrainLedger.slnx --no-build` passed **87 tests, 0 failed**; `npm test` passed **14 tests**; final `npm run build` passed. Actual production `--migrate` and restricted-runtime startup passed on disposable PostgreSQL 17, with migration-owner runtime login rejected. Actual browser workflows used restricted PostgreSQL runtime, cookies and CSRF: receipt 100 kg → drying 88 kg/loss 12 kg → unchanged-lot transfer → evidence/version-bound correction to 88 kg; receiving 20/19 → corrected 20 with original observation retained; unused receipt 30→29 with same code/new UUID; HOLD/isolation/evidence response/notification read retained HOLD and available 0. Scope denials and duplicate-code selector disambiguation were visually exercised. See [verification details](docs/ledger-implementation-plan.md#5-verification-and-acceptance); policies and evidence were explicitly synthetic, not factory approval.

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

`.env` excluded from image. Store runtime and migration-owner secrets in protected secret storage. Provision separate PostgreSQL roles before migration. Run one-shot image `--migrate` with migration-owner credentials and `Ledger:RuntimeRole`; migrations run Identity, Ledger, Capture order and finalize restricted runtime grants. Long-running app receives runtime credential only. Do not put migration-owner secret in normal app environment. Back up before migrations.

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
