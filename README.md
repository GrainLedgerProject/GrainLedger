# GrainLedger

GrainLedger is a modular monolith for rice supply-chain traceability. This implementation provides Google sign-in, session management, user profiles, organization management, and server-enforced access control.

## Structure

```text
backend/src/GrainLedger.Api/                 Single ASP.NET Core 10 host
backend/src/GrainLedger.Modules.Identity/    Identity module, PostgreSQL schema, migrations
backend/tests/GrainLedger.Api.IntegrationTests/
apps/web/                                  React + TypeScript + Vite
docs/architecture.md                        Module boundaries and authorization rules
```

The original six `Hello World` API projects have been replaced by one deployable backend. Future business modules run in this same process. The mobile workspace remains a placeholder; the React web app works on mobile screens.

## Run locally

Requires .NET SDK 10, Node.js 24, npm, and Docker (or an existing PostgreSQL server).

1. Copy `.env.example` to `.env` and set a local database password. Start PostgreSQL:

   ```bash
   cp .env.example .env
   # Edit POSTGRES_PASSWORD in .env before continuing.
   docker compose up -d postgres
   ```

2. Store backend configuration in .NET user-secrets. Replace the placeholders below. The database password must match `.env`:

   ```bash
   dotnet user-secrets set 'ConnectionStrings:GrainLedger' 'Host=localhost;Port=5433;Database=grainledger;Username=grainledger;Password=YOUR_LOCAL_PASSWORD' --project backend/src/GrainLedger.Api
   dotnet user-secrets set 'Authentication:Google:ClientId' 'YOUR_GOOGLE_CLIENT_ID' --project backend/src/GrainLedger.Api
   dotnet user-secrets set 'Authentication:Google:ClientSecret' 'YOUR_GOOGLE_CLIENT_SECRET' --project backend/src/GrainLedger.Api
   dotnet user-secrets set 'Bootstrap:AdminEmail' 'YOUR_VERIFIED_GOOGLE_EMAIL' --project backend/src/GrainLedger.Api
   ```

   `.env` is consumed by Docker Compose; .NET does not automatically load it. Do not put the Google client secret in a `VITE_` variable or commit it.

3. Create a **Web application** OAuth client in Google Cloud and configure the consent screen. In testing mode, add your Google accounts as test users. Register this exact authorized redirect URI:

   ```text
   http://localhost:5173/signin-google
   ```

   Development requests go through Vite on port 5173, which preserves the public host and proxies `/api` and `/signin-google` to the backend on port 5080. Use `localhost` consistently, not `127.0.0.1`. For production, register `https://YOUR_HOST/signin-google` as well. Setup follows the [ASP.NET Core Google authentication documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0).

4. Apply the checked-in migrations, then start the backend:

   ```bash
   dotnet run --project backend/src/GrainLedger.Api -- --migrate
   dotnet run --project backend/src/GrainLedger.Api
   ```

5. In another terminal, start React:

   ```bash
   npm ci
   npm run dev
   ```

   Open **http://localhost:5173**.

## First administrator and account lifecycle

Sign in with the verified Google email configured in `Bootstrap:AdminEmail` to initialize the first administrator. Remove that bootstrap setting after initial setup:

```bash
dotnet user-secrets remove 'Bootstrap:AdminEmail' --project backend/src/GrainLedger.Api
```

Other people sign in through Google and receive a pending profile with no business permissions. The administrator creates customer organizations and assigns each account an organization and role from **Administration**. Roles must match the organization's kind. Access changes revoke existing sessions; disabled accounts cannot sign in.

Profiles support display name and optional phone number. Email and Google identity are managed by Google; editing a profile cannot grant roles or change organizations. Google handles password changes, recovery, and multifactor authentication. GrainLedger does not maintain a second password database or implement the older SRS's local-password lockout flow.

## Checks

If Google sign-in fails, the sign-in page shows a reason and a reference that matches a backend warning under `GrainLedger.Authentication.Google`. Check that warning for the failure category. Callback logs omit Google tokens, profile claims, and callback query strings. After changing backend authentication code, restart the backend before starting a fresh sign-in attempt.

The default Google user-info endpoint uses the v3 fields `sub` and `email_verified`; verification must be mapped from `email_verified`, not the older `verified_email` field. The integration-test provider response follows that same contract.

```bash
dotnet build GrainLedger.slnx
dotnet test GrainLedger.slnx
npm test
npm run build
```

Backend integration tests start an isolated PostgreSQL container using Testcontainers; Docker must be running. Test identities are confined to the test assembly. The application has no development login bypass. Live Google consent requires your own OAuth credentials and is a separate manual check.

## Deployment

```bash
docker build -t grainledger:local .
```

The image builds React into the .NET host's `wwwroot`, so production serves the UI and API from one origin. Place it behind an HTTPS reverse proxy. Configure:

| Environment variable | Purpose |
| --- | --- |
| `ConnectionStrings__GrainLedger` | PostgreSQL connection string |
| `Authentication__Google__ClientId` | Google Web application client ID |
| `Authentication__Google__ClientSecret` | Google client secret |
| `Bootstrap__AdminEmail` | First administrator's verified email; remove after setup |
| `ReverseProxy__KnownProxies__0` | Actual trusted reverse proxy IP; add indexed entries as needed |
| `DataProtection__KeysPath` | Writable persistent volume for cookie encryption keys |
| `AllowedHosts` | Public application hostname |

Run the image once with `--migrate` using the target database configuration before starting the application. Runtime startup does not silently migrate production data. Back up the database before applying future migrations. Keep data-protection keys on a protected persistent volume owned by the container user, and use the hosting platform's encryption at rest. Forward the original host and `X-Forwarded-Proto: https` from the configured proxy. Production cookies require HTTPS.

The Compose file starts only the local database. It does not deploy a production proxy or provision Google credentials.
