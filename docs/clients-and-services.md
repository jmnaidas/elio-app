# Phase 2 — Clients and Services

Clients and services are organization-owned master data for the future invoice engine. No invoicing, financial calculations, PDFs, payments, reminders, or invoice email delivery are implemented in this phase.

## Models and validation

**Client** stores a UUID, immutable OrganizationId, required name (160 characters maximum), required valid email (254), optional phone (50), billing address (1,000), notes (2,000), currency, active state, UTC creation/update timestamps, and a concurrency version. Names do not need to be unique. Whitespace around text is trimmed; empty optional values are stored as null.

**Service** stores a UUID, immutable OrganizationId, required name (160), optional description (2,000), default unit price, currency, active state, UTC timestamps, and a concurrency version. Names do not need to be unique. Prices use .NET decimal and PostgreSQL `numeric(14,2)`. Zero is allowed; negative values, more than two decimal places, and values greater than `999999999999.99` are rejected rather than silently rounded.

Both support **PHP and USD only**, without exchange rates or conversion. Client currency is the preferred invoice currency; service currency identifies the currency of its template price. New UI forms default to the organization's current default currency. Changing an organization default does not rewrite existing records.

Service price JSON is returned as a decimal string, such as `"1234.56"`. Angular keeps it as a string through form submission; .NET parses it as decimal. Currency formatting is presentation only. API callers may send a JSON number or numeric string.

## Tenancy and authorization

All endpoints use the existing verified **Member** policy, cookie authentication, and antiforgery pipeline. The application service resolves `ICurrentOrganization`, rechecks membership in PostgreSQL, and scopes every lookup by both OrganizationId and record Id. Organization IDs from request bodies cannot assign ownership. A foreign organization's record and a nonexistent record both return the same 404 response; a forged organization-selection header returns 403 through the existing membership context.

The current role is Owner, but client/service operations require membership rather than embedding a speculative new permission scheme. There are no global filters used as the sole security boundary. Normal error responses use the existing ProblemDetails conventions.

## API

The following routes exist for both `/api/clients` and `/api/services`:

| Method | Suffix | Behavior |
| --- | --- | --- |
| GET | (none) | List current organization's records |
| GET | `/{id}` | Read one record, including an inactive record |
| POST | (none) | Create an active record; returns 201 and Location |
| PATCH | `/{id}` | Replace editable details using the current `version` |
| POST | `/{id}/deactivate` | Set inactive; body is `{ "version": "current-guid" }` |
| POST | `/{id}/reactivate` | Set active; same version requirement |

PATCH is a complete editable-fields request, following the organization-settings convention; it is not JSON Patch. Client fields: `name`, `email`, `phone`, `billingAddress`, `notes`, `currency`, `version`. Service fields: `name`, `description`, `defaultUnitPrice`, `currency`, `version`. All writes retain the existing CSRF request-token mechanism. Missing/stale update versions return 409; missing status-action versions fail validation. Reload before retrying a stale edit.

Lists accept `search` (at most 160 characters) and `status=active|inactive|all`; active is the default. Client search checks name/email and service search checks name. Search is case-insensitive substring matching, with literal `%` and `_` characters. Results use deterministic name-then-ID ordering. Lists currently return an array of all matches for small-business catalogs; paging can be added to the query contract later without changing the tenant boundary. No truncation silently hides records.

There is no DELETE endpoint. Deactivation retains details, identity, organization ownership, and creation timestamp. It removes a record from the default active list, but not from history or direct detail access. Reactivation restores it to the active list. Both actions update the version and timestamp and require explicit confirmation in the UI.

## Persistence

`20260923050214_ClientsAndServices` adds only Clients and Services, with required fields, currency/name checks, a nonnegative price check, restrictive Organization foreign keys, and `(OrganizationId, IsActive, Name)` indexes. No Identity or organization schema alterations are included. No runtime migration or seed data is added.

Apply locally using existing User Secrets and PostgreSQL on port 5433. From the repository root, with the existing .NET 10 SDK setup:

```powershell
dotnet build backend/Elio.sln
Push-Location backend
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Elio.Infrastructure --startup-project src/Elio.Api
Pop-Location
dotnet test backend/Elio.sln --no-build
Push-Location frontend
npm test -- --watch=false
npm run build
Pop-Location
```

The existing EF tool is 10.0.3 with runtime 10.0.12; its older-tools notice also occurred in Phase 1. The migration applies successfully with this setup. No new secrets, packages, or external services are required.

## UI and tests

Clients and Services replace their placeholder routes. Desktop uses tables; mobile uses cards. Search is submitted explicitly and status selection refreshes immediately. Responses from obsolete filter requests cannot overwrite a newer result. Detail/edit uses a labeled native modal dialog with keyboard focus containment, Escape/Cancel/Close behavior, disabled fields during writes, validation feedback, and stale-edit reload. Status changes require a second confirmation and preserve unsaved form fields. The Client dialog includes full contact/address/notes details without fabricated billing history.

PostgreSQL integration tests use the existing disposable-schema factory. They cover own-tenant creation/read/update, cross-tenant list/read/update/deactivate/reactivate rejection, forged tenant selectors, membership removal, anonymous/unverified access, validation, literal search, deterministic ordering, active filters, retained inactive records, stale writes, CSRF, and absence of deletion. Domain tests check validation and state preservation. Angular tests exercise form validation, server query filters, stale list responses, errors/retry, and confirmation/cancel/reactivation behavior. Existing authentication and protected-route tests remain in place.

## Future invoice integration

Phase 3 must copy the chosen client's billing identity, address and currency preference into an invoice snapshot, and copy service description, unit price and currency into independent invoice-line values. A ClientId/ServiceId may identify the source template, but changing or deactivating master data must never rewrite historical snapshots. The invoice engine must deliberately handle currency compatibility; no automatic conversion is implied here. Snapshot entities and invoice logic are not introduced in Phase 2.

## Manual verification

1. Start the existing API and Angular app, sign in with a verified account, and open Clients.
2. Add a client with billing details; check required-name/email validation. Edit the currency and notes, save, close, and reopen to confirm persistence.
3. Search by name and email. Deactivate, verify disappearance from Active, then find it under Inactive. Cancel one confirmation and confirm the record is unchanged; reactivate it.
4. Open Services. Add and edit a service in PHP/USD; check zero price is allowed and negative/three-decimal prices are rejected. Test search and both status transitions.
5. Open one record in two tabs. Save in one, then try saving in the other; expect the stale-record message and reload action.
6. Repeat at a narrow mobile width and use keyboard navigation/Escape in the dialogs. Refresh a protected page to confirm session restoration.
7. Use a second organization and confirm neither list exposes the first organization's records. The automated PostgreSQL tests also exercise guessed record IDs for all mutations.
