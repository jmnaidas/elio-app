# Invoice finalization (Phase 4)

Drafts remain editable. Finalization is an irreversible issuance boundary: a permanent number, frozen billing details and authoritative totals are saved together. There is no unfinalize, delete, void, send or payment operation.

## Transaction and numbering

`POST /api/invoices/{id}/finalize` accepts `{ "version": "<current UUID>" }`. The service resolves live membership, begins a PostgreSQL transaction and locks the tenant-owned invoice with `FOR UPDATE`. It checks Draft lifecycle and the aggregate version, loads the existing lines and tenant-owned sources, then allocates a number through an atomic upsert of the organization's `InvoiceSequences` row.

The domain revalidates the complete invoice and recalculates lines using the existing decimal/rounding rules. Snapshot, number, lifecycle, new version and UTC timestamp are saved in the same transaction. The response reloads persisted timestamp precision. A failure rolls everything back, including counter allocation. Repeated or competing finalization returns 409 after the first issuance; stale versions also return 409. Different drafts contend on their organization's counter only. Numbers start at `INV-000001`, expand beyond six digits, and never reset annually. Successfully issued numbers cannot be reused through the application. Rolled-back allocations were never issued and do not consume a number.

Unique indexes on `(OrganizationId, SequenceValue)` and `(OrganizationId, InvoiceNumber)` enforce numbering boundaries. Check constraints require positive counters and coherent Draft/issued snapshot state. No `MAX + 1` calculation is used.

## Immutable representation

Finalization copies the available seller name/time zone and client name, email, phone, billing address and active state. The invoice already owns its dates, currency, notes, payment instructions and ordered service/manual line copies; those values stay in place and are locked. Recalculated subtotal and total are also frozen.

Finalized GET, list/search, Angular detail and PDF use these snapshot values. They do not load current seller/client/service details for rendering. Retained inactive source references remain valid, consistent with Phase 3. PATCH rejects finalized content with 409. Domain mutation methods enforce the same boundary. This is application/domain immutability, not protection from privileged direct database edits; restrictive foreign keys and issuance constraints remain in place.

## API and security

| Endpoint | Behavior |
| --- | --- |
| `POST /api/invoices/{id}/finalize` | Member-only, CSRF-protected, current version required; returns updated invoice |
| `GET /api/invoices/{id}/pdf` | Member-only; finalized only; application/pdf attachment named after invoice number |
| `GET /api/invoices` | Both lifecycles by default; optional `status=draft`, `finalized`, or `all`; search includes issued number and frozen client details |
| `GET /api/invoices/{id}` | Existing endpoint, extended with issued fields |
| `PATCH /api/invoices/{id}` | Existing Draft behavior; finalized returns 409 |

Responses add `invoiceNumber`, `finalizedAtUtc`, `sellerName`, `sellerTimeZone`, and `clientPhone`; existing client and invoice fields contain frozen values when issued. Decimal values remain JSON strings. Draft issuance fields are null. Authentication, verified email, live membership, CSRF and ProblemDetails reuse existing ELIO mechanisms. Organization identity and invoice numbers are never accepted as authoritative browser inputs. Foreign invoice IDs return 404; PDF access uses the same tenant lookup. PDF responses are private/no-store and nosniff.

## PDF strategy

Infrastructure implements the focused `IInvoicePdfGenerator` interface with pinned PDFsharp-MigraDoc 6.2.4 (MIT). Bundled Noto Sans regular/bold fonts (SIL OFL, license and provenance beside the fonts) avoid machine font dependencies. Generation is in memory, on demand, exclusively from the finalized DTO; no cloud or local PDF storage is required.

The A4 document contains seller/client billing information, dates, permanent number, ordered lines, subtotal/total, currency, optional notes/payment instructions, UTC issuance time and numbered footers. PHP and USD have explicit symbols and two decimal places. Repeated table headers and bounded description continuation rows support long invoices and large amounts.

Metadata dates/IDs and embedded font subset names are stable. The pinned renderer regenerates XMP UUIDs during serialization, so only those fixed-width UUID payloads are replaced without changing stream lengths or cross-reference offsets. Integration tests require byte-identical PHP/USD PDFs after all master-data edits and parse the resulting PDFs. Revisit this normalization when upgrading the renderer. Byte identity across different library/font versions is not promised.

## Migration

`20260928194458_InvoiceFinalization` adds twelve nullable issuance/snapshot columns to Invoices, InvoiceSequences, two organization-scoped unique indexes and issuance/counter check constraints. Existing Draft data satisfies the null snapshot branch. Existing migrations and restrictive relationships are unchanged. The reviewed migration was applied to local development PostgreSQL; all four migrations are applied and EF reports no pending model changes.

Use the existing README EF commands on another database. Do not roll this migration back after real issuance: its Down operation removes issuance history.

## Verification and limits

Tests cover authoritative recalculation, stale versions, same-draft races, eight concurrent draft finalizations, independent organizations, uniqueness violations, a forced persistence failure after allocation, immutable source changes, inactive retained sources, authenticated membership/CSRF, foreign-ID non-disclosure, PHP/USD PDFs and multi-page content. PostgreSQL tests use isolated temporary schemas, not mocks. Angular tests cover confirmation/cancel, dirty-state protection, locked transition, errors, PDF download and direct finalized route loading alongside existing Draft tests.

Phase 4 provides Draft and Finalized behavior only. The pre-existing future Void enum has no operation. No sending/email, payments, receivables calculations, overdue logic, tax, discounts, withholding, FX, recurring billing, reminders, AI, ledger or storage is added. Seller identity is limited to currently available organization fields. Bundled fonts cover the supported Latin billing text and currency symbols; comprehensive CJK/complex-script shaping and PDF/A compliance are not claimed.

## Manual smoke check

1. Start PostgreSQL, the API and Angular using README commands; sign in to a verified organization.
2. Create a client and matching-currency service. Create and save a Draft with service/manual lines, dates, notes and payment instructions.
3. Choose Finalize invoice, cancel once, then confirm. Verify the permanent number, Finalized badge, locked details and absence of editing controls.
4. Download the PDF; inspect billing information, prices, dates, totals and optional text.
5. Change source client contact/address and service description/price. Reopen the issued invoice and download again; historical details and PDF must remain unchanged.
6. Issue another Draft. Expect the next number for that organization (a fresh organization starts at 1).
7. Refresh `/invoices/<id>` to verify session restoration. Check list status/search, desktop and a 390px viewport.
8. Using a separate organization, verify the first number is independent and foreign invoice GET/finalize/PDF requests return 404. Direct PATCH of an issued invoice and Draft PDF requests return 409.

## Local verification record (2026-09-29)

- Backend build: success, zero warnings/errors. Backend tests: 40 unit + 44 real-PostgreSQL integration + 1 architecture = 85 passed, none failed/skipped. Phase 4 adds 11 backend cases.
- Frontend: `npm test -- --watch=false`, 70 passed across 10 files, none failed; seven new Phase 4 cases. `npm run build` succeeded.
- Migration applied to the existing local database; migration list shows all four applied and `has-pending-model-changes` reports no changes. Earlier migration files remain intact.
- Browser: created a new Phase 4 QA client/service and PHP draft in the existing verification workspace; cancelled confirmation, then issued `INV-000001` for PHP 25,000. Changed client name/email/address and service description/price; a freshly loaded issued route retained all original billing values. A second USD invoice received `INV-000002` for USD 375. Refresh restored the authenticated session and issued detail. The pre-existing Phase 3 draft remains present.
- Desktop 1440px and mobile 390px: finalized details, locked controls, list/status filter and navigation checked. Mobile document width remained 390px with no horizontal overflow and no editable invoice fields. No browser console errors/warnings were observed.
- PDF actions before/after source edits returned HTTP 200. The in-app browser's download observer timed out, so the OS download-file destination was not verified. The same real invoice PDF was separately retrieved through the authenticated API and rendered successfully with its original historical content. Automated tests verify the blob download action and byte-identical PHP/USD regeneration after master-data edits.
- Visually reviewed PHP, USD, all five pages of the long-content stress invoice, and the real browser-created invoice PDF. QA files are ignored local verification outputs, not application storage or tracked artifacts.
- `git diff --check` passed. No commit or push was performed. No Phase 5 functionality was added.

## Working-tree file manifest

Phase 4 handoff: 19 modified files and 12 new files; all unstaged. Branch main tracks origin/main. No commits or pushes were made. Temporary QA PDFs/renders were removed after inspection; desktop/mobile screenshots are outside the repository. The two issued QA invoices and their source records remain in the existing local verification workspace.

~~~text
 M README.md
 M backend/src/Elio.Api/Invoices/InvoicesController.cs
 M backend/src/Elio.Application/Invoices/Contracts.cs
 M backend/src/Elio.Domain/Invoices/Invoice.cs
 M backend/src/Elio.Infrastructure/DependencyInjection.cs
 M backend/src/Elio.Infrastructure/Elio.Infrastructure.csproj
 M backend/src/Elio.Infrastructure/Invoices/InvoiceService.cs
 M backend/src/Elio.Infrastructure/Persistence/InvoiceConfiguration.cs
 M backend/src/Elio.Infrastructure/Persistence/Migrations/ElioDbContextModelSnapshot.cs
 M backend/tests/Elio.IntegrationTests/ApiTests.cs
 M backend/tests/Elio.IntegrationTests/InvoiceTests.cs
 M docs/architecture.md
 M frontend/src/app/app.routes.ts
 M frontend/src/app/features/invoices/invoice-editor.html
 M frontend/src/app/features/invoices/invoice-editor.scss
 M frontend/src/app/features/invoices/invoice-editor.ts
 M frontend/src/app/features/invoices/invoices-data.ts
 M frontend/src/app/features/invoices/invoices-page.ts
 M frontend/src/app/features/invoices/invoices.spec.ts
?? backend/src/Elio.Domain/Invoices/InvoiceSequence.cs
?? backend/src/Elio.Infrastructure/Invoices/Fonts/LICENSE.txt
?? backend/src/Elio.Infrastructure/Invoices/Fonts/NotoSans-Bold.ttf
?? backend/src/Elio.Infrastructure/Invoices/Fonts/NotoSans-Regular.ttf
?? backend/src/Elio.Infrastructure/Invoices/Fonts/README.md
?? backend/src/Elio.Infrastructure/Invoices/InvoicePdfGenerator.cs
?? backend/src/Elio.Infrastructure/Persistence/Migrations/20260928194458_InvoiceFinalization.Designer.cs
?? backend/src/Elio.Infrastructure/Persistence/Migrations/20260928194458_InvoiceFinalization.cs
?? backend/tests/Elio.IntegrationTests/InvoiceFinalizationTests.cs
?? backend/tests/Elio.UnitTests/InvoiceFinalizationTests.cs
?? docs/invoice-finalization.md
?? frontend/src/app/features/invoices/finalized-invoice.ts
~~~
