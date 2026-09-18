# Phase 0 decisions

- Modular monolith with Domain <- Application <- Infrastructure and API as the composition root. No domain types exist yet.
- Dependencies are pinned in project/package manifests and the frontend lockfile. The SDK is selected by backend/global.json.
- PostgreSQL persistence uses ElioDbContext. No model, migration, EnsureCreated or startup schema mutation is included.
- `/health` reports process liveness independent of PostgreSQL. `/health/ready` probes PostgreSQL and returns 503 when unavailable. Health responses omit sensitive connection/exception details.
- JSON console logs carry request correlation and W3C trace IDs. Incoming correlation IDs are restricted to 64 ASCII alphanumeric, hyphen or underscore characters. Invalid IDs are replaced. These IDs are diagnostic data, never authentication.
- Standard ASP.NET request Activities plus the stable Elio.Api ActivitySource provide an OpenTelemetry extension point. No exporter or collection backend is configured; this is readiness for instrumentation, not a claim of distributed tracing deployment.
- Unexpected exceptions and empty HTTP errors use ProblemDetails with correlation metadata. Secrets do not belong in error responses.
- OpenAPI JSON is exposed only in Development at `/openapi/v1.json`. No business endpoints or authentication exist.
- Angular is standalone with lazy feature routes. Signals are used only for shell state. Native dialog supplies modal navigation focus handling. Locally, `/api/*` is proxied to ASP.NET with the prefix removed. Production must supply an equivalent same-origin proxy or an explicitly configured API URL and restricted CORS policy.
- Shared SCSS tokens establish a light neutral base, restrained Amethyst, selected Keylime highlights and distinct semantic colors. No business components, charts or seeded financial data exist.

## Test boundaries

Unit tests validate correlation input. Integration tests run the actual API pipeline, EF provider and empty model; readiness defaults to a deliberately unreachable port. Set `ELIO_TEST_DATABASE` to a dedicated running PostgreSQL connection string to verify the successful readiness path. Tests do not create or modify a schema.

Architecture tests inspect project references so dependency boundaries are enforced even while Domain and Application have no types. Frontend tests exercise routes, shell collapse and API-only correlation headers.

Future phases will add authentication, organization isolation and real use cases. Placeholder folders document ownership, not implementations.
