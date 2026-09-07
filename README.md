# RUN LEAGUE

Milestone 1: a local ASP.NET Core 10 API, EF Core/SQLite persistence and React/TypeScript connection screen. Implements Strava OAuth, encrypted token storage, a browser session and refresh-token rotation. See [plan.md](plan.md) for the larger MVP.

## Prerequisites

- .NET 10 SDK (10.0.200 or newer feature band)
- Node.js 22 LTS (22.12+) with npm
- Your Strava account and a Strava API application

## Register your Strava application

Open https://www.strava.com/settings/api while signed in and register RUN LEAGUE. Strava may require a name, category, club/website information and an application icon; complete these in your account. Set the **Authorization Callback Domain** to `localhost`. The full callback used here is `https://localhost:7043/api/strava/callback`.

Record your client ID and client secret privately. Find your own athlete ID in your Strava profile URL (`/athletes/123456`). The app requires this ID and refuses other athletes: this is a single-developer prototype. Do not paste your secret or tokens into chat, issues or committed files.

Strava registration and real consent must be completed by the account owner. No credentials are included in this repository. See the [official authentication documentation](https://developers.strava.com/docs/authentication/).

## Configure and run

From the repository root:

```powershell
dotnet dev-certs https --trust
dotnet user-secrets set "Strava:ClientId" "YOUR_CLIENT_ID" --project src/RunLeague.Api
dotnet user-secrets set "Strava:ClientSecret" "YOUR_CLIENT_SECRET" --project src/RunLeague.Api
dotnet user-secrets set "Strava:AllowedAthleteId" "YOUR_ATHLETE_ID" --project src/RunLeague.Api
dotnet restore
cd src/runleague-web
npm install
npm run build
cd ../..
dotnet run --project src/RunLeague.Api --launch-profile https
```

Open **https://localhost:7043**. The API serves the built React application on the same HTTPS origin, so cookies and OAuth callbacks work without CORS configuration. Click **Connect with Strava**, allow the requested read scopes and return to the connected athlete screen. **Check connection** makes a real Strava profile request, refreshing tokens if needed.

Migrations apply on startup. The SQLite file is `src/RunLeague.Api/App_Data/runleague.db` and is ignored by Git. Restarting the application preserves the athlete and connection.

Environment variable equivalents are `Strava__ClientId`, `Strava__ClientSecret`, `Strava__AllowedAthleteId`, `Strava__CallbackUrl` and `ConnectionStrings__RunLeague`. User secrets load with the Development launch profile. Never put server secrets in frontend environment variables.

The default read scopes are `read,activity:read`. To include activities visible only to you in later ingestion milestones, configure `Strava:Scopes` as `read,activity:read_all` and reconnect. No write scopes are requested.

For frontend edits, rebuild with `npm run build` and reload the HTTPS API page. Vite's dev server is available for UI-only work, but use the HTTPS API origin for the complete OAuth flow.

## Validation

```powershell
dotnet test
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/RunLeague.Api
cd src/runleague-web
npm run build
```

Integration tests use isolated SQLite databases, ephemeral encryption keys and a fake Strava HTTP handler. They exercise real API routes, session cookies and migrations without external credentials. GitHub Actions runs the same checks.

After configuring your real account:
1. Connect and check that the correct athlete appears.
2. Restart the API and verify the saved connection remains.
3. Click Check connection and verify it succeeds.
4. Reconnect and verify the same athlete/connection is updated.
5. Cancel Strava consent and verify a useful retry message.
6. After token expiry, use Check connection again to exercise real refresh.

## Security and operational boundaries

Tokens are encrypted with ASP.NET Core Data Protection before storage. Its default key persistence uses the current user's profile; on Windows it protects stored keys with DPAPI. Keep that profile/key ring stable across restarts. On other operating systems, configure a persistent key ring with encryption at rest before storing real credentials. Losing the key ring requires reconnecting accounts. For a future hosted deployment, configure an appropriate shared encrypted key store before running the app.

OAuth uses a cryptographically random state, a Secure/HttpOnly browser correlation cookie and a hashed database attempt, valid for ten minutes and consumed atomically once. Session cookies are Secure/HttpOnly; authenticated POSTs require antiforgery tokens. API responses are not cached. Strava request logging is disabled and upstream error bodies are never returned. Avoid enabling request/query/body logging around OAuth endpoints.

The local process serializes token exchanges/refreshes and reloads persisted tokens under the lock. Multi-instance hosting requires a distributed lease. A revoked/invalid refresh marks the connection as needing reconnection; temporary network failures and rate limits preserve saved credentials. Automatic retries are deliberately omitted for token exchanges.

Sign out only removes this browser's session; it does not revoke Strava access or delete stored data. Account disconnection/deletion, privacy and retention must be completed before external testers.

## Scope

Implemented: application skeleton, connection page, OAuth/callback, athlete and connection persistence, migrations, secure session and refresh service.

Not part of milestone 1: webhook registration, initial activity import, streams, routes or competitions. The next milestones add those. No cloud resources or webhook subscription are created here.
