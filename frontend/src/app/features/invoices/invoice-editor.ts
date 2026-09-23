import {
  afterNextRender,
  Component,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CurrencyPipe } from '@angular/common';
import { ClientsData, ClientRecord } from '../clients/clients-data';
import { ServicesData, ServiceRecord } from '../services/services-data';
import { SessionService, errorMessage } from '../../core/auth/session';
import { InvoicesData, InvoiceRecord, InvoiceLineInput } from './invoices-data';
import { lineCents, money, maxCents, pricePattern, quantityPattern } from './invoice-calculations';

@Component({
  selector: 'app-invoice-editor',
  imports: [ReactiveFormsModule, CurrencyPipe],
  templateUrl: './invoice-editor.html',
  styleUrl: './invoice-editor.scss',
})
export class InvoiceEditor {
  readonly initial = input<InvoiceRecord | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();
  private readonly data = inject(InvoicesData);
  private readonly clientData = inject(ClientsData);
  private readonly serviceData = inject(ServicesData);
  readonly session = inject(SessionService);
  private readonly fb = inject(FormBuilder).nonNullable;
  private readonly title = viewChild.required<ElementRef<HTMLElement>>('title');
  readonly record = signal<InvoiceRecord | null>(null);
  readonly clients = signal<ClientRecord[]>([]);
  readonly services = signal<ServiceRecord[]>([]);
  readonly loading = signal(true);
  readonly sourceError = signal('');
  readonly busy = signal(false);
  readonly error = signal('');
  readonly stale = signal(false);
  readonly notice = signal('');
  readonly submitted = signal(false);
  readonly revision = signal(0);
  private initializedCurrency = false;
  private savedValue = '';
  private today = new Date().toLocaleDateString('en-CA');
  readonly form = this.fb.group({
    clientId: ['', Validators.required],
    currency: [
      this.session.organization()?.defaultCurrency ?? 'PHP',
      [Validators.required, Validators.pattern(/^(PHP|USD)$/)],
    ],
    issueDate: [this.today, Validators.required],
    dueDate: [this.today, Validators.required],
    notes: ['', Validators.maxLength(4000)],
    paymentInstructions: ['', Validators.maxLength(4000)],
    lines: this.fb.array([this.lineForm()]),
  });
  get lines() {
    return this.form.controls.lines;
  }
  constructor() {
    this.form.valueChanges.subscribe(() => {
      this.revision.update((x) => x + 1);
      this.notice.set('');
    });
    afterNextRender(() => {
      this.apply(this.initial());
      this.title().nativeElement.focus();
      void this.loadSources();
    });
  }
  private lineForm(line?: InvoiceLineInput) {
    return this.fb.group({
      id: [line?.id ?? ''],
      serviceId: [line?.serviceId ?? ''],
      description: [
        line?.description ?? '',
        [Validators.required, Validators.pattern(/\S/), Validators.maxLength(2000)],
      ],
      quantity: [
        line?.quantity ?? '1',
        [Validators.required, Validators.pattern(quantityPattern), Validators.min(0.0001)],
      ],
      unitPrice: [
        line?.unitPrice ?? '0.00',
        [Validators.required, Validators.pattern(pricePattern)],
      ],
    });
  }
  private apply(item: InvoiceRecord | null) {
    this.record.set(item);
    if (!item) return;
    this.initializedCurrency = true;
    this.form.patchValue({
      clientId: item.clientId,
      currency: item.currency,
      issueDate: item.issueDate,
      dueDate: item.dueDate,
      notes: item.notes ?? '',
      paymentInstructions: item.paymentInstructions ?? '',
    });
    // Keep controls for retained/newly-saved lines so saving does not recreate every row.
    const previous = [...this.lines.controls];
    const controls = item.lines.map((line, index) => {
      const control =
        previous.find((x) => x.controls.id.value === line.id) ??
        (previous[index]?.controls.id.value === '' ? previous[index] : this.lineForm(line));
      control.patchValue({ ...line, serviceId: line.serviceId ?? '' });
      return control;
    });
    this.lines.clear();
    for (const control of controls) this.lines.push(control);
    this.form.markAsPristine();
    this.form.markAsUntouched();
    this.submitted.set(false);
    this.savedValue = JSON.stringify(this.form.getRawValue());
  }
  async loadSources() {
    this.loading.set(true);
    this.sourceError.set('');
    try {
      const [clients, services] = await Promise.all([
        this.clientData.list(),
        this.serviceData.list(),
      ]);
      this.clients.set(clients);
      this.services.set(services);
    } catch (error) {
      this.sourceError.set(errorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }
  clientChanged() {
    const selected = this.clients().find((x) => x.id === this.form.controls.clientId.value);
    if (selected && !this.initializedCurrency) {
      if (!this.form.controls.currency.dirty)
        this.form.controls.currency.setValue(selected.currency);
      this.initializedCurrency = true;
    }
  }
  missingClient() {
    return this.record() && !this.clients().some((x) => x.id === this.record()!.clientId);
  }
  selectService(index: number) {
    const form = this.lines.at(index);
    const selected = this.services().find((x) => x.id === form.controls.serviceId.value);
    if (selected && selected.currency === this.form.controls.currency.value) {
      form.patchValue({
        description: selected.description || selected.name,
        unitPrice: selected.defaultUnitPrice,
      });
      form.markAsDirty();
    }
  }
  retainedService(index: number) {
    const line = this.lines.at(index).getRawValue();
    return (
      this.record()?.currency === this.form.controls.currency.value &&
      this.record()?.lines.some((x) => x.id === line.id && x.serviceId === line.serviceId)
    );
  }
  mismatch(index: number) {
    const id = this.lines.at(index).controls.serviceId.value;
    if (!id || this.retainedService(index)) return false;
    const service = this.services().find((x) => x.id === id);
    return !service || service.currency !== this.form.controls.currency.value;
  }
  missingService(index: number) {
    const id = this.lines.at(index).controls.serviceId.value;
    return id && !this.services().some((x) => x.id === id);
  }
  addLine() {
    if (this.lines.length < 100) {
      this.lines.push(this.lineForm());
      this.form.markAsDirty();
    }
  }
  removeLine(index: number) {
    this.lines.removeAt(index);
    this.form.markAsDirty();
  }
  moveLine(index: number, direction: number) {
    const target = index + direction;
    if (target < 0 || target >= this.lines.length) return;
    const line = this.lines.at(index);
    this.lines.removeAt(index);
    this.lines.insert(target, line);
    this.form.markAsDirty();
  }
  lineTotal(index: number) {
    this.revision();
    if (this.savedValue === JSON.stringify(this.form.getRawValue()))
      return this.record()!.lines[index].lineTotal;
    const value = this.lines.at(index).getRawValue();
    const cents = lineCents(value.quantity, value.unitPrice);
    return cents === null ? null : money(cents);
  }
  total() {
    this.revision();
    if (this.savedValue === JSON.stringify(this.form.getRawValue())) return this.record()!.total;
    let sum = 0n;
    if (!this.lines.length) return null;
    for (const line of this.lines.controls) {
      const value = line.getRawValue();
      const cents = lineCents(value.quantity, value.unitPrice);
      if (cents === null) return null;
      sum += cents;
    }
    return sum <= maxCents ? money(sum) : null;
  }
  dateError() {
    const { issueDate, dueDate } = this.form.getRawValue();
    const valid = (value: string) =>
      /^\d{4}-\d{2}-\d{2}$/.test(value) &&
      value.slice(0, 4) !== '0000' &&
      !Number.isNaN(Date.parse(value)) &&
      new Date(value).toISOString().slice(0, 10) === value;
    return !valid(issueDate) || !valid(dueDate) || dueDate < issueDate;
  }
  previewClient() {
    this.revision();
    const id = this.form.controls.clientId.value;
    const client = this.clients().find((x) => x.id === id);
    if (client) return { name: client.name, email: client.email, address: client.billingAddress };
    const saved = this.record();
    return saved?.clientId === id
      ? { name: saved.clientName, email: saved.clientEmail, address: saved.billingAddress }
      : null;
  }
  invalid(name: string) {
    const field = this.form.get(name);
    return field?.invalid && (field.touched || this.submitted());
  }
  async save() {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    this.error.set('');
    if (
      this.form.invalid ||
      this.dateError() ||
      !this.total() ||
      this.lines.controls.some((_, i) => this.mismatch(i))
    ) {
      this.error.set('Check the highlighted fields and line items before saving.');
      return;
    }
    if (this.busy() || this.loading() || this.sourceError() || this.stale()) return;
    this.busy.set(true);
    try {
      const value = this.form.getRawValue();
      const result = await this.data.save(
        {
          ...value,
          version: this.record()?.version,
          lines: value.lines.map((x) => ({
            ...x,
            id: x.id || undefined,
            serviceId: x.serviceId || null,
          })),
        },
        this.record()?.id,
      );
      this.apply(result);
      this.notice.set('Draft saved. Totals confirmed by the server.');
      this.changed.emit();
    } catch (error) {
      this.handle(error);
    } finally {
      this.busy.set(false);
    }
  }
  async reload() {
    if (!this.record() || this.busy()) return;
    this.busy.set(true);
    try {
      this.apply(await this.data.get(this.record()!.id));
      this.stale.set(false);
      this.error.set('');
      await this.loadSources();
    } catch (error) {
      this.handle(error);
    } finally {
      this.busy.set(false);
    }
  }
  close() {
    if (this.busy()) return;
    if (this.form.dirty && !window.confirm('Discard unsaved draft changes?')) return;
    this.closed.emit();
  }
  private handle(error: unknown) {
    this.error.set(errorMessage(error));
    this.stale.set(
      typeof error === 'object' && error !== null && 'status' in error && error.status === 409,
    );
  }
}
