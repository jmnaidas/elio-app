import { afterNextRender, Component, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { errorMessage } from '../../core/auth/session';
import { InvoiceReminder, Receivable, ReceivablesData } from './receivables-data';

@Component({
  selector: 'app-invoice-reminders', imports: [CurrencyPipe, DatePipe, FormsModule], styleUrl: './payments.scss',
  template: `
    <section class="financial" aria-label="Payment reminders">
      <h2>Payment reminders</h2>
      @if (summary().paymentStatus !== 'Paid') {
        <button #opener type="button" class="primary-button" (click)="open()" [disabled]="busy() || loading() || !!historyError() || pending()">Send reminder</button>
      }
      @if (message()) { <p role="status">{{ message() }}</p> }
      @if (error()) { <p role="alert" class="form-error">{{ error() }}</p> }
      <h3>Reminder history</h3>
      @if (loading()) { <p role="status">Loading reminder history…</p> }
      @else if (historyError()) { <p role="alert">{{ historyError() }} <button type="button" (click)="load()">Retry reminders</button></p> }
      @else if (!history().length) { <p>No reminders sent yet.</p> }
      @for (attempt of history(); track attempt.id) {
        <article class="payment"><strong>{{ attempt.status === 'Sent' ? 'Successful' : attempt.status }}</strong>
          <p>{{ attempt.recipientEmail }}</p><p>{{ attempt.attemptedAtUtc | date:'medium':'UTC' }} UTC</p>
          <p>Outstanding at confirmation: {{ attempt.balanceDue | currency:summary().currency }}</p>
          @if (attempt.channel === 'development-capture') { <p>Development capture · no external email</p> }
          @if (attempt.status === 'Failed') { <p>{{ attempt.failureCode === 'provider_unavailable' ? 'Email delivery is not configured.' : 'Reminder delivery failed. Review before retrying.' }}</p> }
          @if (attempt.status === 'Pending') { <p>The outcome is pending or unknown. Refresh history before retrying.</p> }
        </article>
      }
      @if (pending()) { <button type="button" class="secondary-button" (click)="load()" [disabled]="loading()">Refresh reminders</button> }
    </section>
    <dialog #confirmation aria-labelledby="reminder-title" (cancel)="onCancel($event)">
      @if (confirming()) {
        <h2 id="reminder-title">Send payment reminder</h2>
        <p><strong>{{ summary().invoiceNumber }}</strong> · Due {{ summary().dueDate }}</p>
        <p>Total {{ summary().total | currency:summary().currency }} · Paid {{ summary().amountPaid | currency:summary().currency }}</p>
        <p><strong>Outstanding {{ summary().balanceDue | currency:summary().currency }} {{ summary().currency }}</strong></p>
        <p>The reminder includes the current recorded balance and the original issued PDF.</p>
        <form #form="ngForm" (ngSubmit)="form.valid && send()">
          <label for="reminder-recipient">Recipient email</label>
          <input id="reminder-recipient" name="recipient" type="email" required email maxlength="254" [(ngModel)]="recipient" [disabled]="busy()" />
          <p class="field-help">Defaults to the issued client email. An override affects only this reminder.</p>
          <div class="actions"><button type="submit" class="primary-button" [disabled]="busy() || form.invalid">{{ busy() ? 'Sending…' : 'Confirm reminder' }}</button>
            <button type="button" class="secondary-button" (click)="cancel()" [disabled]="busy()">Cancel</button></div>
        </form>
      }
    </dialog>
  `,
})
export class InvoiceReminders {
  readonly summary = input.required<Receivable>(); readonly refreshed = output<void>();
  readonly history = signal<InvoiceReminder[]>([]); readonly loading = signal(false); readonly busy = signal(false);
  readonly historyError = signal(''); readonly error = signal(''); readonly message = signal(''); readonly confirming = signal(false);
  readonly confirmation = viewChild.required<ElementRef<HTMLDialogElement>>('confirmation');
  readonly opener = viewChild<ElementRef<HTMLButtonElement>>('opener');
  private readonly data = inject(ReceivablesData); private readonly injector = inject(Injector);
  recipient = '';
  constructor() { afterNextRender(() => void this.load()); }
  pending() { return this.history().some(x => x.status === 'Pending'); }
  async load() {
    this.loading.set(true); this.historyError.set('');
    try { this.history.set(await this.data.reminders(this.summary().invoiceId)); }
    catch (e) { this.historyError.set(errorMessage(e)); }
    finally { this.loading.set(false); }
  }
  open() {
    if (this.busy() || this.loading() || this.historyError() || this.pending() || this.summary().paymentStatus === 'Paid') return;
    this.recipient = this.summary().clientEmail; this.error.set(''); this.message.set(''); this.confirming.set(true);
    afterNextRender(() => this.confirmation().nativeElement.showModal(), { injector: this.injector });
  }
  cancel() {
    if (this.busy()) return;
    this.confirmation().nativeElement.close(); this.confirming.set(false); this.opener()?.nativeElement.focus();
  }
  onCancel(event: Event) { event.preventDefault(); this.cancel(); }
  async send() {
    if (this.busy() || !this.confirmation().nativeElement.open || this.summary().paymentStatus === 'Paid') return;
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.data.remind(this.summary().invoiceId, this.recipient.trim());
      this.message.set(result.channel === 'development-capture' ? 'Reminder captured locally. No external email was sent.' : 'Reminder sent successfully.');
    } catch (e) { this.error.set(errorMessage(e)); }
    finally {
      await this.load(); this.busy.set(false); this.cancel(); this.refreshed.emit();
      // Restore after Angular has removed the busy/disabled state from the opener.
      afterNextRender(() => this.opener()?.nativeElement.focus(), { injector: this.injector });
    }
  }
}
