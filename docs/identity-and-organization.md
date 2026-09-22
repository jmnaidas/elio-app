# Phase 1: identity and organization

## Browser authentication

ELIO is a first-party SPA. ASP.NET Core Identity hashes passwords, generates account tokens, and authenticates through an HttpOnly cookie. There are no JWTs or credentials in localStorage/sessionStorage. Angular restores its Signals state from `GET /api/auth/session`; guards wait for that response before choosing login, verification, onboarding, or the workspace. A failed session lookup shows a retry page rather than claiming the user is signed out.

The authentication cookie is `Elio.Auth` on local HTTP development and `__Host-Elio.Auth` in production. It uses Path=/, SameSite=Lax, HttpOnly, and Secure in production. Local HTTP omits Secure; local HTTPS uses it. The ticket lifetime is eight hours with sliding expiration disabled. Identity validates the security stamp on every request. Logout changes that stamp and clears the cookie, revoking all existing sessions for that account; password reset also revokes them. Five failed sign-ins lock an account for 15 minutes. Authentication-sensitive endpoints share a 30 requests/minute limit per remote IP (`Account:RequestsPerMinute`). Deployments behind a proxy must configure trusted forwarding explicitly; untrusted forwarding headers are not used.

Unverified accounts may sign in but cannot create or access an organization. Verification is checked from the database, not a stale cookie claim. Registration, resend, and recovery responses use the same generic eligibility message for existing and nonexistent accounts; invalid login errors do not identify the failing credential.

## CSRF

All POST/PATCH and other unsafe requests under `/api` validate ASP.NET antiforgery protection, including anonymous login/registration. `GET /api/auth/antiforgery` sets the HttpOnly antiforgery cookie and a readable `XSRF-TOKEN` request-token cookie. Angular's standard XSRF interceptor sends `X-XSRF-TOKEN` on same-origin mutations. The session service refreshes protection before each mutation so login/logout and changes in another tab do not reuse a token for the wrong identity. Failed mutations are not automatically replayed. Both cookies use Secure outside Development; the underlying antiforgery cookie uses SameSite=Strict.

Use the same origin for the SPA and `/api`; Angular's development proxy preserves the `/api` prefix. Do not disable antiforgery to test an endpoint. A script must retain cookies, obtain a request token, and send it in the header. API responses are not cached. No open CORS policy is configured.

## Account email in development

`IAccountEmailSender` is the Application boundary. The Development implementation captures each account email as an ignored JSON file in `artifacts/dev-mail/` with recipient, purpose, and link. No SMTP server is needed. These files contain sensitive one-hour Identity tokens; keep them local and delete obsolete captures. They are never served by the API or frontend and are never emitted to ordinary logs.

1. Register in the browser.
2. Open the matching JSON file under `artifacts/dev-mail/` and open its `link` in the browser.
3. Click **Verify email**, then sign in and create your workspace.
4. To test recovery, use **Forgot password**, then open the new `reset-password` capture and submit a new password.

Links carry tokens in the URL fragment so HTTP access logs and Referer headers do not receive them. Angular consumes the fragment into component memory and removes it from the address bar. If the page is refreshed before submitting, reopen the original email link. No token is stored in browser storage, and verification is not automatic. Reset tokens cannot be reused after a successful reset.

Optional public settings: `AccountEmail:FrontendOrigin` (default `http://localhost:4200` in Development), and `AccountEmail:CaptureDirectory` (defaults to the ignored repository artifacts directory). Do not place the capture directory under any public/static web root.

There is deliberately no production email provider in Phase 1. Outside Development the capture sender rejects all email-dependent registration/resend/recovery requests with the same 503 before account lookup. A deployment must register a real provider and configure an HTTPS frontend origin. Production must also supply TLS, durable protected Data Protection keys (shared between replicas), secure database credentials, and deployment-specific host/proxy configuration. No startup migration or automatic account verification is enabled.

## Tenant boundary

`ApplicationUser → Membership → Organization`. The Domain contains Organization and Membership; Identity types remain in Infrastructure. Organization stores its name, an IANA timezone (for example `Asia/Manila` or `America/New_York`), PHP or USD default currency, UTC timestamps, and an optimistic concurrency version. No FX conversion exists.

Membership has its own ID, user/organization IDs, role, and creation timestamp. The initial role is Owner; the model supports future additional roles without attaching business data to a user. There is a unique `(UserId, OrganizationId)` index. Membership foreign keys restrict organization/user deletion. Identity's own subordinate tables retain framework-standard cascades. There is no deletion endpoint.

`ICurrentOrganization` resolves the authenticated user and a live database membership once per request. With no selection header, the earliest membership is selected. An optional `X-Organization-ID` selector is accepted only when the authenticated user has that membership. A forged or malformed selector returns 403. It never grants authority by itself. The Angular Phase 1 UI does not send this header.

Reusable Verified, Member, and Owner authorization policies are defined in the API. Organization application services independently scope access by user, organization, and Owner role. Future business queries must use the resolved organization ID and retain a server-side membership boundary; EF global filters alone are not authorization.

Onboarding creates the organization and Owner membership in one PostgreSQL transaction. It locks the user's row before checking membership, preventing concurrent onboarding requests from creating duplicate workspaces. The database still supports multiple memberships for future phases. Updates require the current version and return 409 for a stale edit; Settings offers a reload action.

## API

| Endpoint | Access and behavior |
| --- | --- |
| GET `/api/auth/antiforgery` | Public; obtains request protection |
| GET `/api/auth/session` | Public; anonymous session has null user/organization/membership |
| POST `/api/auth/register` | Public + CSRF; generic email response |
| POST `/api/auth/login` | Public + CSRF; establishes Identity cookie |
| POST `/api/auth/logout` | Authenticated + CSRF; revokes sessions |
| POST `/api/auth/verify-email` | Public + CSRF; consumes Identity verification token |
| POST `/api/auth/resend-verification` | Public + CSRF; generic email response |
| POST `/api/auth/forgot-password` | Public + CSRF; generic email response |
| POST `/api/auth/reset-password` | Public + CSRF; Identity reset token and matching passwords |
| POST `/api/organization` | Verified account + CSRF; atomic onboarding |
| GET `/api/organization` | Verified Owner with validated membership |
| PATCH `/api/organization` | Verified Owner + CSRF + current version |

Requests use camelCase JSON. Validation and domain failures use ProblemDetails. Framework authentication failures return 401/403, never HTML redirects. OpenAPI remains Development-only.

## Migrations and tests

From the repository root with .NET 10 on PATH and the existing development User Secret configured:

```powershell
dotnet restore backend/Elio.sln
dotnet build backend/Elio.sln --no-restore
Push-Location backend
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool run dotnet-ef database update --project src/Elio.Infrastructure --startup-project src/Elio.Api
Pop-Location
dotnet test backend/Elio.sln --no-build
```

The migration was generated with `dotnet tool run dotnet-ef migrations add IdentityAndOrganization --project src/Elio.Infrastructure --startup-project src/Elio.Api --output-dir Persistence/Migrations`. The pinned 10.0.3 design/tool package is compatible with the 10.0.12 runtime used here; it emits an older-tools notice. Applying an already applied migration is a no-op.

Real PostgreSQL integration tests use `ELIO_TEST_DATABASE`, or fall back to the API's existing User Secret. They create a unique `elio_test_<guid>` schema, apply migrations there, and drop only that schema afterward. The role needs schema creation permission. Tests fail rather than silently skip if PostgreSQL is unavailable. A dedicated test database is recommended in CI. An interrupted test run may leave an isolated test schema for manual cleanup; development tables are never dropped.

Coverage includes real Identity registration/verification/login/logout/recovery, generic responses, cookie/session behavior, CSRF failure and success, anonymous/unverified rejection, organization validation/update concurrency, Owner membership, transactional rollback, and User A attempting to read/update Organization B. Angular tests cover session restoration, guard destinations, auth state changes, settings form/version behavior, and the retained shell. Architecture tests retain Phase 0 dependency rules.
