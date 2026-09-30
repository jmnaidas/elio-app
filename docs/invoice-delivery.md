# Phase 5 — Invoice delivery and lifecycle

## Issuance and delivery

The Phase 4 aggregate stays immutable: its lifecycle remains Finalized after sending. `deliveryStatus` is `Sent` when at least one successful delivery exists, otherwise `NotSent`; `lastSentAtUtc` is the latest successful timestamp. Angular presents Draft, Finalized (not yet sent), and Sent. A later failed resend never removes an earlier success. No number, aggregate version, timestamp, snapshot, line or total is changed by delivery. No unfinalize, void, payment or deletion operation is added.

`InvoiceDelivery` owns the attempt ID, OrganizationId, InvoiceId, actual recipient, UTC attempt/success timestamps, channel, status and a bounded safe failure code. Attempts transition once from Pending to Sent or Failed. Completed records have no mutation or deletion API. History is newest attempt first, with ID as a deterministic tie breaker. Invoice number is obtained from the immutable invoice rather than duplicated.

## Recipients and message

Default recipient is always the finalized client email, never current client master data. An optional recipient override must be one valid email address, at most 254 characters, without display-name or CR/LF header syntax. Blank overrides are rejected. Each override is audited on its attempt and changes neither client nor invoice. If a frozen address is invalid, provide a valid address in confirmation; the application never silently substitutes a current address.

The plain-text email includes the frozen seller/client names, invoice number, issue/due dates, total/currency and payment instructions. Its subject is `Invoice INV-000123 from Seller`. It attaches `INV-000123.pdf`, generated through the existing `IInvoicePdfGenerator`. The PDF engine and fonts are unchanged. Tests compare exact attachment bytes with the existing PDF endpoint, including after send/resend and source edits.

## Sender and development capture

Application defines `IInvoiceEmailSender`; invoice business logic has no SMTP/vendor dependency. The built-in sender captures only in Development. Captures live in ignored `artifacts/dev-mail/invoices/`, separate from account-token captures. `InvoiceEmail:CaptureDirectory` can override that private directory. Never place it in a public web root or tracked directory.

Each successful development capture writes a delivery-ID-prefixed PDF and a JSON file with recipient, subject, plain-text body, invoice number, attachment filename/path and channel. The JSON is written last; both files must exist for a complete capture. No authentication tokens, credentials, provider responses or PDF binaries are stored in database history. A filesystem error can leave an orphan PDF; inspect/clean local captures as needed. These files contain billing data and must remain local. Development UI/history explicitly says no external email was sent.

Outside Development the default sender throws before writing files, producing a durable `provider_unavailable` failure and a 503 response. A production deployment must register a real provider implementation. No SMTP credentials, vendor SDK or external sending is configured in this phase. Provider success means accepted for delivery, not verified arrival/read/bounce status.

## Consistency and failure tradeoff

1. Resolve authenticated live membership and tenant-owned invoice; require Finalized, current version and valid recipient.
2. Commit a Pending attempt. A partial unique index permits only one Pending attempt per organization/invoice. An overlapping attempt returns 409 before another provider call.
3. Generate the immutable PDF and call the sender with **no open database transaction**.
4. Persist Sent and its timestamp after provider acceptance. On a known sender/generation failure, persist Failed with `delivery_failed`, `pdf_failed` or `provider_unavailable` and return safe ProblemDetails (503). Raw exception messages are not returned or stored.

External delivery cannot be atomic with PostgreSQL. A crash, timeout with uncertain acceptance, or failed completion write can leave a Pending/unknown attempt, or a provider may have accepted a request that raised an exception. There is no exactly-once guarantee. In particular, a successful call followed by a failed database write is deliberately **not** recorded as Failed; the durable Pending row blocks blind resends. This failure window is tested with a PostgreSQL trigger that rejects only the completion write. Do not automatically replay failed HTTP requests.

An operator must reconcile a stranded Pending attempt against provider evidence (or development capture files) before correcting its outcome through a controlled maintenance procedure. This phase provides no automatic timeout reset or audit-edit endpoint. Future production hardening should use the delivery ID as the provider idempotency key, bounded provider timeouts, reconciliation and, if warranted, an outbox/worker. No broker, queue or scheduler is added now.

## API and security

| Endpoint | Contract |
| --- | --- |
| `POST /api/invoices/{id}/send` | `{ "version": "UUID", "recipientEmail": "optional override" }`; 200 with completed delivery DTO |
| `GET /api/invoices/{id}/deliveries` | Newest-first delivery DTO array; no provider secrets/raw responses |
| `GET /api/invoices` | Adds `status=sent`; `finalized` selects issued invoices without successful delivery; existing search/currency filters remain |
| `GET /api/invoices/{id}` | Adds deliveryStatus/lastSentAtUtc without changing issuance lifecycle or content |

Draft send and stale version return 409. Invalid recipient/missing required version return 400. Missing and foreign invoice IDs use the same 404 title. Anonymous requests return 401; unverified users/no membership return 403. Existing Member policy, live membership checks, cookie authentication, CSRF and ProblemDetails remain in use. All delivery lookups use server-resolved OrganizationId. A composite restrictive FK also prevents a delivery row referencing an invoice from another organization.

## Migration

`20260929145633_InvoiceDelivery` adds only InvoiceDeliveries, state checks, tenant/history indexes, the unique Pending index, restrictive FKs, and an `(OrganizationId, Id)` alternate key on Invoices required by the composite FK. No existing invoice columns or values change. Earlier migrations are intact. It was reviewed and applied to the existing local PostgreSQL database; all five migrations are applied and EF reports no pending model changes. Use the existing README migration commands on another environment. Reverting this migration would delete delivery audit history and is not a routine rollback.

## UI and manual verification

Issued details retain their read-only document/PDF action. A native labeled modal confirms invoice number, total and actual recipient; Escape/Cancel work before sending. Busy state disables repeat submission and dismissal. Success updates Sent and history; failures preserve issuance and earlier successes. Pending attempts explain uncertainty and offer history refresh. Every new confirmation defaults back to the snapshot recipient. List filters distinguish Draft, Finalized and Sent. No paid/overdue status is introduced.

1. Start PostgreSQL, API and Angular using README instructions. Sign in to a verified workspace.
2. Create/save/finalize a realistic draft with a client email. Confirm Draft has no send action.
3. Open Send invoice; verify number, total and recipient. Cancel or press Escape once, then reopen and confirm.
4. Verify Sent, timestamp, Resend invoice and a successful development-capture history row.
5. Inspect the matching JSON/PDF under `artifacts/dev-mail/invoices/`; verify subject, recipient, total and original attachment filename. Open the PDF and compare it to the invoice download.
6. Change the current client's email. Refresh the issued route, resend without changing the recipient and confirm it still uses the frozen email.
7. Resend with an override; confirm a separate history entry, unchanged client/snapshot/number/totals, and identical attachment bytes.
8. Refresh the route; check persisted history and Sent list filtering. Repeat the confirmation and detail checks at 1440px and 390px. Check browser errors.
9. Provider failures, production fail-closed behavior, tenant boundaries, CSRF, pending concurrency and completion-write failure are covered by automated tests; there is no public failure-injection endpoint.

## Boundaries

No real production email provider, inbox-arrival tracking, bounce/webhook handling, auto-retries, rate-limiting specific to send, history pagination, outbox or reconciliation UI is supplied. Production deployment must address abuse controls and provider-specific uncertain outcomes. No Phase 6+ payments, balances, overdue/reminder logic, recurring invoices, public links, portal, tax, FX or dashboard redesign is included.

## Verification record — 2026-09-29

- Backend solution build: succeeded, 0 warnings and 0 errors. Backend tests: **94 passed, 0 failed, 0 skipped** (43 unit, 50 real PostgreSQL integration, 1 architecture). Phase 5 adds 3 unit and 6 integration cases, including concurrent Pending exclusion and provider acceptance followed by a failed completion write.
- `npm test -- --watch=false`: **77 passed across 10 files**, 0 failures. Seven Phase 5 cases cover confirmation, cancellation/Escape, busy submission, history/retry, successful and failed outcomes, recipient overrides and Sent filtering.
- `npm run build`: succeeded; initial bundle 315.34 kB, estimated transfer 88.19 kB. No new NuGet/npm dependencies.
- Migration reviewed/applied; all five migrations applied, no pending model changes. EF tooling emits its existing 10.0.3 versus runtime 10.0.12 version notice; the checks succeed.
- Real browser/API scenario: created and finalized `INV-000003` (PHP 16,000), cancelled confirmation, sent, changed the current QA client's email, resent to the frozen `accounts@northline.example`, then sent once to `phase5-override@northline.example`. Refresh retained Sent and three ordered successful delivery records. A new confirmation reverted to the frozen default. Client master data retained `new-accounts@northline.example`.
- Captured subjects, body totals, recipients and `INV-000003.pdf` filenames were checked. All three attachments and a fresh authenticated PDF endpoint download had identical SHA-256 `DC2F4989988D160B8DC0152E84B9C84030213E1215D360556E785FC10019E505`. The one-page A4 PDF was rendered and visually inspected.
- Desktop at 1440px and mobile at 390px were visually checked. Mobile dialog width was 358px; detail/list did not exceed the viewport width. Escape closed the dialog and restored focus. Sent filtering returned only the sent invoice. Browser console warning/error inspection returned no entries; no application/request errors appeared during the real API workflow. The browser tool did not expose a separate network-request log.
- `git diff --check` passed. Review of tracked and non-ignored paths found no local `.env`, capture JSON/PDF, build/test artifacts or credential files. Secret-pattern matches were existing README placeholder/test fallback examples, unchanged by this phase. Captures and PDF inspection files remain local under ignored `artifacts/`.
- The QA invoice and its three delivery records remain in the local database. The existing QA client's current email was intentionally changed as noted above. No commit or push was performed. Verification API/Angular processes were stopped afterward; PostgreSQL was left intact.

## Changed-file manifest

Paths below are relative to the repository root; 15 modified and 11 new files, all unstaged.

| Status | File |
| --- | --- |
| Modified | `README.md` |
| Modified | `backend/src/Elio.Api/Invoices/InvoicesController.cs` |
| Modified | `backend/src/Elio.Application/Invoices/Contracts.cs` |
| New | `backend/src/Elio.Application/Invoices/DeliveryContracts.cs` |
| New | `backend/src/Elio.Domain/Invoices/InvoiceDelivery.cs` |
| Modified | `backend/src/Elio.Infrastructure/DependencyInjection.cs` |
| New | `backend/src/Elio.Infrastructure/Invoices/InvoiceDeliveryService.cs` |
| New | `backend/src/Elio.Infrastructure/Invoices/InvoiceEmailSender.cs` |
| Modified | `backend/src/Elio.Infrastructure/Invoices/InvoiceService.cs` |
| Modified | `backend/src/Elio.Infrastructure/Persistence/ElioDbContext.cs` |
| New | `backend/src/Elio.Infrastructure/Persistence/InvoiceDeliveryConfiguration.cs` |
| New | `backend/src/Elio.Infrastructure/Persistence/Migrations/20260929145633_InvoiceDelivery.cs` |
| New | `backend/src/Elio.Infrastructure/Persistence/Migrations/20260929145633_InvoiceDelivery.Designer.cs` |
| Modified | `backend/src/Elio.Infrastructure/Persistence/Migrations/ElioDbContextModelSnapshot.cs` |
| Modified | `backend/tests/Elio.IntegrationTests/ApiTests.cs` |
| Modified | `backend/tests/Elio.IntegrationTests/IdentityOrganizationTests.cs` |
| New | `backend/tests/Elio.IntegrationTests/InvoiceDeliveryTests.cs` |
| Modified | `backend/tests/Elio.IntegrationTests/InvoiceTests.cs` |
| New | `backend/tests/Elio.UnitTests/InvoiceDeliveryTests.cs` |
| Modified | `docs/architecture.md` |
| New | `docs/invoice-delivery.md` |
| Modified | `frontend/src/app/features/invoices/finalized-invoice.ts` |
| New | `frontend/src/app/features/invoices/invoice-delivery.ts` |
| Modified | `frontend/src/app/features/invoices/invoices-data.ts` |
| Modified | `frontend/src/app/features/invoices/invoices-page.ts` |
| Modified | `frontend/src/app/features/invoices/invoices.spec.ts` |
