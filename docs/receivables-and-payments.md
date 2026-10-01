# Phase 6 — Receivables and manual payment recording

## Financial state and issued documents

Issuance remains Draft/Finalized and delivery remains NotSent/Sent. Payment state is separate and never writable by the client. Receivables include only finalized invoices, whether emailed or not. Invoice number, dates, snapshots, lines, total, version, finalization timestamp, delivery history and the issued PDF remain unchanged after payment.

The server calculates `AmountPaid = sum(InvoicePayments.Amount)` and `BalanceDue = FinalizedTotal - AmountPaid` using decimal arithmetic. A positive balance with no payments is Unpaid; a positive balance with payments is PartiallyPaid; zero balance is Paid. A zero-value issued invoice is therefore Paid without requiring a payment. No negative balances, FX, taxes, overdue calculation or cross-currency totals are introduced.

## Payment records and validation

InvoicePayment records have UUID identity, server-derived OrganizationId/InvoiceId/currency, amount, received UTC timestamp, method, reference, notes, creation UTC timestamp and the authenticated recording user's ID. There are no payment update/delete endpoints or domain mutation methods. Corrections require a future reversal/adjustment workflow; users are warned before recording.

Amount is required, positive, at most `999999999999.99`, and must have at most two decimal places. PostgreSQL uses `numeric(14,2)`. Invalid precision is rejected before persistence rather than rounded. JSON responses use decimal strings; requests accept strings or numbers following the existing API convention. Angular keeps submitted amounts as strings and validates known balances using the existing BigInt decimal helpers.

Currency is copied from the immutable invoice and cannot be selected by the request. Methods are BankTransfer, Cash, Check, Card, EWallet and Other, representing payments received outside ELIO. No payment processor is called. Reference is trimmed and limited to 160 characters; notes to 2,000; empty optional values become null. ReceivedAtUtc is required and normalized to UTC. Default/invalid timestamps and times more than five minutes ahead of server UTC are rejected; that allowance accommodates clock skew without adding an invoice-date accounting restriction. The browser accepts local date/time and converts it to UTC.

## Transactions and concurrent payments

Each POST resolves live tenant membership, begins a Read Committed transaction, and takes `SELECT ... FOR UPDATE` on the invoice scoped by both Id and OrganizationId. It then validates issuance and input, sums committed payments, rejects any amount above the remaining balance with 409, inserts the record and commits. Competing writers wait on the same row, then their balance query sees the earlier committed payment. No invoice fields are updated to obtain this protection. All application payment writers must use this path; direct database maintenance must respect the same lock and invariants.

An insert failure rolls back the transaction. Database constraints additionally enforce valid positive amounts, currency/method values, restrictive user/organization links and a composite tenant/invoice FK. The real PostgreSQL suite tests competing payments and an injected insert failure.

Busy UI prevents duplicate clicks within one submission. There is no automatic POST retry or payment idempotency token. If a response is lost after commit, refresh and inspect history before another manual attempt; a payment reference is informational, not a uniqueness key. Do not infer a failed commit solely from a network error.

## API and tenant boundary

| Method and route | Result |
| --- | --- |
| `GET /api/receivables` | Issued invoice summaries with number, frozen client name, currency, dates, total, paid, balance, payment state and delivery state |
| `GET /api/receivables/{invoiceId}` | `{ summary, payments }` for the current tenant |
| `GET /api/invoices/{id}/payments` | Payment history |
| `POST /api/invoices/{id}/payments` | Records one payment; 200 with updated `{ summary, payments }` |

POST body: `{ "amount": "5000.00", "receivedAtUtc": "2026-09-30T10:00:00Z", "method": "BankTransfer", "reference": "BANK-001", "notes": "Partial payment" }`. No financial summary, lifecycle or ownership fields are writable. Invoice version is not required because issued content is immutable and payment balance concurrency is protected by the database lock.

List filters: `search` (maximum 160 characters; case-insensitive literal invoice-number/frozen-client substring), `status=Unpaid|PartiallyPaid|Paid|all`, and `currency=PHP|USD`. Empty filters mean all. Ordering is due date, invoice number, ID. No paging or aggregate multi-currency balance is introduced. History sorts received date descending, creation date descending, then ID for stable ties. Detail summaries are computed from the same payment rows returned as history; list summaries use one SQL statement.

Member authorization, verified account, cookie auth, CSRF and live membership checks remain in use. Every query resolves organization ownership server-side. Foreign and missing invoice IDs return the same 404 title. Draft payment/history access is 409, invalid input is 400, overpayment is 409, anonymous access is 401, and unverified/no-membership access is 403. Errors use existing ProblemDetails.

## Database

`20260930150912_InvoicePayments` creates only InvoicePayments, its amount/currency/method checks, restrictive FKs to organization, tenant-owned invoice and recording user, plus recording-user/history indexes. It reuses Phase 5's invoice alternate key. No previous migration, invoice column or existing row is changed. The migration was reviewed and applied to the existing local PostgreSQL database. All six migrations are applied, with no pending model changes. The existing EF tooling/runtime version notice remains informational. Reverting would destroy payment history and is not a routine rollback.

## UI

Receivables replaces the placeholder with a desktop table and mobile cards, search, status/currency filters, loading/empty/error/retry states and protected refreshable detail routes. Stale filter responses cannot overwrite a newer response. Detail links back to the original invoice. Issued invoice detail also shows the financial panel outside the frozen document.

The native labeled payment dialog shows invoice total, amount paid and outstanding balance. Required amount/date/method and optional reference/notes are explicit. Cancel/Escape work before submission; busy state disables fields, duplicate submission and dismissal. Success applies authoritative server totals/history. Conflict/failure refreshes balances without fabricating success. Paid invoices have no normal Record payment action. Payment times are displayed in the browser's local zone; audit storage is UTC.

## Manual verification

1. Start PostgreSQL, API and Angular with the README commands; sign in to a verified QA workspace.
2. Create/finalize a PHP 20,000 invoice. Open Receivables; verify total 20,000, paid 0, balance 20,000 and Unpaid. Drafts must be absent.
3. Open its payment detail, inspect context, then cancel and reopen. Record 5,000 by Bank transfer with reference TEST-BANK-001.
4. Verify paid 5,000, balance 15,000 and Partially Paid; refresh the protected route and confirm persistence.
5. Enter 15,000.01 and verify validation prevents submission. Record exactly 15,000. Verify Paid, zero balance and no Record payment action.
6. Inspect both history rows. Open the original invoice and confirm number, total, snapshots and delivery history are unchanged. Download its PDF before/after and compare.
7. Test invoice/client search, Unpaid/Partially Paid/Paid/All and currency filters. Repeat list/detail/dialog checks at 1440px and 390px, including keyboard cancellation and console errors.

## Limits and boundaries

This is manual recording, not money movement or proof of bank settlement. No payment providers, receipts, reversal/refund/delete workflow, bank reconciliation, public links, portal, reminders, overdue state, recurring invoices, tax/FX, write-offs, aging reports or dashboard analytics are added. No Phase 7+ functionality or new dependencies. History/list paging and idempotency can be added deliberately in a later phase.

## Final verification record (2026-10-01)

- Backend: full solution build succeeded with zero warnings/errors. All 109 tests passed: 52 unit, 56 integration (including real PostgreSQL security, transaction, concurrency and regression tests), and 1 architecture; zero failed or skipped. Phase 6 adds 9 unit cases and 6 integration cases.
- Frontend: `npm test -- --watch=false` passed all 92 tests in 11 files; zero failures. Phase 6 adds 15 cases. `npm run build` succeeded; initial production bundle 315.47 kB, estimated transfer 88.23 kB.
- Database: all six migrations are applied to the existing development database; EF reports no pending model changes. Only the new payment table, constraints and indexes are added. Earlier migration files are unchanged.
- Real API browser QA: INV-000004 was finalized for PHP 20,000, then received PHP 5,000 (TEST-BANK-001) and PHP 15,000 (TEST-BANK-002). Verified Unpaid → Partially Paid → Paid, persistence after protected-route refresh, rejection of 15,000.01 against the 15,000 balance, ordered history, and removal of the payment action at zero balance. Cancel/Escape and busy behavior were checked.
- Search by invoice number and client, all four payment-status filters, PHP/USD filtering, and empty results were verified. The desktop table at 1440px and mobile cards/detail/dialog at 390px were visually reviewed; measured mobile document width never exceeded the viewport.
- The complete issued invoice JSON and delivery-history JSON remained identical to their pre-payment captures. The PDF downloaded again through the authenticated API was byte-for-byte identical: SHA-256 `8441F16ECDB1D47678B3D4571812B7B33FDF9D08FC42D13814C66111BE05BF50`. The browser PDF download action was also exercised.
- Browser console had no captured warnings/errors. The automation interface does not expose a full network log; successful UI operations, authenticated API responses and API logs were checked instead. API logs contained the existing antiforgery no-cache header warning, with no application errors observed in this workflow.
- The permanent local QA invoice and its two payment records remain for inspection. Use a new invoice to repeat the payment scenario. Temporary before/after PDF and JSON comparison files were removed; screenshots are stored outside the repository.
- Repository whitespace and sensitive/generated-file reviews passed. No commit or push was performed.

## Phase 6 changed-file manifest

Modified (11):

```text
README.md
backend/src/Elio.Infrastructure/DependencyInjection.cs
backend/src/Elio.Infrastructure/Persistence/ElioDbContext.cs
backend/src/Elio.Infrastructure/Persistence/Migrations/ElioDbContextModelSnapshot.cs
backend/tests/Elio.IntegrationTests/ApiTests.cs
backend/tests/Elio.IntegrationTests/InvoiceTests.cs
docs/architecture.md
frontend/src/app/app.routes.ts
frontend/src/app/features/invoices/finalized-invoice.ts
frontend/src/app/features/invoices/invoices.spec.ts
frontend/src/app/features/receivables/receivables-page.ts
```

Added (14):

```text
backend/src/Elio.Api/Invoices/ReceivablesController.cs
backend/src/Elio.Application/Invoices/PaymentContracts.cs
backend/src/Elio.Domain/Invoices/InvoicePayment.cs
backend/src/Elio.Infrastructure/Invoices/ReceivableService.cs
backend/src/Elio.Infrastructure/Persistence/InvoicePaymentConfiguration.cs
backend/src/Elio.Infrastructure/Persistence/Migrations/20260930150912_InvoicePayments.cs
backend/src/Elio.Infrastructure/Persistence/Migrations/20260930150912_InvoicePayments.Designer.cs
backend/tests/Elio.IntegrationTests/InvoicePaymentTests.cs
backend/tests/Elio.UnitTests/InvoicePaymentTests.cs
docs/receivables-and-payments.md
frontend/src/app/features/receivables/invoice-payments.ts
frontend/src/app/features/receivables/payments.scss
frontend/src/app/features/receivables/receivables-data.ts
frontend/src/app/features/receivables/receivables.spec.ts
```
