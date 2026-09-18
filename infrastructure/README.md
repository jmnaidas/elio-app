# Local infrastructure

Run PostgreSQL with the root Docker Compose file and Angular/.NET on the host. PostgreSQL is bound to loopback, persists in a named volume, and has a `pg_isready` health check. API readiness separately authenticates and connects through EF Core.

`start-api.ps1` reads the four POSTGRES values from the ignored root `.env` (simple unquoted values) when `ConnectionStrings__Database` is not already set. The connection string stays in the launched process environment. It supports the optional ignored workspace SDK used during initial verification.

Changing POSTGRES_PASSWORD after the volume is initialized does not change the existing database role's password. Update the role deliberately rather than deleting a populated volume.

Full application containers, deployment TLS and an ingress/reverse proxy are future deployment concerns. Do not expose the development API or PostgreSQL directly on a public network.
