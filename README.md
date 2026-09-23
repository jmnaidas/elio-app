# ELIO

**Know what's due. Know what's next.**

ELIO — Exceptions · Ledger · Invoicing · Operations — is an invoicing and receivables workspace for small service businesses.

**Status: Phase 3 — Draft Invoice Engine.** Tenant-scoped clients and services now support draft invoice creation and editing, decimal-safe totals, ordered lines, and a live preview. Authentication, onboarding, settings, and catalog workflows are preserved. Finalization, invoice numbering, PDF, payments, and receivables remain future work; no fake financial data is shown.

## Stack

- Angular 22, standalone components, TypeScript, local Signals, SCSS
- ASP.NET Core / .NET 10, EF Core, Npgsql, PostgreSQL 17
- REST/OpenAPI, JSON structured logs, ProblemDetails, correlation IDs
- xUnit backend tests; Angular/Vitest frontend tests
- Docker Compose for PostgreSQL; frontend and API run locally

## Repository

```text
frontend/                     Angular workspace (no nested application)
backend/
  Elio.sln
  src/
    Elio.Api/                 HTTP pipeline and composition root
    Elio.Application/         Account/organization contracts; references Domain
    Elio.Domain/              Organization and membership rules; no dependencies
    Elio.Infrastructure/      Identity, EF Core, migrations and email capture
  tests/
    Elio.UnitTests/
    Elio.IntegrationTests/
    Elio.ArchitectureTests/
infrastructure/               Local launch helper and infrastructure notes
docs/                         Concise architecture decisions
docker-compose.yml
.env.example
```

## Prerequisites

- .NET 10 SDK (backend/global.json permits the latest installed 10.0 feature band)
- Node.js 24.15 or newer in the 24.x line and npm 11
- Docker Desktop with Compose and Linux containers for PostgreSQL
- PowerShell for the optional launch helper

The initial build used Node 24.19 and .NET SDK 10.0.401. A workspace-local SDK may exist at `.tools/dotnet`; it is ignored and is not required on another machine.

## Local development

Run commands from the repository root unless otherwise stated.

1. Configure PostgreSQL:

```powershell
if (!(Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
docker compose up -d postgres
docker compose ps
```

Use simple unquoted values in `.env` and replace the example password with a local value. Never commit `.env`. Compose reads this file; ASP.NET does not automatically read it.

PostgreSQL uses host port **5433** (container port 5432). Keep your existing `.env` and User Secrets; do not replace them when upgrading this checkout.

2. Configure User Secrets only if this is a new checkout:

```powershell
dotnet user-secrets set 'ConnectionStrings:Database' 'Host=127.0.0.1;Port=5433;Database=elio;Username=elio;Password=YOUR_LOCAL_PASSWORD;Timeout=5' --project backend/src/Elio.Api
```

3. Apply the migration, then start the API in its own terminal:

```powershell
dotnet restore backend/Elio.sln
Push-Location backend
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool run dotnet-ef database update --project src/Elio.Infrastructure --startup-project src/Elio.Api
Pop-Location
dotnet run --project backend/src/Elio.Api --launch-profile http
```

The existing `infrastructure/start-api.ps1` remains an alternative for `.env`-based configuration; it supplies an environment connection string instead of using User Secrets. Neither startup method applies migrations automatically.

4. Start Angular in another terminal:

```powershell
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. Angular proxies `/api/*` to the local API through `proxy.conf.json`, preserving `/api`. Production needs a same-origin `/api` reverse proxy and SPA fallback to index.html. Keep the API URL relative for Angular's XSRF protection.

Register, open the matching verification link captured in ignored `artifacts/dev-mail/*.json`, click **Verify email**, and sign in to set up your organization. Forgot/reset password uses the same local email capture. Tokens expire after one hour and are never automatically confirmed. See [Phase 1 authentication, CSRF, tenancy and test details](docs/identity-and-organization.md).

## Health and OpenAPI

```powershell
Invoke-RestMethod http://localhost:5080/health
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod http://localhost:5080/openapi/v1.json
```

- `/health`: 200 while the process is serving; does not require PostgreSQL.
- `/health/ready`: 200 when PostgreSQL can be reached with configured credentials, otherwise 503. No schema is created.
- `/openapi/v1.json`: development-only OpenAPI document for health, account, organization, catalog, and draft invoice operations.
- Responses carry `X-Correlation-ID`; errors include ProblemDetails correlation/trace metadata.

## Build and tests

```powershell
dotnet restore backend/Elio.sln
dotnet build backend/Elio.sln --no-restore
dotnet test backend/Elio.sln --no-build
cd frontend
npm ci
npm run build
npm test -- --watch=false
```

If using the workspace-local SDK in this checkout, first run these commands from the repository root:

```powershell
$env:PATH = "$PWD/.tools/dotnet;$env:PATH"
$env:DOTNET_ROOT = "$PWD/.tools/dotnet"
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
$env:NUGET_PACKAGES = "$PWD/.tools/nuget"
```

Identity/organization integration tests require real PostgreSQL. They use `ELIO_TEST_DATABASE` or the API's existing User Secret, create an isolated schema, migrate it, and remove that test schema afterward. The role needs permission to create schemas. The separate readiness failure test retains its intentionally unreachable database unless `ELIO_TEST_DATABASE` is supplied.

## Architecture

The API composes Application and Infrastructure. Application references Domain; Infrastructure references Application. Domain has no external dependencies. This is one application, not multiple services. Native EF Core, ASP.NET DI and Angular routing are used without custom repository or messaging frameworks.

See [architecture decisions](docs/architecture.md), [Phase 1 details](docs/identity-and-organization.md), [Clients and Services](docs/clients-and-services.md), [Draft Invoice Engine](docs/invoice-engine.md), and [local infrastructure](infrastructure/README.md). ELIO uses Identity HttpOnly cookies because this is a first-party browser application; there are no browser-stored JWTs. Every organization request validates a live membership. Apply all migrations, including DraftInvoices, using the existing migration command before using Phase 3.

## Stop local services

Use Ctrl+C in the frontend/API terminals. `docker compose stop` stops PostgreSQL without removing its data. Do not remove the volume unless you explicitly intend to delete the local database.
