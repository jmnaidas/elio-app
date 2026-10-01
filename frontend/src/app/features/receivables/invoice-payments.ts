import { afterNextRender, Component, ElementRef, inject, Injector, input, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../../core/auth/session';
import { lineCents } from '../invoices/invoice-calculations';
import { paymentStatusLabel, ReceivableDetail, ReceivablesData } from './receivables-data';
@Component({
  selector: 'app-invoice-payments', imports: [CurrencyPipe, DatePipe, FormsModule], styleUrl: './payments.scss',
  template: `
    <section class="financial" aria-label="Payment summary">
      <h2>Payments</h2>
      @if (loading()) { <p role="status">Loading payments…</p> }
      @if (loadError()) { <p role="alert">{{ loadError() }} <button type="button" class="secondary-button" (click)="load()">Retry payments</button></p> }
      @if (detail(); as detail) {
        <div class="financial-heading"><h3>{{ detail.summary.invoiceNumber }}</h3><span class="badge">{{ label(detail.summary.paymentStatus) }}</span></div>
        <div class="figures"><p>Total<strong>{{ detail.summary.total | currency:detail.summary.currency }}</strong></p>
          <p>Paid<strong>{{ detail.summary.amountPaid | currency:detail.summary.currency }}</strong></p>
          <p>Outstanding<strong>{{ detail.summary.balanceDue | currency:detail.summary.currency }}</strong></p></div>
        @if (detail.summary.paymentStatus !== 'Paid') { <button type="button" class="primary-button" (click)="open()" [disabled]="loading() || !!loadError() || busy()">Record payment</button> }
        @if (message()) { <p role="status">{{ message() }}</p> }
        @if (error()) { <p class="form-error" role="alert">{{ error() }}</p> }
        <h3>Payment history</h3>
        @if (!detail.payments.length) { <p>No payments recorded.</p> }
        @for (payment of detail.payments; track payment.id) {
          <article class="payment"><strong>{{ payment.amount | currency:payment.currency }} {{ payment.currency }}</strong>
            <p>{{ payment.receivedAtUtc | date:'medium' }} · {{ methodLabel(payment.method) }}</p>
            @if (payment.reference) { <p>Reference: {{ payment.reference }}</p> }
            @if (payment.notes) { <p class="notes">{{ payment.notes }}</p> }</article>
        }
        <p class="field-help">Payments are a permanent record. Corrections and reversals are not available yet.</p>
      }
    </section>
    <dialog #confirmation aria-labelledby="payment-title" (cancel)="onCancel($event)">
      @if (confirming()) { @if (detail(); as detail) {
        <h2 id="payment-title">Record payment</h2><p><strong>{{ detail.summary.invoiceNumber }}</strong> · {{ detail.summary.currency }}</p>
        <p>Total {{ detail.summary.total | currency:detail.summary.currency }} · Already paid {{ detail.summary.amountPaid | currency:detail.summary.currency }}</p>
        <p><strong>Outstanding {{ detail.summary.balanceDue | currency:detail.summary.currency }}</strong></p>
        <form #paymentForm="ngForm" (ngSubmit)="paymentForm.valid && !amountError() && save()">
          <fieldset [disabled]="busy()">
            <label for="payment-amount">Amount *</label><input id="payment-amount" name="amount" inputmode="decimal" required [(ngModel)]="amount" aria-describedby="amount-help" />
            <p id="amount-help" [class.form-error]="!!amountError()">{{ amountError() || 'Use up to two decimal places.' }}</p>
            <label for="payment-received">Received date/time *</label><input id="payment-received" name="received" type="datetime-local" required [(ngModel)]="received" />
            <p class="field-help">Your local time; stored in UTC.</p>
            <label for="payment-method">Payment method *</label><select id="payment-method" name="method" required [(ngModel)]="method"><option value="">Choose a method</option>
              @for (item of methods; track item.value) { <option [value]="item.value">{{ item.label }}</option> }</select>
            <label for="payment-reference">Reference</label><input id="payment-reference" name="reference" maxlength="160" [(ngModel)]="reference" />
            <label for="payment-notes">Notes</label><textarea id="payment-notes" name="notes" maxlength="2000" rows="3" [(ngModel)]="notes"></textarea>
          </fieldset>
          <p class="field-help">Record a payment received outside ELIO. This does not charge the customer. Payment records cannot be edited or deleted.</p>
          <div class="actions"><button type="submit" class="primary-button" [disabled]="busy() || paymentForm.invalid || !!amountError()">{{ busy() ? 'Recording…' : 'Confirm payment' }}</button>
            <button type="button" class="secondary-button" (click)="cancel()" [disabled]="busy()">Cancel</button></div>
        </form>
      } }
    </dialog>
  `,
})
export class InvoicePayments {
  readonly invoiceId = input.required<string>();
  readonly detail = signal<ReceivableDetail | null>(null); readonly loading = signal(false); readonly busy = signal(false);
  readonly loadError = signal(''); readonly error = signal(''); readonly message = signal(''); readonly confirming = signal(false);
  readonly confirmation = viewChild.required<ElementRef<HTMLDialogElement>>('confirmation'); readonly label = paymentStatusLabel;
  readonly methods = [{ value: 'BankTransfer', label: 'Bank transfer' }, { value: 'Cash', label: 'Cash' }, { value: 'Check', label: 'Check' }, { value: 'Card', label: 'Card' }, { value: 'EWallet', label: 'E-wallet' }, { value: 'Other', label: 'Other' }];
  private readonly data = inject(ReceivablesData); private readonly injector = inject(Injector);
  amount = ''; received = ''; method = ''; reference = ''; notes = '';
  constructor() { afterNextRender(() => void this.load()); }
  methodLabel(value: string) { return this.methods.find(x => x.value === value)?.label || value; }
  async load() {
    this.loading.set(true); this.loadError.set('');
    try { this.detail.set(await this.data.get(this.invoiceId())); } catch (e) { this.loadError.set(errorMessage(e)); }
    finally { this.loading.set(false); }
  }
  amountError() {
    const cents = lineCents('1', this.amount); const balance = lineCents('1', this.detail()?.summary.balanceDue || '0');
    if (cents === null || cents <= 0n) return 'Enter a positive amount with at most two decimal places.';
    if (balance !== null && cents > balance) return 'Amount exceeds the outstanding balance.';
    return '';
  }
  open() {
    if (this.busy() || this.loading() || this.loadError() || !this.detail() || this.detail()!.summary.paymentStatus === 'Paid') return;
    this.amount = ''; this.method = ''; this.reference = ''; this.notes = '';
    const now = new Date(); this.received = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    this.error.set(''); this.message.set(''); this.confirming.set(true);
    afterNextRender(() => this.confirmation().nativeElement.showModal(), { injector: this.injector });
  }
  cancel() { if (!this.busy()) { this.confirmation().nativeElement.close(); this.confirming.set(false); } }
  onCancel(event: Event) { if (this.busy()) event.preventDefault(); else this.confirming.set(false); }
  async save() {
    if (this.busy() || !this.confirmation().nativeElement.open || this.amountError() || !this.method || !this.received) return;
    const date = new Date(this.received); if (Number.isNaN(date.getTime())) return;
    this.busy.set(true); this.error.set('');
    try {
      this.detail.set(await this.data.record(this.invoiceId(), { amount: this.amount, receivedAtUtc: date.toISOString(), method: this.method, reference: this.reference.trim(), notes: this.notes.trim() }));
      this.message.set('Payment recorded. The outstanding balance is updated.');
    } catch (e) { this.error.set(errorMessage(e)); await this.load(); }
    finally { this.busy.set(false); this.confirmation().nativeElement.close(); this.confirming.set(false); }
  }
}
