# GrainLedger

GrainLedger is a rice supply-chain traceability platform. It connects identity and access control, shared product catalogs, procurement source records, and a protected ledger for receiving, stock, processing, and history.

Lot-to-lot tracing is available through Ledger → Traceability and the [audited trace API](docs/traceability-api.md). The [implementation plan](docs/traceability-implementation-plan.md) records current scope and deferred lab/export/container dependencies. New trace migration/runtime grants require explicit deployment; normal startup does not apply them.

## What it does

- **Identity:** Google and email/password sign-in, verified onboarding, session management, and administrator-managed organizations and permissions.
- **Catalog:** Shared commodities and measurement units used by procurement and ledger workflows.
- **Capture:** Farmer, plot, crop-season, procurement, physical samples, laboratory dispatch, and immutable external reports/results. Procurement records source expectations; it does not create usable stock. Laboratory confirmation checks transcription and identity, not market compliance.
- **Ledger:** Permission- and scope-controlled receiving, stock, processing, transfers, HOLDs, evidence, corrections, and history. Accepted arrivals create stock from measured mass.

The React web app supports desktop and mobile-sized screens. The mobile workspace is a placeholder, not a shipped mobile app. GrainLedger does not include customer settlement. Planned or documented future workflows are not implied to be available.

## Technology

- ASP.NET Core 10 modular monolith with PostgreSQL and Npgsql
- React, TypeScript, and Vite web app
- Docker image and Compose PostgreSQL service for local development

## Repository map

```text
backend/src/GrainLedger.Api/                  ASP.NET Core host
backend/src/GrainLedger.Modules.Identity/     Identity and access
backend/src/GrainLedger.Modules.Catalogs/     Shared catalog
backend/src/GrainLedger.Modules.Capture/      Farmer and procurement records
backend/src/GrainLedger.Modules.Ledger/        Receiving, stock and operations
backend/tests/                                Backend integration tests
apps/web/                                     React web app
apps/mobile/                                  Placeholder workspace
docs/                                         Setup, architecture and API guides
```

## Run locally

Requires .NET SDK 10, Node.js 24, and npm. Quickstart assumes PostgreSQL is already provisioned, its schema is migrated, and root `.env` contains working `ConnectionStrings__GrainLedger`, JWT, Google, and (for email/password flows) SMTP settings. For a fresh local database, role provisioning, configuration, and first migration, follow [Setup and deployment](docs/setup-and-deployment.md).

From repository root:

```bash
dotnet run --project backend/src/GrainLedger.Api
```

Then in another terminal:

```bash
npm ci
npm run dev --workspace @grainledger/web
```

Open the Vite URL, usually **http://localhost:5173**. In Development, API Scalar is at **http://localhost:5080/scalar/v1** and OpenAPI JSON at **http://localhost:5080/openapi/v1.json**. Use a Bearer access token in Scalar for protected API calls.

Normal startup does not migrate or seed the database. Do not run migration or seed commands against a shared database without explicit database-owner approval.

## Build and test

Run from repository root. Backend integration tests require Docker and use isolated PostgreSQL containers, not the configured shared database.

```bash
dotnet build GrainLedger.slnx
dotnet test GrainLedger.slnx --no-build
npm test
npm run build
```

## Documentation

- [Setup and deployment](docs/setup-and-deployment.md): local provisioning, configuration, migrations, seeds, and production deployment
- [Architecture and access control](docs/architecture.md): module boundaries, authorization, and data rules
- [Email/password authentication and Scalar](docs/auth-email-password-scalar.md)
- [Development accounts and demo Ledger data](docs/development-accounts.md)
- [Farmer API](docs/farmer-api.md) · [Procurement API](docs/procurement-api.md)
- [MRL CSV/Excel and YouTrack reference import](docs/mrl-file-import.md): substance setup, Vietnam draft limits, source provenance, and import safeguards
- [Container preparation API and integration boundary](docs/container-api.md) · [Loading/CLOSE implementation plan](docs/container-loading-implementation-plan.md)
- [Unified lot-code guide](docs/unified-lot-code-guide.md)
- [Laboratory workflow guide](docs/lab-flow-guide.md) · [Samples and external laboratory API](docs/sample-api.md#laboratory-report-routes) · [Laboratory workflow policy boundaries](docs/lab-flow-plan.md)
- [PostgreSQL diagrams and data dictionary](docs/dbdiagram/README.md)

For deployment, separate restricted runtime credentials from migration-owner credentials, use HTTPS behind a trusted reverse proxy, and protect persistent DataProtection keys. See [production deployment guidance](docs/setup-and-deployment.md#production-deployment).
