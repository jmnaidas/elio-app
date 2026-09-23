# Phase 3 — Draft invoice engine

Invoices are organization-owned draft aggregates. Phase 3 supports creation, reading, listing, and complete draft updates. There is no invoice number of any kind. Finalization, permanent numbering, immutable finalized snapshots, PDF, delivery, payments, receivables, and other later workflows are not implemented.

## Model and persistence

`Invoice` contains Id, immutable OrganizationId, ClientId, Currency, IssueDate, DueDate, optional Notes and PaymentInstructions, Lifecycle, UTC creation/update timestamps, and a Guid concurrency Version. Lifecycle defines Draft / Finalized / Void, but the public domain/API operations only create and edit Draft. No lifecycle transition API exists.

`InvoiceLine` contains Id, InvoiceId, optional ServiceId, Description, Quantity, UnitPrice, calculated LineTotal, and SortOrder. Lines belong exclusively to their invoice; there are no independent line endpoints. A draft requires 1–100 lines. Descriptions are required, trimmed, and limited to 2,000 characters. Notes and payment instructions are each limited to 4,000 characters. Dates are date-only; the due date cannot precede the issue date.

`20260923131444_DraftInvoices` adds only Invoices and InvoiceLines. Organization, Client, Invoice, and Service foreign keys use restrictive deletion. Query indexes cover organization/lifecycle/update time, organization/client/currency, and invoice/line order. Constraints check currency, lifecycle, date ordering, nonblank descriptions, positive quantities, nonnegative prices, calculated line totals, and nonnegative order. Existing tables are unchanged. No runtime migration, seed data, numbering, or snapshot columns are added.

Line array order is authoritative. The server assigns contiguous zero-based SortOrder values; reads sort by SortOrder then Id. Keep an existing line's Id to edit/reorder it, omit Id for a new line, and omit the line to remove it. Foreign or duplicate line IDs are rejected. All changes, including child removals, are saved in one EF transaction.

## Decimal calculations

- Quantity: PostgreSQL `numeric(10,4)`, greater than zero, maximum `999999.9999`. Up to four fractional places; excess precision is rejected.
- UnitPrice and LineTotal: `numeric(14,2)`, nonnegative, maximum `999999999999.99`. Unit prices with more than two decimal places are rejected rather than silently rounded.
- `LineTotal = round(Quantity × UnitPrice, 2, MidpointRounding.AwayFromZero)`.
- `Subtotal = sum(rounded LineTotal)` and `Total = Subtotal`. The total must also fit the supported money range.
- Example: `1.5 × 10.01 = 15.02`; `0.25 × 0.02 = 0.01`; together the total is `15.03`. Round each line, not the unrounded grand total.

The backend is authoritative. Request totals, sort orders, organization IDs, numbers, and lifecycle fields are not writable. API decimal results are JSON strings, matching the existing service-price convention; requests accept decimal strings or JSON numbers. Angular uses scaled BigInt arithmetic for live totals and keeps quantities/prices as strings. Display formatting never participates in persisted calculations. Save/reload applies the server's fields, line IDs, totals, and version.

There are no taxes, discounts, withholding, FX, shipping, or rounding-adjustment lines.

## Client and service behavior

Only active sources are offered for new selections. A new or changed client must be active and belong to the current organization. An existing draft may retain its now-inactive client, and the editor explicitly labels that retained selection. Client name, email, and billing address are current master data displayed for this draft; they are not finalized snapshots.

The first client selection initializes currency from its preference unless the user already intentionally edited currency. Subsequent client selections do not overwrite the user's currency. Existing drafts always retain their saved currency. Supported currencies are PHP and USD.

Selecting a service copies its description (or name when description is absent) and price into editable line values. The submitted values, rather than a later lookup of the service price, are saved. ServiceId identifies the source; it does not bind line values to the source. Later service edits/deactivation do not rewrite existing draft lines or prevent saving their independent edits. Existing copies retain their invoice currency even if the service's currency later changes.

New service selections must be active, tenant-owned, and match the invoice currency. Changing the invoice currency revalidates attached services; clear the service reference to make a manual line or select a matching service. Nothing converts amounts automatically. The UI disables mismatched choices and explains mismatches after a currency change; the API independently enforces the rule.

## API, tenancy, and concurrency

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/invoices` | List drafts; optional `search`, `currency`, and `clientId` |
| GET | `/api/invoices/{id}` | Read invoice and ordered lines |
| POST | `/api/invoices` | Create Draft; 201 with Location |
| PATCH | `/api/invoices/{id}` | Replace all editable fields and line collection |

Search is a case-insensitive, literal substring of the current client's name/email (maximum 160 characters). Currency is PHP or USD; omitted/empty means all. Ordering is UpdatedAtUtc descending then Id. Lists currently return all matches, including lines; pagination is deferred, consistent with small Phase 2 catalogs.

Create/update fields: `clientId`, `currency`, `issueDate`, `dueDate`, `notes`, `paymentInstructions`, `lines`; updates also require `version`. Each line accepts optional `id`, optional `serviceId`, `description`, `quantity`, and `unitPrice`. PATCH is a complete replacement request, not JSON Patch. No DELETE, finalize, send, PDF, or line CRUD endpoints exist.

The existing Member policy requires a verified account and organization membership. The shared access helper rechecks live membership in PostgreSQL. All invoice/client/service lookups include server-derived OrganizationId; request bodies cannot assign ownership. Foreign invoices and nonexistent invoices produce the same 404 ProblemDetails. Foreign client/service references likewise return 404, and foreign line IDs are rejected without disclosing their owner. Forged organization-selection headers are rejected by the existing membership context.

Cookie authentication, CSRF tokens, correlation metadata, and ProblemDetails are unchanged. PATCH checks the supplied version and updates the aggregate's Guid version, including for line-only edits. EF optimistic concurrency handles overlapping writes; stale updates return 409 and roll back the transaction. The editor retains unsaved changes on conflict and offers explicit reload (which replaces those unsaved changes).

## Frontend and limits

The invoice list has client search, currency filtering, desktop columns, mobile cards, and loading/error/retry/empty states. The editor is a single page, approximately 60% editor / 40% preview on desktop; at 1,100px and below it stacks. Lines can be added, removed, and moved up/down. The preview is marked DRAFT and contains no number. Saving updates the same editor; Back returns to the list and confirms before discarding dirty changes.

The editor is local to `/invoices`; a full page refresh restores the protected invoice list. Reopen a saved draft from that list. Unsaved edits are not autosaved or restored across refresh/navigation. No additional packages or external services were introduced.

## Migration and verification

Use the existing local PostgreSQL on port 5433 and existing API User Secrets. From the repository root, with the .NET SDK environment described in README:

```powershell
dotnet build backend/Elio.sln
Push-Location backend
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool run dotnet-ef database update --project src/Elio.Infrastructure --startup-project src/Elio.Api --no-build
dotnet tool run dotnet-ef migrations list --project src/Elio.Infrastructure --startup-project src/Elio.Api --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Elio.Infrastructure --startup-project src/Elio.Api --no-build
Pop-Location
dotnet test backend/Elio.sln --no-build
Push-Location frontend
npm test -- --watch=false
npm run build
Pop-Location
git diff --check
git status --short
```

Domain tests cover rounding, fractional quantities, bounds/precision, validation before mutation, line replacement and IDs. PostgreSQL integration tests cover CRUD, authoritative totals, child removal/order, tenant/source boundaries, stale writes, membership/CSRF, filters, invalid requests, and inactive/changed sources. Angular tests cover form validation, copies, initial currency, mismatch, exact live arithmetic, line operations, authoritative save responses, stale reload, source/list errors, and query races. Existing tests remain in place.

Manual checks:

1. Start the API and Angular as in README. Sign in to a verified workspace; ensure an active client and matching service exist.
2. Create a draft with that client and service. Confirm service values copy and remain editable. Add a manual line.
3. Set one line to quantity `1.5`, price `10.01`, and another to `0.25`, price `0.02`. Expect `15.02`, `0.01`, and total `15.03`.
4. Change invoice currency with the service attached. Expect a mismatch and blocked save. Restore matching currency. Check zero/negative quantity, negative price, excessive precision, missing description, no lines, and reversed dates.
5. Save, return to the list, reopen, edit quantity, reorder/add/remove lines, and save again. Reopen to confirm persistence and ordering.
6. Open the same saved draft in two tabs. Save one; saving the stale other tab must show conflict and explicit reload without silently overwriting its edits.
7. Edit/deactivate the source service and deactivate the client. Reopen the draft; copied lines and totals remain intact and notes/lines remain editable. Inactive sources are not offered for new selections.
8. Refresh `/invoices` to confirm session restoration. Check list search/currency filter, then repeat editor/list checks at desktop, tablet, and approximately 390px mobile widths.

## Phase 4 boundary

Finalization must later introduce permanent numbering and immutable client/organization/line snapshots deliberately. The current live client display must not be mistaken for a finalized billing snapshot. No Phase 4 schema or workflow is prebuilt here beyond the explicitly required lifecycle enum values.
