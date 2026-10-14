# GrainLedger

GrainLedger is a modular monolith for rice supply-chain traceability. This implementation provides Google sign-in, email/password registration with email OTP, JWT access/refresh tokens, password recovery, user profiles, organization management, server-enforced access control, and farmer registration and lookup APIs.

## Structure

```text
backend/src/GrainLedger.Api/                 Single ASP.NET Core 10 host
backend/src/GrainLedger.Modules.Identity/    Identity module, PostgreSQL schema, migrations
backend/src/GrainLedger.Modules.Capture/     Farmer API, PostgreSQL schema, migrations
backend/tests/GrainLedger.Api.IntegrationTests/
apps/web/                                  React + TypeScript + Vite
docs/architecture.md                        Module boundaries and authorization rules
```

The original six `Hello World` API projects have been replaced by one deployable backend. Future business modules run in this same process. The mobile workspace remains a placeholder; the React web app works on mobile screens. The farmer API contract is in [docs/farmer-api.md](docs/farmer-api.md).

In Development, the API serves Scalar at `http://localhost:5080/scalar/v1` and its OpenAPI document at `http://localhost:5080/openapi/v1.json`. Use an access token in Scalar's Bearer authentication field to test the farmer API.

## Run locally

Requires .NET SDK 10, Node.js 24, npm, and Docker (or an existing PostgreSQL server).

1. If you do not already have a `.env`, copy `.env.example` to `.env` at the repository root. Keep an existing `.env` instead of overwriting it.

2. Edit these settings in `.env`:

   ```dotenv
   POSTGRES_PASSWORD='YOUR_LOCAL_PASSWORD'
   ConnectionStrings__GrainLedger='Host=localhost;Port=5433;Database=grainledger;Username=grainledger;Password=YOUR_LOCAL_PASSWORD'
   Authentication__Google__ClientId='YOUR_GOOGLE_CLIENT_ID'
   Authentication__Google__ClientSecret='YOUR_GOOGLE_CLIENT_SECRET'
   Bootstrap__AdminEmail='YOUR_VERIFIED_GOOGLE_EMAIL'
   ```

   Use the same database password in both settings. Single quotes preserve literal `$` and `#` characters. If the database volume already exists, keep its existing password; editing `.env` does not change the password stored in PostgreSQL.

   The backend loads the root `.env` when launched from the repository or API project in Development. It no longer loads .NET user-secrets. Actual process environment variables and command-line settings override `.env`; clear any old shell overrides if you want `.env` to control them. Restart the backend after editing `.env`. Tests use isolated configuration instead of your local file. Do not prefix backend secrets with `VITE_` or commit `.env`.

   Start PostgreSQL:

   ```bash
   docker compose up -d postgres
   ```

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

## Email/password API and Scalar

See [the Vietnamese authentication and Scalar guide](docs/auth-email-password-scalar.md) for SMTP/JWT configuration, registration and verification, login, refresh, logout, password recovery, and example requests.

Run the database migrations, start the backend, and open **http://localhost:5080/scalar/v1** in Development. Login returns `accessToken`, `refreshToken`, and the user profile. Paste the access token into Scalar's Bearer field to test protected endpoints without browser cookies or CSRF tokens. SMTP must be configured for real verification/reset email delivery. The React UI supports both Google sign-in and email/password registration, verification, login, and password recovery. See [development accounts](docs/development-accounts.md) to seed six local role accounts and one demo customer organization.

## First administrator and account lifecycle

Configure `Bootstrap__AdminEmail` in `.env` before initial signup. The first matching newly provisioned Google account, or local account after email OTP verification, becomes administrator if no administrator exists. After setup, remove that line or set it to an empty value and restart the backend. Existing administrators retain their access.

Other people sign in through Google or complete local email verification and receive a pending profile with no business permissions. The administrator creates customer organizations and assigns each account an organization and role from **Administration**. Roles must match the organization's kind. Access changes revoke existing sessions; disabled accounts cannot sign in.

Profiles support display name and optional phone number; editing a profile cannot grant roles or change organizations. Local passwords are hashed and support recovery through email OTP; Google handles recovery for Google identities. Accounts from different providers are not automatically linked merely because they share an email address.

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

The `.env` file is excluded from the image. For deployment, mount a protected `.env` read-only at `/app/.env`, readable by the container user, and use a database host reachable from the container. This preserves the same quoted-value format used locally. Runtime environment variables can also supply or override settings.

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
