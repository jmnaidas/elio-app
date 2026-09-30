import { afterNextRender, Component, ElementRef, inject, Injector, input, output, signal, viewChild } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InvoiceDeliveryRecord, InvoiceRecord, InvoicesData } from './invoices-data';
import { errorMessage } from '../../core/auth/session';

@Component({
  selector: 'app-invoice-delivery', imports: [CurrencyPipe, DatePipe, FormsModule],
  template: `
    <section aria-label="Invoice delivery" class="delivery">
      <div class="actions"><button class="primary-button" type="button" (click)="confirm()" [disabled]="busy() || loading() || pending()">
        {{ wasSent() ? 'Resend invoice' : 'Send invoice' }}</button>
        @if (invoice().lastSentAtUtc || sentAt()) { <p>Last sent {{ (sentAt() || invoice().lastSentAtUtc) | date:'medium':'UTC' }} UTC</p> }
      </div>
      @if (message()) { <p role="status">{{ message() }}</p> }
      @if (error()) { <p role="alert" class="form-error">{{ error() }}</p> }
      <h3>Delivery history</h3>
      @if (loading()) { <p role="status">Loading delivery history…</p> }
      @else if (historyError()) { <p role="alert">{{ historyError() }} <button type="button" (click)="load()">Retry history</button></p> }
      @else if (!history().length) { <p>No delivery attempts yet.</p> }
      @for (attempt of history(); track attempt.id) {
        <article class="attempt"><strong>{{ attempt.status === 'Sent' ? 'Successful' : attempt.status }}</strong>
          <p>{{ attempt.recipientEmail }}</p><p>{{ attempt.attemptedAtUtc | date:'medium':'UTC' }} UTC</p>
          @if (attempt.channel === 'development-capture') { <p>Development capture · no external email</p> }
          @if (attempt.status === 'Failed') { <p>{{ attempt.failureCode === 'provider_unavailable' ? 'Email delivery is not configured.' : 'Delivery failed. You can try again.' }}</p> }
          @if (attempt.status === 'Pending') { <p>Delivery is pending or its outcome is unknown. Refresh history before retrying.</p> }
        </article>
      }
      @if (pending()) { <button type="button" class="secondary-button" (click)="load()" [disabled]="loading()">Refresh history</button> }
    </section>
    <dialog #confirmation aria-labelledby="send-title" (cancel)="onCancel($event)">
      @if (confirming()) {
      <h2 id="send-title">{{ wasSent() ? 'Resend invoice' : 'Send invoice' }}</h2>
      <p><strong>{{ invoice().invoiceNumber }}</strong> · {{ invoice().total | currency:invoice().currency:'symbol':'1.2-2' }} {{ invoice().currency }}</p>
      <p>The finalized invoice and its PDF will be emailed to this recipient.</p>
      <form #sendForm="ngForm" (ngSubmit)="sendForm.valid && send()">
        <label for="send-recipient">Recipient email</label>
        <input id="send-recipient" name="recipient" type="email" required email maxlength="254" [(ngModel)]="recipient" [disabled]="busy()" />
        <p class="field-help">Defaults to the issued client email. Changing it here only affects this delivery.</p>
        <div class="actions"><button class="primary-button" type="submit" [disabled]="busy() || sendForm.invalid">{{ busy() ? 'Sending…' : 'Confirm send' }}</button>
          <button class="secondary-button" type="button" (click)="cancel()" [disabled]="busy()">Cancel</button></div>
      </form>
      }
    </dialog>
  `,
  styles: `
    :host { display:block; min-width:0; } .delivery { margin:1.5rem 0; padding:1.25rem; border:1px solid #dedee3; background:white; border-radius:8px; }
    .actions { display:flex; flex-wrap:wrap; align-items:center; gap:1rem; } p { overflow-wrap:anywhere; } .attempt { border-top:1px solid #e5e5e8; padding:.8rem 0; }
    .attempt p { margin:.35rem 0; } dialog { max-width:480px; width:calc(100% - 2rem); box-sizing:border-box; border:1px solid #dedee3; border-radius:12px; padding:1.5rem; color:inherit; }
    .delivery h3 { margin:1.25rem 0 .75rem; } dialog h2 { margin:0 0 .75rem; } dialog p { margin:.75rem 0; line-height:1.5; }
    .secondary-button { border:1px solid #dedee3; border-radius:6px; padding:.8rem 1rem; color:inherit; background:white; font:inherit; cursor:pointer; }
    .actions .primary-button { margin:0; } button:disabled { cursor:wait; opacity:.65; } .field-help { color:#66666e; font-size:.85rem; }
    dialog::backdrop { background:rgba(30,30,40,.35); } label { display:block; margin-bottom:.5rem; } input { width:100%; box-sizing:border-box; padding:.75rem; border:1px solid #dedee3; border-radius:6px; }
  `,
})
export class InvoiceDelivery {
  readonly invoice = input.required<InvoiceRecord>();
  readonly sent = output<string>();
  readonly history = signal<InvoiceDeliveryRecord[]>([]);
  readonly loading = signal(false); readonly busy = signal(false);
  readonly error = signal(''); readonly historyError = signal(''); readonly message = signal('');
  readonly sentAt = signal<string | null>(null);
  readonly confirmation = viewChild.required<ElementRef<HTMLDialogElement>>('confirmation');
  private readonly data = inject(InvoicesData);
  readonly confirming = signal(false);
  private readonly injector = inject(Injector);
  recipient = '';
  constructor() { afterNextRender(() => void this.load()); }
  wasSent() { return this.invoice().deliveryStatus === 'Sent' || !!this.sentAt(); }
  pending() { return this.history().some(x => x.status === 'Pending'); }
  async load() {
    this.loading.set(true); this.historyError.set('');
    try {
      const rows = await this.data.deliveries(this.invoice().id); this.history.set(rows);
      const latest = rows.find(x => x.status === 'Sent');
      if (latest?.sentAtUtc) { this.sentAt.set(latest.sentAtUtc); this.sent.emit(latest.sentAtUtc); }
    } catch (e) { this.historyError.set(errorMessage(e)); }
    finally { this.loading.set(false); }
  }
  confirm() {
    if (this.busy() || this.loading() || this.pending() || this.invoice().lifecycle !== 'Finalized') return;
    this.recipient = this.invoice().clientEmail; this.error.set(''); this.message.set('');
    this.confirming.set(true);
    afterNextRender(() => this.confirmation().nativeElement.showModal(), { injector: this.injector });
  }
  cancel() { if (!this.busy()) { this.confirmation().nativeElement.close(); this.confirming.set(false); } }
  onCancel(event: Event) { if (this.busy()) event.preventDefault(); else this.confirming.set(false); }
  async send() {
    if (this.busy() || !this.confirmation().nativeElement.open) return;
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.data.send(this.invoice(), this.recipient.trim());
      if (result.status === 'Sent' && result.sentAtUtc) { this.sentAt.set(result.sentAtUtc); this.sent.emit(result.sentAtUtc); }
      this.message.set(result.channel === 'development-capture' ? 'Invoice captured locally for development. No external email was sent.' : 'Invoice sent successfully.');
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.busy.set(false); this.confirmation().nativeElement.close(); this.confirming.set(false); await this.load(); }
  }
}
