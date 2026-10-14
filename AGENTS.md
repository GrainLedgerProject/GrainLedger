# AGENTS.md

Instructions for coding agents working anywhere in this repository.

## Project map

GrainLedger is a modular monolith: ASP.NET Core 10, PostgreSQL, and React/TypeScript with Vite.

- `backend/src/GrainLedger.Api/`: application host and module composition.
- `backend/src/GrainLedger.Modules.Identity/`: authentication, sessions, organizations, permissions, and `identity` schema.
- `backend/src/GrainLedger.Modules.Catalogs/`: Catalog APIs and catalog schema.
- `backend/src/GrainLedger.Modules.Capture/`: official farmer/procurement APIs and `capture` schema.
- `backend/tests/GrainLedger.Api.IntegrationTests/`: backend integration tests using isolated Docker/Testcontainers PostgreSQL.
- `apps/web/`: working web UI; `apps/mobile/` remains a placeholder.

Read `README.md` for setup and `docs/architecture.md` for module, authorization, transaction, and numeric contracts. Read `docs/ledger-implementation-plan.md` when changing ledger workflows. Authentication setup lives in `docs/auth-email-password-scalar.md`; seed-account instructions live in `docs/development-accounts.md`.

## Development commands

Prerequisites: .NET SDK 10 (see `global.json`), Node.js 24, npm, and Docker for backend integration tests.

From repository root:

```bash
# Start backend and frontend in separate terminals.
dotnet run --project backend/src/GrainLedger.Api
npm run dev

# Build and test.
dotnet build GrainLedger.slnx
dotnet test GrainLedger.slnx --no-build
npm test
npm run build
```

Use `http://localhost:5173`; Vite proxies `/api` and `/signin-google` to backend port 5080. Normal backend startup does not migrate or seed the database. Do not run migrations or seeds as part of routine startup or verification.

## Database and secret safety

- Preserve existing `.env` and connection settings. The team uses Supabase; do not silently switch to local PostgreSQL or create/reset a replacement database.
- Treat configured Supabase databases as shared. Obtain explicit approval before running migrations, seeds, schema/role/grant changes, or data-changing smoke scenarios against them. Use isolated test containers for write-based verification.
- `ConnectionStrings__GrainLedger` is the restricted runtime connection. `ConnectionStrings__GrainLedgerMigration` is migration-owner access, not normal application access. Never bypass runtime-role validation or use elevated credentials to make application startup succeed.
- Never print or commit credentials, tokens, `.env`, or real evidence files. Never expose backend secrets through `VITE_` variables.
- Capture owns official source records; it has no arrival-observation or receipt-correction endpoints. Do not convert historical synthetic records automatically or remove existing Ledger stock/history.
- Obtain explicit owner approval before shared database migrations, seeds, schema/role/grant changes or data-changing checks.

## Correctness boundaries

- Each module owns its schema and migrations. Use existing in-process contracts; do not pass EF entities across module boundaries.
- Source/stock changes must share the existing connection and transaction, acquire the existing advisory lock before source/stock rows, and retain `READ COMMITTED` semantics. Module routines must not commit their caller's transaction.
- Enforce persisted active-role, permission, owner, and facility scope on the backend. UI visibility is not authorization. Administrator is not a wildcard business role; never trust request-supplied actor/confirmer IDs.
- Mass crosses the API as decimal strings and uses exact kg `numeric(20,6)`. Preserve original quantity/unit, precision, and deterministic lineage allocation; never use JavaScript floating-point arithmetic for mass accounting.
- Committed source records, original observations, evidence, and postings are immutable. Corrections append reviewed reversal/replacement records; do not overwrite history or release HOLD implicitly.
- Missing business policy must fail closed. Do not invent tolerances, blend compatibility, transport adjustments, or ownership-transfer rules.
- Read optimizations must preserve freshness and scope. Prefer bulk queries and parallel independent requests over per-record fetches. Do not add response caching without explicit freshness/invalidation requirements.

## Change and verification rules

- Make the smallest complete change. Reuse existing dependencies and local patterns; avoid speculative abstractions or unrelated refactors.
- Follow Identity's feature-based layout documented in `docs/architecture.md`; retain existing namespaces and generated migration history when moving code.
- Update affected callers, contracts, and documentation together. Preserve unrelated user changes.
- Add focused regression coverage for behavior, authorization, boundaries, or accounting invariants. Avoid tests that only mirror implementation details.
- Run relevant checks after implementation. For behavioral changes, also exercise the actual API/CLI/browser path; tests alone do not prove the UI or runtime works. Do not mutate shared Supabase data for this smoke check.
- Report only checks actually run and distinguish warm endpoint timings from cold connections or whole-page loading. React StrictMode replays mount effects in Development; use a production build for production loading measurements.
- Documentation-only changes need file/link validation, not application rebuilds.

## Commits

Use Conventional Commit titles: `type(scope): concise description`.

Examples:

```text
feat(capture): add development mock data seed
fix(auth): preserve session revocation checks
perf(ledger): batch scoped database fetches
docs(agents): add repository coding guidance
```

Choose a scope matching the change (`auth`, `capture`, `ledger`, `web`, `api`, or `agents`). Commit or push only when requested.
