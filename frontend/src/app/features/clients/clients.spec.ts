import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ClientsPage } from './clients-page';
import { ClientEditor } from './client-editor';
import { ClientsData, ClientRecord } from './clients-data';
import { SessionService } from '../../core/auth/session';

const record: ClientRecord = {
  id: 'record-1',
  name: 'Example',
  currency: 'PHP',
  email: 'billing@example.test',
  phone: null,
  billingAddress: null,
  notes: null,
  isActive: true,
  version: 'v1',
  createdAtUtc: '2026-09-23T00:00:00Z',
  updatedAtUtc: '2026-09-23T00:00:00Z',
};

describe('Client feature', () => {
  // jsdom has no native modal implementation; business behavior still runs in real components.
  const originalShow = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal');
  const originalClose = Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close');
  beforeAll(() => {
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', {
      configurable: true,
      value: function (this: HTMLDialogElement) {
        this.open = true;
      },
    });
    Object.defineProperty(HTMLDialogElement.prototype, 'close', {
      configurable: true,
      value: function (this: HTMLDialogElement) {
        this.open = false;
      },
    });
  });
  afterAll(() => {
    if (originalShow) Object.defineProperty(HTMLDialogElement.prototype, 'showModal', originalShow);
    else Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
    if (originalClose) Object.defineProperty(HTMLDialogElement.prototype, 'close', originalClose);
    else Reflect.deleteProperty(HTMLDialogElement.prototype, 'close');
  });
  function setup() {
    const data = {
      list: vi.fn().mockResolvedValue([record]),
      get: vi.fn().mockResolvedValue(record),
      save: vi.fn().mockResolvedValue({ ...record, version: 'v2' }),
      setActive: vi.fn().mockImplementation(async (_: unknown, active: boolean) => ({
        ...record,
        isActive: active,
        version: 'v2',
      })),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: ClientsData, useValue: data },
        { provide: SessionService, useValue: { organization: signal({ defaultCurrency: 'USD' }) } },
      ],
    });
    return data;
  }
  it('returns keyboard focus to Add client when the editor closes', async () => {
    setup();
    const fixture = TestBed.createComponent(ClientsPage);
    await fixture.whenStable();
    const add: HTMLButtonElement = fixture.nativeElement.querySelector('.page-top button');
    add.click();
    await fixture.whenStable();
    fixture.nativeElement.querySelector('.editor-top button').click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(add);
    expect(fixture.nativeElement.querySelector('dialog')).toBeNull();
  });
  it('loads real rows and sends search/status filters to feature data access', async () => {
    const data = setup();
    const fixture = TestBed.createComponent(ClientsPage);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Example');
    expect(data.list).toHaveBeenCalledWith('', 'active');
    const search: HTMLInputElement = fixture.nativeElement.querySelector('input[type="search"]');
    search.value = ' Example ';
    search.dispatchEvent(new Event('input'));
    const status: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    status.value = 'inactive';
    status.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(data.list).toHaveBeenLastCalledWith('Example', 'inactive');
    data.list.mockResolvedValue([]);
    fixture.nativeElement
      .querySelector('.toolbar')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No matching clients');
  });
  it('ignores an older list response after filters change', async () => {
    const data = setup();
    const fixture = TestBed.createComponent(ClientsPage);
    await fixture.whenStable();
    let finish!: (value: ClientRecord[]) => void;
    data.list.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)));
    const old = fixture.componentInstance.load();
    data.list.mockResolvedValue([]);
    fixture.componentInstance.search = 'missing';
    await fixture.componentInstance.load();
    finish([record]);
    await old;
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('No matching clients');
  });
  it('shows server errors and allows a retry', async () => {
    const data = setup();
    data.list.mockRejectedValueOnce(new Error('offline'));
    const fixture = TestBed.createComponent(ClientsPage);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    fixture.nativeElement.querySelector('[role="alert"] button').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Example');
  });
  it('validates required name/email before creating a record', async () => {
    const data = setup();
    const fixture = TestBed.createComponent(ClientEditor);
    await fixture.whenStable();
    await fixture.componentInstance.save();
    await fixture.whenStable();
    expect(data.save).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.field-error')).not.toBeNull();
    fixture.componentInstance.form.patchValue({ name: 'Valid name', email: 'not-an-email' });
    await fixture.componentInstance.save();
    expect(data.save).not.toHaveBeenCalled();
    fixture.componentInstance.form.patchValue({ email: 'valid@example.test' });
    await fixture.componentInstance.save();
    await fixture.whenStable();
    expect(data.save).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Valid name', currency: 'USD', email: 'valid@example.test' }),
      undefined,
    );
    expect(fixture.nativeElement.textContent).toContain('Client saved.');
  });

  it('requires confirmation for deactivation and supports reactivation with the latest version', async () => {
    const data = setup();
    const fixture = TestBed.createComponent(ClientEditor);
    fixture.componentRef.setInput('initial', record);
    await fixture.whenStable();
    function button(text: string): HTMLButtonElement {
      return Array.from(
        fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
      ).find((b) => b.textContent!.trim() === text)!;
    }
    button('Deactivate client').click();
    await fixture.whenStable();
    expect(data.setActive).not.toHaveBeenCalled();
    button('Keep current status').click();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.confirm-box')).toBeNull();
    button('Deactivate client').click();
    await fixture.whenStable();
    button('Confirm deactivation').click();
    await fixture.whenStable();
    expect(data.setActive).toHaveBeenCalledWith(record, false);
    expect(fixture.nativeElement.textContent).toContain('Client deactivated.');
    button('Reactivate client').click();
    await fixture.whenStable();
    button('Confirm reactivation').click();
    await fixture.whenStable();
    expect(data.setActive).toHaveBeenLastCalledWith(
      expect.objectContaining({ version: 'v2', isActive: false }),
      true,
    );
    expect(fixture.nativeElement.textContent).toContain('Client reactivated.');
  });
});
