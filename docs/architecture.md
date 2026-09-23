# Architecture decisions

- Modular monolith with Domain <- Application <- Infrastructure and API as the composition root. Domain contains Organization and Membership; ASP.NET Identity stays in Infrastructure.
- Dependencies are pinned in project/package manifests and the frontend lockfile. The SDK is selected by backend/global.json.
- PostgreSQL persistence uses an Identity-backed ElioDbContext with organization/membership mappings and an explicit initial migration. No EnsureCreated or startup schema mutation is included.
- `/health` reports process liveness independent of PostgreSQL. `/health/ready` probes PostgreSQL and returns 503 when unavailable. Health responses omit sensitive connection/exception details.
- JSON console logs carry request correlation and W3C trace IDs. Incoming correlation IDs are restricted to 64 ASCII alphanumeric, hyphen or underscore characters. Invalid IDs are replaced. These IDs are diagnostic data, never authentication.
- Standard ASP.NET request Activities plus the stable Elio.Api ActivitySource provide an OpenTelemetry extension point. No exporter or collection backend is configured; this is readiness for instrumentation, not a claim of distributed tracing deployment.
- Unexpected exceptions and empty HTTP errors use ProblemDetails with correlation metadata. Secrets do not belong in error responses.
- OpenAPI JSON is exposed only in Development at `/openapi/v1.json`. Account and organization endpoints are documented there.
- Angular is standalone with lazy feature routes and a central Signals session service. Native dialog supplies modal navigation focus handling. Locally, `/api/*` is proxied to ASP.NET with the prefix preserved. Production must supply an equivalent same-origin proxy for cookie and XSRF handling.
- Shared SCSS tokens establish a light neutral base, restrained Amethyst, selected Keylime highlights and distinct semantic colors. Clients and Services use responsive lists and native detail/edit dialogs. There are no charts or seeded financial data.

## Test boundaries

Unit tests validate correlation input and organization rules. Integration tests run the actual API pipeline and real PostgreSQL in unique temporary schemas, covering Identity, membership isolation, CSRF and transaction behavior. Set `ELIO_TEST_DATABASE` to a dedicated PostgreSQL connection string, or use the existing API User Secret. The separate readiness-failure test retains a deliberately unreachable port by default.

Architecture tests enforce project-reference boundaries. Frontend tests exercise session restoration, route guards, login/logout state, onboarding decisions, settings form versions, shell collapse and API-only correlation headers.

See [Phase 1 identity and organization](identity-and-organization.md) for cookie/CSRF security, account email, tenancy, migrations, and production prerequisites. Financial business modules remain future work.

Phase 2 adds organization-owned Client/Service entities, feature application contracts, EF-backed feature services, Member-protected controllers, and PostgreSQL tenant tests. A small shared catalog access helper revalidates membership; it is not a generic repository. All record lookups include the organization ID. See [Clients and Services](clients-and-services.md) for decimal precision, deactivation, API contracts, migration setup, and the future invoice snapshot boundary.
