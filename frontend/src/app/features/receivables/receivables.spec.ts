import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InvoicePayments } from './invoice-payments';
import { InvoiceReminders } from './invoice-reminders';
import { ReceivablesPage } from './receivables-page';
import { ReceivableDetail, ReceivablesData } from './receivables-data';
import { SessionService } from '../../core/auth/session';
import { API_BASE_URL } from '../../core/http/api-config';

const unpaid: ReceivableDetail = { summary: { invoiceId: 'invoice-1', invoiceNumber: 'INV-000001', clientName: 'Studio', currency: 'PHP', issueDate: '2026-09-01', dueDate: '2026-09-30', total: '20000.00', amountPaid: '0.00', balanceDue: '20000.00', paymentStatus: 'Unpaid', deliveryStatus: 'Sent', dueState: 'Overdue', daysOverdue: 2, asOfDate: '2026-10-02', timeZone: 'Asia/Manila', clientEmail: 'frozen@example.test' }, payments: [] };
const partial: ReceivableDetail = { summary: { ...unpaid.summary, amountPaid: '5000.00', balanceDue: '15000.00', paymentStatus: 'PartiallyPaid' }, payments: [{ id: 'payment-1', amount: '5000.00', currency: 'PHP', method: 'BankTransfer', reference: 'BANK-001', notes: 'Partial', receivedAtUtc: '2026-09-29T00:00:00Z', createdAtUtc: '2026-09-29T00:00:00Z', createdBy: 'user-1' }] };
const paid: ReceivableDetail = { ...partial, summary: { ...unpaid.summary, amountPaid: '20000.00', balanceDue: '0.00', paymentStatus: 'Paid', dueState: 'Paid', daysOverdue: 0 } };
const showModal = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
const close = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');
beforeAll(() => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value(this: HTMLDialogElement) { this.setAttribute('open', ''); } });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value(this: HTMLDialogElement) { this.removeAttribute('open'); } });
});
afterAll(() => {
  if (showModal) Object.defineProperty(HTMLDialogElement.prototype, 'showModal', showModal); else delete (HTMLDialogElement.prototype as Partial<HTMLDialogElement>).showModal;
  if (close) Object.defineProperty(HTMLDialogElement.prototype, 'close', close); else delete (HTMLDialogElement.prototype as Partial<HTMLDialogElement>).close;
});
function setup() {
  const data = { reminders: vi.fn().mockResolvedValue([]), remind: vi.fn(), list: vi.fn().mockResolvedValue([unpaid.summary]), get: vi.fn().mockResolvedValue(unpaid), record: vi.fn().mockResolvedValue(partial) };
  TestBed.configureTestingModule({ providers: [{ provide: ReceivablesData, useValue: data }, provideRouter([{ path: 'receivables', component: ReceivablesPage }, { path: 'receivables/:id', component: ReceivablesPage }])] });
  return data;
}
async function panel() {
  const data = setup(); const fixture = TestBed.createComponent(InvoicePayments); fixture.componentRef.setInput('invoiceId', 'invoice-1'); await fixture.whenStable();
  return { data, fixture, page: fixture.componentInstance };
}
describe('Receivables', () => {
  it('loads issued rows with desktop and mobile representations and submits all filters', async () => {
    const data = setup(); const fixture = TestBed.createComponent(ReceivablesPage); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('table').textContent).toContain('INV-000001');
    expect(fixture.nativeElement.querySelector('.mobile-list').textContent).toContain('Outstanding');
    fixture.componentInstance.search = 'Studio'; fixture.componentInstance.status = 'PartiallyPaid'; fixture.componentInstance.currency = 'PHP';
    await fixture.componentInstance.load(); expect(data.list).toHaveBeenLastCalledWith('Studio', 'PartiallyPaid', 'PHP', '');
  });
  it('renders empty, loading, error and retry states without retaining obsolete results', async () => {
    const data = setup(); data.list.mockRejectedValueOnce(new Error('Unavailable'));
    const fixture = TestBed.createComponent(ReceivablesPage); await fixture.whenStable(); expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
    data.list.mockResolvedValueOnce([]); await fixture.componentInstance.load(); await fixture.whenStable(); expect(fixture.nativeElement.textContent).toContain('No matching receivables');
    let resolve!: (value: typeof unpaid.summary[]) => void; data.list.mockReturnValueOnce(new Promise(r => resolve = r));
    const old = fixture.componentInstance.load(); await fixture.whenStable(); expect(fixture.nativeElement.textContent).toContain('Loading receivables');
    data.list.mockResolvedValueOnce([paid.summary]); await fixture.componentInstance.load(); resolve([unpaid.summary]); await old;
    expect(fixture.componentInstance.items()[0].paymentStatus).toBe('Paid');
  });
  it('supports refreshed detail routes and changing the invoice within the same route', async () => {
    const data = setup(); const harness = await RouterTestingHarness.create('/receivables/invoice-1'); await harness.fixture.whenStable();
    expect(data.get).toHaveBeenCalledWith('invoice-1');
    await harness.navigateByUrl('/receivables/invoice-2'); await harness.fixture.whenStable(); expect(data.get).toHaveBeenLastCalledWith('invoice-2');
  });
});
describe('Payment dialog', () => {
  it('shows invoice context and required fields, cancels and handles Escape', async () => {
    const { fixture, page, data } = await panel(); page.open(); await fixture.whenStable();
    const dialog = fixture.nativeElement.querySelector('dialog'); expect(dialog.open).toBe(true); expect(dialog.textContent).toContain('INV-000001'); expect(dialog.textContent).toContain('₱20,000.00');
    expect(dialog.querySelector('button[type=submit]').disabled).toBe(true); expect(dialog.querySelector('#payment-received').required).toBe(true); expect(dialog.querySelector('#payment-method').required).toBe(true);
    page.cancel(); expect(dialog.open).toBe(false); expect(data.record).not.toHaveBeenCalled();
    page.open(); await fixture.whenStable(); const event = new Event('cancel', { cancelable: true }); dialog.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false); expect(page.confirming()).toBe(false);
  });
  it.each(['0', '-1', '0.001', '20000.01', '1000000000000', 'not-money'])('rejects invalid payment amount %s before submission', async amount => {
    const { fixture, page, data } = await panel(); page.open(); await fixture.whenStable(); page.amount = amount; page.method = 'Cash';
    expect(page.amountError()).not.toBe(''); await page.save(); expect(data.record).not.toHaveBeenCalled();
  });
  it('prevents duplicate submissions and dismissal while busy, then updates partial history', async () => {
    const { fixture, page, data } = await panel(); page.open(); await fixture.whenStable(); page.amount = '5000.00'; page.method = 'BankTransfer'; page.reference = ' BANK-001 ';
    let resolve!: (value: ReceivableDetail) => void; data.record.mockReturnValueOnce(new Promise(r => resolve = r));
    const saving = page.save(); await page.save(); await fixture.whenStable(); expect(data.record).toHaveBeenCalledTimes(1); expect(fixture.nativeElement.querySelector('fieldset').disabled).toBe(true);
    const event = new Event('cancel', { cancelable: true }); page.onCancel(event); expect(event.defaultPrevented).toBe(true); page.cancel(); expect(page.confirmation().nativeElement.open).toBe(true);
    resolve(partial); await saving; await fixture.whenStable(); expect(page.detail()!.summary.paymentStatus).toBe('PartiallyPaid'); expect(fixture.nativeElement.textContent).toContain('BANK-001'); expect(fixture.nativeElement.textContent).toContain('Partially Paid');
    expect(data.record.mock.calls[0][1].amount).toBe('5000.00'); expect(data.record.mock.calls[0][1].reference).toBe('BANK-001');
  });
  it('updates full payment and removes the normal recording action', async () => {
    const { fixture, page, data } = await panel(); page.open(); await fixture.whenStable(); page.amount = '20000'; page.method = 'Cash'; data.record.mockResolvedValueOnce(paid);
    await page.save(); await fixture.whenStable(); expect(page.detail()!.summary.balanceDue).toBe('0.00');
    expect([...fixture.nativeElement.querySelectorAll('button')].some((x: any) => x.textContent.trim() === 'Record payment')).toBe(false);
    page.open(); expect(page.confirming()).toBe(false);
  });
  it('refreshes authoritative balance after a conflict and never fabricates a successful payment', async () => {
    const { fixture, page, data } = await panel(); page.open(); await fixture.whenStable(); page.amount = '20000'; page.method = 'Cash';
    data.record.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { title: 'Payment exceeds the outstanding balance.' } }));
    data.get.mockResolvedValueOnce(partial); await page.save(); await fixture.whenStable();
    expect(page.message()).toBe(''); expect(page.error()).toContain('outstanding'); expect(page.detail()).toEqual(partial);
  });
  it('offers retry after failed history load and does not permit a payment with an unknown balance', async () => {
    const data = setup(); data.get.mockRejectedValueOnce(new Error('Offline')); const fixture = TestBed.createComponent(InvoicePayments); fixture.componentRef.setInput('invoiceId','invoice-1'); await fixture.whenStable();
    fixture.componentInstance.open(); expect(fixture.componentInstance.confirming()).toBe(false); expect(fixture.nativeElement.textContent).toContain('Retry payments');
    await fixture.componentInstance.load(); await fixture.whenStable(); expect(fixture.componentInstance.detail()).toEqual(unpaid);
  });
});
it('uses tenant-authenticated API reads and the existing CSRF mutation service', async () => {
  const session = { mutate: vi.fn().mockResolvedValue(partial) };
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '/api' }, { provide: SessionService, useValue: session }] });
  const data = TestBed.inject(ReceivablesData); const http = TestBed.inject(HttpTestingController);
  const listing = data.list('Studio','Paid','PHP'); http.expectOne('/api/receivables?search=Studio&status=Paid&currency=PHP&dueState=').flush([paid.summary]); await listing;
  const detail = data.get('invoice-1'); http.expectOne('/api/receivables/invoice-1').flush(unpaid); await detail;
  const body = { amount:'5000.00', receivedAtUtc:'2026-09-29T00:00:00Z', method:'Cash', reference:'', notes:'' };
  await data.record('invoice-1',body); expect(session.mutate).toHaveBeenCalledWith('/invoices/invoice-1/payments',body); http.verify();
});

describe('Due dates and reminders', () => {
  const attempt = { id: 'reminder-1', recipientEmail: 'frozen@example.test', attemptedAtUtc: '2026-10-02T00:00:00Z', sentAtUtc: '2026-10-02T00:00:01Z', status: 'Sent', channel: 'development-capture', failureCode: null, amountPaid: '0.00', balanceDue: '20000.00' };
  async function reminder() {
    const data = setup(); const fixture = TestBed.createComponent(InvoiceReminders); fixture.componentRef.setInput('summary', unpaid.summary); await fixture.whenStable();
    return { data, fixture, page: fixture.componentInstance };
  }
  it('renders overdue days in table/cards/detail and combines the due filter with existing filters', async () => {
    const data = setup(); const fixture = TestBed.createComponent(ReceivablesPage); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('tr.overdue').textContent).toContain('2 days overdue');
    expect(fixture.nativeElement.querySelector('.record-card.overdue').textContent).toContain('Overdue');
    fixture.componentInstance.dueState = 'Overdue'; fixture.componentInstance.status = 'PartiallyPaid'; fixture.componentInstance.currency = 'PHP'; fixture.componentInstance.search = 'Studio';
    await fixture.componentInstance.load(); expect(data.list).toHaveBeenLastCalledWith('Studio','PartiallyPaid','PHP','Overdue');
    const detail = TestBed.createComponent(InvoicePayments); detail.componentRef.setInput('invoiceId','invoice-1'); await detail.whenStable();
    expect(detail.nativeElement.textContent).toContain('2 days overdue'); expect(detail.nativeElement.textContent).toContain('Asia/Manila');
  });
  it('shows frozen recipient/context, validates email, and restores focus on Cancel and Escape', async () => {
    const { fixture, page, data } = await reminder();
    page.open(); await fixture.whenStable(); const dialog = fixture.nativeElement.querySelector('dialog');
    expect(dialog.textContent).toContain('₱20,000.00'); expect(page.recipient).toBe('frozen@example.test');
    const input = dialog.querySelector('input'); input.value = 'bad'; input.dispatchEvent(new Event('input')); await fixture.whenStable();
    expect(dialog.querySelector('button[type=submit]').disabled).toBe(true);
    const focus = vi.spyOn(page.opener()!.nativeElement, 'focus'); page.cancel(); expect(dialog.open).toBe(false); expect(focus).toHaveBeenCalled();
    page.open(); await fixture.whenStable(); expect(page.recipient).toBe('frozen@example.test');
    dialog.dispatchEvent(new Event('cancel',{cancelable:true})); expect(dialog.open).toBe(false); expect(data.remind).not.toHaveBeenCalled();
  });
  it('blocks duplicate submission/dismissal while busy and refreshes successful history', async () => {
    const { fixture, page, data } = await reminder(); page.open(); await fixture.whenStable(); page.recipient = ' override@example.test ';
    let resolve!: (value: typeof attempt) => void; data.remind.mockReturnValueOnce(new Promise(r => resolve = r));
    const refreshed = vi.fn(); page.refreshed.subscribe(refreshed); const sending = page.send(); await page.send(); await fixture.whenStable();
    expect(data.remind).toHaveBeenCalledTimes(1); expect(data.remind).toHaveBeenCalledWith('invoice-1','override@example.test');
    expect(fixture.nativeElement.querySelector('dialog input').disabled).toBe(true);
    page.onCancel(new Event('cancel',{cancelable:true})); expect(page.confirmation().nativeElement.open).toBe(true);
    data.reminders.mockResolvedValueOnce([attempt]); resolve(attempt); await sending; await fixture.whenStable();
    expect(page.history()).toEqual([attempt]); expect(page.message()).toContain('captured locally'); expect(refreshed).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Successful');
    expect(document.activeElement).toBe(page.opener()!.nativeElement);
  });
  it('shows failed history without false success and refreshes financial context', async () => {
    const { fixture, page, data } = await reminder(); page.open(); await fixture.whenStable();
    data.remind.mockRejectedValueOnce(new HttpErrorResponse({status:503,error:{title:'Reminder delivery failed.'}}));
    data.reminders.mockResolvedValueOnce([{...attempt,status:'Failed',sentAtUtc:null,failureCode:'delivery_failed'}]);
    const refreshed = vi.fn(); page.refreshed.subscribe(refreshed); await page.send(); await fixture.whenStable();
    expect(page.message()).toBe(''); expect(page.error()).toContain('failed'); expect(page.history()[0].status).toBe('Failed'); expect(refreshed).toHaveBeenCalled();
  });
  it('hides reminders for Paid and blocks unknown or pending history until refreshed', async () => {
    const { fixture, page, data } = await reminder(); fixture.componentRef.setInput('summary',paid.summary); await fixture.whenStable();
    expect(page.opener()).toBeUndefined(); page.open(); expect(page.confirming()).toBe(false);
    fixture.componentRef.setInput('summary',unpaid.summary); data.reminders.mockRejectedValueOnce(new Error('offline')); await page.load(); await fixture.whenStable();
    page.open(); expect(page.confirming()).toBe(false); expect(fixture.nativeElement.textContent).toContain('Retry reminders');
    data.reminders.mockResolvedValueOnce([{...attempt,status:'Pending',sentAtUtc:null}]); await page.load(); page.open(); expect(page.confirming()).toBe(false);
    data.reminders.mockResolvedValueOnce([attempt]); await page.load(); page.open(); await fixture.whenStable(); expect(page.confirming()).toBe(true);
  });
  it('sends due filters and reminders through authenticated reads and CSRF mutations', async () => {
    const session = { mutate:vi.fn().mockResolvedValue(attempt) };
    TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting(),{provide:API_BASE_URL,useValue:'/api'},{provide:SessionService,useValue:session}]});
    const data = TestBed.inject(ReceivablesData); const http = TestBed.inject(HttpTestingController);
    const list = data.list('Studio','PartiallyPaid','PHP','Overdue'); http.expectOne('/api/receivables?search=Studio&status=PartiallyPaid&currency=PHP&dueState=Overdue').flush([]); await list;
    const history = data.reminders('invoice-1'); http.expectOne('/api/invoices/invoice-1/reminders').flush([attempt]); await history;
    await data.remind('invoice-1','frozen@example.test'); expect(session.mutate).toHaveBeenCalledWith('/invoices/invoice-1/reminders',{recipientEmail:'frozen@example.test'}); http.verify();
  });
});
