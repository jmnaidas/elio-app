# ELIO

**Know what's due. Know what's next.**

ELIO — Exceptions · Ledger · Invoicing · Operations — is an invoicing and receivables workspace for small service businesses.

**Status: Phase 0 foundation.** This repository contains a working application shell and API infrastructure. Authentication, business entities, financial workflows, PDFs and email delivery are intentionally not implemented. The interface contains no fake financial data.

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
    Elio.Application/         Future use cases; references Domain
    Elio.Domain/              Future business rules; no dependencies
    Elio.Infrastructure/      EF Core and dependency health checks
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
Copy-Item .env.example .env
notepad .env
docker compose up -d postgres
docker compose ps
```

Use simple unquoted values in `.env` and replace the example password with a local value. Never commit `.env`. Compose reads this file; ASP.NET does not automatically read it.

2. Start the API in its own terminal:

```powershell
./infrastructure/start-api.ps1
```

The helper reads `.env`, configures the connection string and runs the API at `http://localhost:5080`. It uses the ignored workspace SDK if present, otherwise installed `dotnet`.

Alternatively, configure the connection string directly with an environment variable or .NET user secrets:

```powershell
$env:ConnectionStrings__Database = 'Host=127.0.0.1;Port=5432;Database=elio;Username=elio;Password=YOUR_LOCAL_PASSWORD;Timeout=5'
dotnet run --project backend/src/Elio.Api --launch-profile http
```

3. Start Angular in another terminal:

```powershell
cd frontend
npm ci
npm start
```

Open `http://localhost:4200`. Angular proxies `/api/*` to the local API through `proxy.conf.json`, removing `/api`. The configurable API base URL lives in `src/environments/`; environment files are public build configuration, never secret storage. Production needs a same-origin `/api` reverse proxy and SPA fallback to index.html.

## Health and OpenAPI

```powershell
Invoke-RestMethod http://localhost:5080/health
Invoke-RestMethod http://localhost:5080/health/ready
Invoke-RestMethod http://localhost:5080/openapi/v1.json
```

- `/health`: 200 while the process is serving; does not require PostgreSQL.
- `/health/ready`: 200 when PostgreSQL can be reached with configured credentials, otherwise 503. No schema is created.
- `/openapi/v1.json`: development-only OpenAPI document. It has no business operations yet.
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

Integration tests default to an unreachable local database and verify that readiness fails honestly. To test real PostgreSQL success, set `ELIO_TEST_DATABASE` to a dedicated test connection string before running them. No migrations or business tables exist in Phase 0.

## Architecture

The API composes Application and Infrastructure. Application references Domain; Infrastructure references Application. Domain has no external dependencies. This is one application, not multiple services. Native EF Core, ASP.NET DI and Angular routing are used without custom repository or messaging frameworks.

See [architecture decisions](docs/architecture.md) and [local infrastructure](infrastructure/README.md). Authentication, tenant data isolation and business behavior begin in later phases.

## Stop local services

Use Ctrl+C in the frontend/API terminals. `docker compose stop` stops PostgreSQL without removing its data. Do not remove the volume unless you explicitly intend to delete the local database.
