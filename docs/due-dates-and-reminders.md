# Phase 7 — Due dates and manual payment reminders

## Derived due state

Issuance, original delivery and payment state keep their Phase 0–6 meanings. Due state is derived from the immutable invoice DueDate and authoritative remaining balance; no mutable overdue flag is stored. Drafts remain absent from Receivables.

The server reads the organization's existing validated IANA timezone, converts `TimeProvider.GetUtcNow()` into its current calendar date, and uses DateOnly day arithmetic. This uses the current organization setting rather than the frozen seller timezone or the browser's local timezone. Changing organization timezone can change today's classification, but never changes an issued invoice. Each list uses one business date for all rows. Detail includes `asOfDate` and `timeZone` to make the date basis visible.

| Condition | DueState | DaysOverdue |
| --- | --- | --- |
| Balance = 0 (including zero-value invoice) | Paid | 0 |
| DueDate before today; balance > 0 | Overdue | Today minus DueDate, calendar days |
| DueDate today; balance > 0 | DueToday | 0 |
| DueDate tomorrow through today + 7, inclusive | DueSoon | 0 |
| Later than today + 7 | NotDue | 0 |

Partially paid invoices can be overdue. Paid invoices cannot. Due state and days are recalculated on reads/payment responses; an already-open screen needs refresh after midnight or an organization timezone change. No aging buckets, charts or analytics are added.

## API and reminder eligibility

- `GET /api/receivables` adds `dueState=all|NotDue|DueSoon|DueToday|Overdue` (empty means all), combined with existing search/payment-status/currency filters. Responses add dueState, daysOverdue, asOfDate, timeZone and frozen clientEmail. Detail/payment responses use the same summary.
- `GET /api/invoices/{id}/reminders` returns reminder history, newest attempt first with ID as a tie breaker. Existing drafts return empty history.
- `POST /api/invoices/{id}/reminders` accepts `{ "recipientEmail": "optional-override@example.test" }`, or `{}` to use the frozen issued client email. Returns 200 with the attempt DTO after a successful send/capture.

Only finalized invoices with a positive balance qualify. Sending the original invoice is not required; future-due invoices also qualify. Draft/Paid reminders return 409, invalid recipients return 400, and missing/foreign invoices return indistinguishable 404 responses. Recipients are trimmed, bounded to 254 characters and validated against display-name/header injection. An override never changes the client or frozen invoice. There are no reminder PATCH/DELETE APIs.

The existing cookie, verified Member policy, live membership lookup, CSRF, tenant query and ProblemDetails patterns apply. Organization ownership is server-derived. History has restrictive organization and composite organization/invoice foreign keys.

## Durable audit and delivery behavior

InvoiceReminder is separate from InvoiceDelivery. It records organization/invoice IDs, actual recipient, attempt/success UTC timestamps, channel, Pending/Sent/Failed outcome, allowlisted safe failure code, and decimal amount-paid/balance snapshots. Completed outcomes cannot be changed through normal domain/API operations. No credentials, provider error bodies or PDF binaries are stored in history.

Creation takes the same tenant-owned invoice row lock used by payment recording, validates current balance and commits a durable Pending attempt with its financial snapshot. A filtered unique index allows only one Pending reminder per invoice. The transaction is released before PDF generation or the email-provider call. No invoice, payment or original delivery records are updated.

The existing IInvoiceEmailSender and IInvoicePdfGenerator are reused without new dependencies. Email uses frozen invoice/client information, due date, total, confirmation-time paid/outstanding amounts, currency and payment instructions; the attachment is the unchanged issued invoice PDF. The text identifies the balance timestamp and acknowledges payments made afterward.

Payments are allowed while a reminder is in flight. Consequently an eligible reminder can arrive after settlement; it retains the confirmation-time balance rather than blocking payments during external delivery. Requests made after settlement are rejected. UI balances are informative; server eligibility is authoritative.

PDF/provider failures are recorded as Failed with `pdf_failed`, `delivery_failed` or `provider_unavailable`, and return 503. Provider acceptance followed by a completion-write failure remains durable Pending/unknown and blocks blind retries. There is no exactly-once guarantee, automatic retry, idempotency token, timeout reconciliation or audit-edit UI. An operator must reconcile unknown attempts against provider/capture evidence before controlled maintenance, following Phase 5's approach.

Development uses the existing ignored `artifacts/dev-mail/invoices` capture directory; no external email is sent. A complete capture contains JSON plus its PDF. Production remains fail-closed until the existing sender boundary has a configured implementation. No new provider, scheduler, background job or notification framework is introduced.

## Migration

`20261002155856_InvoiceReminders` was reviewed and applied to the existing local PostgreSQL database. It adds only InvoiceReminders, two restrictive FKs, positive-balance/state checks, a history index and a unique Pending index. It does not change old migration files, invoice/payment/delivery tables or existing rows. All seven migrations are applied and EF reports no pending model changes. The existing EF tools 10.0.3/runtime 10.0.12 notice remains informational. Down drops reminder history and is not a routine rollback.

## UI

Receivables retains its navigation, search, financial filters and desktop-table/mobile-card layout. Due state appears beside the due date; overdue rows/cards have a restrained accent and calendar-day count. Details identify the organization's date basis. The shared financial view exposes reminder history separately from payments and original delivery.

The native labeled reminder dialog shows invoice/due date/financial context and the frozen recipient, permits a validated override, supports Cancel/Escape with focus restoration, and blocks duplicate submission and dismissal while busy. Success/failure refreshes history and financial context. Unknown history disables sending until retry succeeds. Pending history explains the uncertainty and offers refresh. Paid invoices have no normal reminder action.

## Verification (2026-10-03, Asia/Manila)

- Backend solution build: succeeded, zero warnings/errors. All 125 tests passed: 61 unit, 63 integration (including real PostgreSQL), 1 architecture; zero failures/skips. Phase 7 adds 9 unit and 7 integration cases, including date/timezone/DST boundaries, filters, partial/Paid behavior, immutable invoice/payment/delivery/PDF data, frozen/override recipients, tenant/auth/CSRF/live-membership checks, failures, Pending exclusion, in-flight payment and completion-write failure.
- Frontend: `npm test -- --watch=false` passed 98 tests in 11 files, including 6 new Phase 7 cases. Production build passed; initial 315.47 kB, estimated transfer 88.20 kB. Tests cover due rendering/filter combination, dialog validation, Cancel/Escape/focus restoration, busy duplicate protection, success/failure/history refresh, Paid exclusion and CSRF data access.
- Real API QA: existing Paid INV-000004 had no reminder action. Added only two focused issued QA invoices: INV-000005 (due 2026-09-26, PHP 1,000 total, 250 paid, 750 outstanding) and INV-000006 (due 2026-10-08, PHP 1,000 outstanding). The workspace timezone is America/New_York; at verification its business date was October 2, so INV-000005 correctly showed 6 days overdue while INV-000006 was Due Soon.
- Sent exactly one reminder for INV-000005 through the browser. Development capture succeeded; history persisted after protected-route refresh. Confirmed expected balance/instructions in captured email and unchanged invoice JSON, payment JSON, original delivery JSON, downloaded PDF and attached PDF. PDF SHA-256: `F26474D3DC782B188EAF767343752FC9ABC16396D71E575401A28ED569744DB9`.
- Desktop 1440px and mobile 390px list/detail/dialog reviewed; no horizontal overflow. Cancel/Escape returned focus to Send reminder. Browser console inspection found no application errors. Screenshots are outside the repository; temporary comparison files were removed. Permanent QA invoices/payment/reminder remain in local development for inspection; the local reminder capture stays ignored.

## Manual checks

1. Start PostgreSQL/API/Angular using README and sign in to the QA workspace. Open Receivables. Check the organization's timezone in Settings without changing it.
2. Inspect INV-000005 and INV-000006. Due state advances with the organization's current date, so expected day counts differ after verification day. Combine Due Soon/Overdue with payment status/search/currency filters.
3. Open INV-000005; inspect its partial balance and existing successful reminder. Refresh and confirm history remains. Open Send reminder, inspect the default frozen recipient and financial context, then cancel/Escape. Confirm focus returns to the action.
4. If another local capture is desired, confirm one reminder and inspect its ignored JSON/PDF. Expect a new audit row, unchanged balance and unchanged original invoice/delivery/PDF. Do not repeatedly submit an uncertain attempt.
5. Open Paid INV-000004: zero balance, no overdue indication and no normal reminder action. Check list/detail/dialog around 390px and on desktop.

No automatic reminders, payment links/providers, receipts, reversals, taxes/FX, charts, analytics or Phase 8+ work is included. No commit or push was performed.

## Changed files

- `backend/src/Elio.Api/Invoices/InvoiceRemindersController.cs`
- `backend/src/Elio.Application/Invoices/PaymentContracts.cs`
- `backend/src/Elio.Application/Invoices/ReminderContracts.cs`
- `backend/src/Elio.Domain/Invoices/DueSummary.cs`
- `backend/src/Elio.Domain/Invoices/InvoiceReminder.cs`
- `backend/src/Elio.Infrastructure/DependencyInjection.cs`
- `backend/src/Elio.Infrastructure/Invoices/InvoiceReminderService.cs`
- `backend/src/Elio.Infrastructure/Invoices/ReceivableService.cs`
- `backend/src/Elio.Infrastructure/Persistence/ElioDbContext.cs`
- `backend/src/Elio.Infrastructure/Persistence/InvoiceReminderConfiguration.cs`
- `backend/src/Elio.Infrastructure/Persistence/Migrations/20261002155856_InvoiceReminders.cs`
- `backend/src/Elio.Infrastructure/Persistence/Migrations/20261002155856_InvoiceReminders.Designer.cs`
- `backend/src/Elio.Infrastructure/Persistence/Migrations/ElioDbContextModelSnapshot.cs`
- `backend/tests/Elio.IntegrationTests/ApiTests.cs`
- `backend/tests/Elio.IntegrationTests/InvoiceReminderTests.cs`
- `backend/tests/Elio.UnitTests/DueSummaryTests.cs`
- `docs/architecture.md`
- `docs/due-dates-and-reminders.md`
- `frontend/src/app/features/invoices/invoices.spec.ts`
- `frontend/src/app/features/receivables/due-state.scss`
- `frontend/src/app/features/receivables/invoice-payments.ts`
- `frontend/src/app/features/receivables/invoice-reminders.ts`
- `frontend/src/app/features/receivables/receivables-data.ts`
- `frontend/src/app/features/receivables/receivables-page.ts`
- `frontend/src/app/features/receivables/receivables.spec.ts`
- `README.md`
