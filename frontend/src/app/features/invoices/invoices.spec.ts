import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { InvoiceEditor } from './invoice-editor';
import { InvoicesPage } from './invoices-page';
import { InvoicesData, InvoiceRecord } from './invoices-data';
import { ClientsData } from '../clients/clients-data';
import { ServicesData } from '../services/services-data';
import { SessionService } from '../../core/auth/session';
import { lineCents, money } from './invoice-calculations';
import { FinalizedInvoice } from './finalized-invoice';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { InvoiceDelivery } from './invoice-delivery';

const record: InvoiceRecord = {
  id: 'invoice-1',
  clientId: 'client-1',
  clientName: 'Example client',
  clientEmail: 'billing@example.test',
  billingAddress: 'Manila',
  clientIsActive: true,
  currency: 'PHP',
  issueDate: '2026-09-23',
  dueDate: '2026-09-30',
  notes: null,
  paymentInstructions: null,
  lifecycle: 'Draft',
  subtotal: '15.02',
  total: '15.02',
  createdAtUtc: '2026-09-23T00:00:00Z',
  updatedAtUtc: '2026-09-23T00:00:00Z',
  version: 'v1',
  lines: [
    {
      id: 'line-1',
      serviceId: 'service-1',
      description: 'Copied work',
      quantity: '1.5',
      unitPrice: '10.01',
      lineTotal: '15.02',
      sortOrder: 0,
    },
  ],
};
function setup() {
  const data = {
    list: vi.fn().mockResolvedValue([record]),
    get: vi.fn().mockResolvedValue(record),
    save: vi.fn().mockResolvedValue({ ...record, version: 'v2' }),
    finalize: vi.fn().mockResolvedValue({ ...record, lifecycle: 'Finalized', invoiceNumber: 'INV-000001', sellerName: 'Issued studio', finalizedAtUtc: '2026-09-29T00:00:00Z', version: 'v3' }),
    deliveries: vi.fn().mockResolvedValue([]),
    send: vi.fn(),
    pdf: vi.fn().mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' })),
  };
  const clients = {
    list: vi.fn().mockResolvedValue([
      {
        id: 'client-1',
        name: 'Example client',
        email: 'billing@example.test',
        currency: 'PHP',
        isActive: true,
      },
      { id: 'client-2', name: 'US client', currency: 'USD', isActive: true },
    ]),
  };
  const services = {
    list: vi.fn().mockResolvedValue([
      {
        id: 'service-1',
        name: 'Consulting',
        description: 'Current service description',
        defaultUnitPrice: '10.01',
        currency: 'PHP',
        isActive: true,
      },
      {
        id: 'service-2',
        name: 'US work',
        description: null,
        defaultUnitPrice: '20.00',
        currency: 'USD',
        isActive: true,
      },
    ]),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: InvoicesData, useValue: data },
      { provide: ClientsData, useValue: clients },
      { provide: ServicesData, useValue: services },
      {
        provide: SessionService,
        useValue: { organization: signal({ name: 'My studio', defaultCurrency: 'USD' }) },
      },
    ],
  });
  return { data, clients, services };
}
async function editor(initial?: InvoiceRecord) {
  const mocks = setup();
  const fixture = TestBed.createComponent(InvoiceEditor);
  if (initial) fixture.componentRef.setInput('initial', initial);
  await fixture.whenStable();
  return { ...mocks, fixture, page: fixture.componentInstance };
}
function valid(page: InvoiceEditor) {
  page.form.patchValue({
    clientId: 'client-1',
    currency: 'PHP',
    issueDate: '2026-09-23',
    dueDate: '2026-09-30',
  });
  page.lines.at(0).patchValue({ description: 'Manual work', quantity: '1.5', unitPrice: '10.01' });
}
describe('Draft invoice editor', () => {
  it('validates client, dates, description, and at least one line before saving', async () => {
    const { page, data, fixture } = await editor();
    await page.save();
    expect(data.save).not.toHaveBeenCalled();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Choose a client.');
    valid(page);
    page.form.controls.dueDate.setValue('2026-09-01');
    await page.save();
    expect(data.save).not.toHaveBeenCalled();
    page.form.controls.dueDate.setValue('2026-09-30');
    page.removeLine(0);
    await page.save();
    expect(data.save).not.toHaveBeenCalled();
    expect(page.total()).toBeNull();
  });
  it.each([
    ['0', '1'],
    ['-1', '1'],
    ['1.00001', '1'],
    ['1', '-1'],
    ['1', '1.001'],
    ['1000000', '1'],
    ['2', '999999999999.99'],
  ])('rejects invalid quantity/price %s × %s', async (quantity, unitPrice) => {
    const { page, data } = await editor();
    valid(page);
    page.lines.at(0).patchValue({ quantity, unitPrice });
    await page.save();
    expect(data.save).not.toHaveBeenCalled();
    expect(page.total()).toBeNull();
  });
  it('copies service values, leaves them independently editable, and allows manual lines', async () => {
    const { page } = await editor();
    valid(page);
    page.lines.at(0).controls.serviceId.setValue('service-1');
    page.selectService(0);
    expect(page.lines.at(0).controls.description.value).toBe('Current service description');
    expect(page.lines.at(0).controls.unitPrice.value).toBe('10.01');
    page.lines.at(0).controls.description.setValue('My custom work');
    page.lines.at(0).controls.serviceId.setValue('');
    page.selectService(0);
    expect(page.lines.at(0).controls.description.value).toBe('My custom work');
    page.form.controls.currency.setValue('USD');
    page.lines.at(0).controls.serviceId.setValue('service-2');
    page.selectService(0);
    expect(page.lines.at(0).controls.description.value).toBe('US work');
  });
  it('initializes currency from the first client without overwriting intentional edits later', async () => {
    const { page } = await editor();
    page.form.controls.clientId.setValue('client-1');
    page.clientChanged();
    expect(page.form.controls.currency.value).toBe('PHP');
    page.form.controls.currency.setValue('USD');
    page.form.controls.currency.markAsDirty();
    page.form.controls.clientId.setValue('client-2');
    page.clientChanged();
    page.form.controls.clientId.setValue('client-1');
    page.clientChanged();
    expect(page.form.controls.currency.value).toBe('USD');
  });
  it('respects a currency intentionally chosen before the first client', async () => {
    const { page } = await editor();
    page.form.controls.currency.markAsDirty();
    page.form.controls.clientId.setValue('client-1');
    page.clientChanged();
    expect(page.form.controls.currency.value).toBe('USD');
  });
  it('blocks mismatched services and never changes invoice currency to match a service', async () => {
    const { page, data, fixture } = await editor();
    valid(page);
    page.lines.at(0).controls.serviceId.setValue('service-2');
    page.selectService(0);
    expect(page.form.controls.currency.value).toBe('PHP');
    expect(page.lines.at(0).controls.unitPrice.value).toBe('10.01');
    await page.save();
    expect(data.save).not.toHaveBeenCalled();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Service currency must match');
    expect(fixture.nativeElement.querySelector('option[value="service-2"]').disabled).toBe(true);
  });
  it('adds, removes, and reorders lines while preserving their values', async () => {
    const { page } = await editor();
    valid(page);
    const first = page.lines.at(0);
    page.addLine();
    page.lines.at(1).patchValue({ description: 'Second', quantity: '0.25', unitPrice: '20' });
    expect(page.total()).toBe('20.02');
    page.moveLine(1, -1);
    expect(page.lines.at(1)).toBe(first);
    expect(page.lines.at(0).controls.description.value).toBe('Second');
    page.removeLine(1);
    expect(page.total()).toBe('5.00');
  });
  it('renders fractional live calculations using exact per-line rounding', async () => {
    const { page, fixture } = await editor();
    valid(page);
    expect(page.lineTotal(0)).toBe('15.02');
    page.addLine();
    page.lines.at(1).patchValue({ description: 'Small item', quantity: '0.25', unitPrice: '0.02' });
    expect(page.total()).toBe('15.03');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.grand-total').textContent).toContain('15.03');
    expect(money(lineCents('0.25', '0.02')!)).toBe('0.01');
  });
  it('applies authoritative save response fields, identifiers, versions, and totals', async () => {
    const { page, data, fixture } = await editor();
    valid(page);
    const lineControl = page.lines.at(0);
    const descriptionInput = fixture.nativeElement.querySelector('#description-0');
    data.save.mockResolvedValue({
      ...record,
      version: 'server-v2',
      total: '17.00',
      subtotal: '17.00',
      lines: [{ ...record.lines[0], description: 'Server normalized', lineTotal: '17.00' }],
    });
    await page.save();
    expect(data.save).toHaveBeenCalledWith(
      expect.objectContaining({
        lines: [expect.objectContaining({ quantity: '1.5', unitPrice: '10.01', serviceId: null })],
      }),
      undefined,
    );
    expect(page.record()?.version).toBe('server-v2');
    expect(page.lines.at(0)).toBe(lineControl);
    expect(page.lines.at(0).controls.id.value).toBe('line-1');
    expect(page.lines.at(0).controls.description.value).toBe('Server normalized');
    expect(page.total()).toBe('17.00');
    expect(page.form.pristine).toBe(true);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#description-0')).toBe(descriptionInput);
    expect(fixture.nativeElement.textContent).toContain('Totals confirmed by the server');
  });
  it('preserves stale edits and reloads the latest server version explicitly', async () => {
    const { page, data, fixture } = await editor(record);
    page.form.controls.notes.setValue('Unsaved change');
    data.save.mockRejectedValue({
      status: 409,
      error: { title: 'This record changed. Reload it before saving again.' },
    });
    await page.save();
    expect(page.stale()).toBe(true);
    expect(page.form.controls.notes.value).toBe('Unsaved change');
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Reload latest draft');
    data.get.mockResolvedValue({ ...record, version: 'v3', notes: 'Latest' });
    await page.reload();
    expect(page.stale()).toBe(false);
    expect(page.record()?.version).toBe('v3');
    expect(page.form.controls.notes.value).toBe('Latest');
  });
  it('retains an inactive client and unavailable service without overwriting saved copies', async () => {
    const mocks = setup();
    mocks.clients.list.mockResolvedValue([]);
    mocks.services.list.mockResolvedValue([]);
    const fixture = TestBed.createComponent(InvoiceEditor);
    fixture.componentRef.setInput('initial', { ...record, clientIsActive: false });
    await fixture.whenStable();
    const page = fixture.componentInstance;
    expect(page.lines.at(0).controls.description.value).toBe('Copied work');
    expect(page.mismatch(0)).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('inactive · retained');
    page.form.controls.notes.setValue('Edit with inactive sources');
    await page.save();
    expect(mocks.data.save).toHaveBeenCalled();
  });
  it('shows source loading failures and supports retry', async () => {
    const mocks = setup();
    mocks.services.list.mockRejectedValueOnce(new Error('offline'));
    const fixture = TestBed.createComponent(InvoiceEditor);
    await fixture.whenStable();
    expect(fixture.componentInstance.sourceError()).toBeTruthy();
    await fixture.componentInstance.loadSources();
    expect(fixture.componentInstance.sourceError()).toBe('');
    expect(fixture.componentInstance.services()).toHaveLength(2);
  });
});
describe('Draft invoice list', () => {
  it('loads drafts, sends search/currency filters, and opens fresh server details', async () => {
    const { data } = setup();
    const fixture = TestBed.createComponent(InvoicesPage);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Example client');
    fixture.componentInstance.search = ' client ';
    fixture.componentInstance.currency = 'USD';
    await fixture.componentInstance.load();
    expect(data.list).toHaveBeenLastCalledWith('client', 'USD');
    await fixture.componentInstance.edit(record);
    await fixture.whenStable();
    expect(data.get).toHaveBeenCalledWith(record.id);
    expect(fixture.nativeElement.textContent).toContain('Edit draft');
  });
  it('ignores obsolete responses and supports retry after failure', async () => {
    const { data } = setup();
    const fixture = TestBed.createComponent(InvoicesPage);
    await fixture.whenStable();
    let finish!: (value: InvoiceRecord[]) => void;
    data.list.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)));
    const old = fixture.componentInstance.load();
    data.list.mockResolvedValue([]);
    await fixture.componentInstance.load();
    finish([record]);
    await old;
    expect(fixture.componentInstance.items()).toEqual([]);
    data.list.mockRejectedValueOnce(new Error('offline'));
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.error()).toBeTruthy();
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.error()).toBe('');
  });
});

describe('Invoice finalization', () => {
  const issued: InvoiceRecord = { ...record, lifecycle: 'Finalized', invoiceNumber: 'INV-000001', sellerName: 'Issued studio', finalizedAtUtc: '2026-09-29T00:00:00Z' };
  it('requires explicit confirmation, supports cancellation, and transitions to a locked snapshot', async () => {
    const { page, data, fixture } = await editor(record);
    const button = (name: string) => Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(x => x.textContent?.trim() === name)!;
    button('Finalize Invoice').click(); await fixture.whenStable();
    expect(data.finalize).not.toHaveBeenCalled(); expect(fixture.nativeElement.textContent).toContain('cannot be edited afterward');
    button('Keep as draft').click(); await fixture.whenStable(); expect(page.confirming()).toBe(false);
    button('Finalize Invoice').click(); await fixture.whenStable(); button('Confirm finalization').click(); await fixture.whenStable();
    expect(data.finalize).toHaveBeenCalledWith(record); expect(fixture.nativeElement.textContent).toContain('INV-000001');
    expect(fixture.nativeElement.textContent).toContain('Issued studio'); expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Save draft'); expect(fixture.nativeElement.textContent).toContain('Download PDF');
    await page.save(); expect(data.save).not.toHaveBeenCalled();
  });
  it('requires saving unsaved edits before issuing a permanent invoice', async () => {
    const { page, data } = await editor(record); page.form.controls.notes.setValue('Unsaved'); page.form.markAsDirty();
    page.requestFinalization(); await page.finalize(); expect(page.confirming()).toBe(false); expect(data.finalize).not.toHaveBeenCalled();
  });
  it('preserves draft state after a finalization error and reloads a competing finalized result', async () => {
    const { page, data, fixture } = await editor(record); data.finalize.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { title: 'This invoice changed.' } }));
    page.requestFinalization(); await page.finalize(); await fixture.whenStable(); expect(page.record()?.lifecycle).toBe('Draft');
    expect(page.stale()).toBe(true); expect(fixture.nativeElement.textContent).toContain('This invoice changed.');
    data.get.mockResolvedValue(issued); await page.reload(); await fixture.whenStable(); expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('INV-000001');
  });
  it('renders finalized data without fetching current source records', async () => {
    const { clients, services, fixture } = await editor(issued);
    expect(clients.list).not.toHaveBeenCalled(); expect(services.list).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Issued studio'); expect(fixture.nativeElement.textContent).not.toContain('My studio');
  });
  it('downloads the authorized PDF with its invoice number and reports retrieval errors', async () => {
    const { data } = setup(); const fixture = TestBed.createComponent(FinalizedInvoice); fixture.componentRef.setInput('invoice', issued); await fixture.whenStable();
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:invoice');
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    await fixture.componentInstance.download(); expect(data.pdf).toHaveBeenCalledWith('invoice-1'); expect(create).toHaveBeenCalled(); expect(click).toHaveBeenCalled();
    data.pdf.mockRejectedValue(new HttpErrorResponse({ status: 500, error: { title: 'Unable to create PDF.' } })); await fixture.componentInstance.download(); await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Unable to create PDF.'); create.mockRestore(); click.mockRestore();
  });
  it('loads a finalized invoice from a refreshed detail route', async () => {
    const { data } = setup(); data.get.mockResolvedValue(issued);
    TestBed.configureTestingModule({ providers: [provideRouter([{ path: 'invoices/:id', component: InvoicesPage }])] });
    const harness = await RouterTestingHarness.create('/invoices/invoice-1'); await harness.fixture.whenStable();
    expect(data.get).toHaveBeenCalledWith('invoice-1'); expect(harness.routeNativeElement?.textContent).toContain('INV-000001');
    expect(harness.routeNativeElement?.querySelector('form')).toBeNull();
  });
  it('shows both lifecycle states and submits the selected list filter', async () => {
    const { data } = setup(); data.list.mockResolvedValue([record, { ...issued, id: 'invoice-2' }]);
    const fixture = TestBed.createComponent(InvoicesPage); await fixture.whenStable(); expect(fixture.nativeElement.textContent).toContain('Finalized');
    expect(fixture.nativeElement.textContent).toContain('INV-000001'); fixture.componentInstance.status = 'finalized'; await fixture.componentInstance.load();
    expect(data.list).toHaveBeenLastCalledWith('', '', 'finalized');
  });
});

describe('invoice email delivery', () => {
  const issued: InvoiceRecord = { ...record, lifecycle: 'Finalized', invoiceNumber: 'INV-000001', sellerName: 'Issued studio' };
  const success = { id: 'delivery-1', recipientEmail: record.clientEmail, attemptedAtUtc: '2026-09-29T12:00:00Z', sentAtUtc: '2026-09-29T12:00:01Z', status: 'Sent', channel: 'development-capture', failureCode: null };
  const show = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
  const close = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');
  beforeAll(() => {
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value: function(this: HTMLDialogElement) { this.setAttribute('open', ''); } });
    Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value: function(this: HTMLDialogElement) { this.removeAttribute('open'); } });
  });
  afterAll(() => {
    if (show) Object.defineProperty(HTMLDialogElement.prototype, 'showModal', show); else Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
    if (close) Object.defineProperty(HTMLDialogElement.prototype, 'close', close); else Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  });
  async function delivery(invoice = issued) {
    const { data } = setup(); data.send.mockResolvedValue(success);
    const fixture = TestBed.createComponent(InvoiceDelivery); fixture.componentRef.setInput('invoice', invoice); await fixture.whenStable();
    return { data, fixture, page: fixture.componentInstance };
  }
  it('does not offer sending for a Draft', async () => {
    const { fixture } = await editor(record);
    expect(fixture.nativeElement.querySelector('app-invoice-delivery')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Send invoice');
  });
  it('shows a labeled confirmation with snapshot recipient, number and total and supports cancel', async () => {
    const { fixture, page, data } = await delivery(); page.confirm(); await fixture.whenStable();
    const dialog: HTMLDialogElement = fixture.nativeElement.querySelector('dialog');
    expect(dialog.open).toBe(true); expect(dialog.getAttribute('aria-labelledby')).toBe('send-title');
    expect(dialog.textContent).toContain('INV-000001'); expect(dialog.textContent).toContain('15.02');
    expect((dialog.querySelector('input') as HTMLInputElement).value).toBe(record.clientEmail);
    expect(dialog.querySelector('label')?.htmlFor).toBe('send-recipient');
    page.cancel(); await fixture.whenStable(); expect(dialog.open).toBe(false); expect(data.send).not.toHaveBeenCalled();
    page.confirm(); await fixture.whenStable(); page.onCancel(new Event('cancel')); expect(page.confirming()).toBe(false);
  });
  it('prevents duplicate sends and dismissal while busy, then shows successful history and resend', async () => {
    const { fixture, page, data } = await delivery(); let complete!: (value: typeof success) => void;
    data.send.mockImplementation(() => new Promise(resolve => complete = resolve));
    page.confirm(); await fixture.whenStable(); const sending = page.send(); await page.send();
    expect(data.send).toHaveBeenCalledTimes(1); const escape = new Event('cancel', { cancelable: true }); page.onCancel(escape); expect(escape.defaultPrevented).toBe(true);
    await fixture.whenStable(); expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    data.deliveries.mockResolvedValue([success]); complete(success); await sending; await fixture.whenStable();
    expect(page.wasSent()).toBe(true); expect(fixture.nativeElement.textContent).toContain('Resend invoice');
    expect(fixture.nativeElement.textContent).toContain('Successful'); expect(fixture.nativeElement.textContent).toContain('No external email was sent.');
    expect(fixture.nativeElement.querySelector('dialog').open).toBe(false);
  });
  it('shows failed attempts without claiming the invoice was sent and permits retry', async () => {
    const { fixture, page, data } = await delivery(); data.send.mockRejectedValue(new HttpErrorResponse({ status: 503, error: { title: 'Invoice delivery failed.' } }));
    data.deliveries.mockResolvedValue([{ ...success, status: 'Failed', sentAtUtc: null, failureCode: 'delivery_failed' }]);
    page.confirm(); await fixture.whenStable(); await page.send(); await fixture.whenStable();
    expect(page.wasSent()).toBe(false); expect(fixture.nativeElement.textContent).toContain('Invoice delivery failed.');
    expect(fixture.nativeElement.textContent).toContain('Send invoice'); expect(fixture.nativeElement.textContent).toContain('Failed');
  });
  it('uses an override only for that attempt and resets the next confirmation to the snapshot email', async () => {
    const { fixture, page, data } = await delivery({ ...issued, deliveryStatus: 'Sent', lastSentAtUtc: success.sentAtUtc });
    page.confirm(); await fixture.whenStable(); page.recipient = 'override@example.test'; await page.send();
    expect(data.send).toHaveBeenCalledWith(expect.objectContaining({ clientEmail: record.clientEmail }), 'override@example.test');
    page.confirm(); await fixture.whenStable(); expect(page.recipient).toBe(record.clientEmail); expect(page.wasSent()).toBe(true);
  });
  it('reports history loading failures and blocks sending while an unresolved attempt exists', async () => {
    const { fixture, page, data } = await delivery(); data.deliveries.mockRejectedValue(new Error('unavailable')); await page.load(); await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Retry history');
    data.deliveries.mockResolvedValue([{ ...success, status: 'Pending', sentAtUtc: null }]); await page.load(); page.confirm();
    expect(page.confirming()).toBe(false); expect(page.wasSent()).toBe(false);
  });
  it('lists Sent invoices separately and sends the status filter to the API', async () => {
    const { data } = setup(); data.list.mockResolvedValue([{ ...issued, deliveryStatus: 'Sent' }]);
    const fixture = TestBed.createComponent(InvoicesPage); await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.badge')?.textContent).toContain('Sent');
    fixture.componentInstance.status = 'sent'; await fixture.componentInstance.load(); expect(data.list).toHaveBeenLastCalledWith('', '', 'sent');
  });
});
