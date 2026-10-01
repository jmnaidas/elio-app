import { Component, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { InvoiceRecord, InvoicesData } from './invoices-data';
import { errorMessage } from '../../core/auth/session';
import { InvoiceDelivery } from './invoice-delivery';
import { InvoicePayments } from '../receivables/invoice-payments';

@Component({
  selector: 'app-finalized-invoice', imports: [CurrencyPipe, DatePipe, InvoiceDelivery, InvoicePayments],
  styleUrl: './invoice-editor.scss',
  template: `
    <div class="editor-heading"><div><p class="eyebrow">INVOICES / FINALIZED</p><h2 tabindex="-1">{{ invoice().invoiceNumber }}</h2></div>
      <button class="secondary-button" type="button" (click)="closed.emit()">Back to invoices</button></div>
    <div class="issued-actions"><span class="draft-badge">{{ sentAt() || invoice().deliveryStatus === 'Sent' ? 'Sent' : 'Finalized' }}</span><p class="field-help">This invoice is locked. Its issued details cannot be edited.</p>
      <button class="primary-button" type="button" (click)="download()" [disabled]="busy()">{{ busy() ? 'Preparing PDF…' : 'Download PDF' }}</button></div>
    @if (error()) { <p class="form-error" role="alert">{{ error() }}</p> }
    <app-invoice-delivery [invoice]="invoice()" (sent)="sentAt.set($event)" />
    <app-invoice-payments [invoiceId]="invoice().id" />
    <article class="paper issued-paper" aria-label="Finalized invoice">
      <div class="paper-header"><strong>{{ invoice().sellerName }}</strong><span class="draft-badge">FINALIZED</span></div>
      <h2>Invoice</h2><h3>{{ invoice().invoiceNumber }}</h3><p class="field-help">{{ invoice().currency }}</p>
      <div class="billing"><p class="eyebrow">BILL TO</p><strong>{{ invoice().clientName }}</strong><p>{{ invoice().clientEmail }}</p><p>{{ invoice().clientPhone }}</p><p class="preserve-lines">{{ invoice().billingAddress }}</p></div>
      <div class="paper-dates"><div><span>Issued</span><strong>{{ invoice().issueDate }}</strong></div><div><span>Due</span><strong>{{ invoice().dueDate }}</strong></div></div>
      <div class="preview-lines">@for (line of invoice().lines; track line.id) {
        <div class="preview-line"><div><strong class="preserve-lines">{{ line.description }}</strong><p>{{ line.quantity }} × {{ line.unitPrice | currency:invoice().currency:'symbol':'1.2-2' }}</p></div><strong>{{ line.lineTotal | currency:invoice().currency:'symbol':'1.2-2' }}</strong></div>
      }</div>
      <div class="summary"><span>Subtotal</span><strong>{{ invoice().subtotal | currency:invoice().currency:'symbol':'1.2-2' }}</strong></div>
      <div class="summary grand-total"><span>Total · {{ invoice().currency }}</span><strong>{{ invoice().total | currency:invoice().currency:'symbol':'1.2-2' }}</strong></div>
      @if (invoice().notes) { <div class="paper-note"><h3>Notes</h3><p class="preserve-lines">{{ invoice().notes }}</p></div> }
      @if (invoice().paymentInstructions) { <div class="paper-note"><h3>Payment instructions</h3><p class="preserve-lines">{{ invoice().paymentInstructions }}</p></div> }
      <p class="draft-footnote">Finalized {{ invoice().finalizedAtUtc | date:'medium':'UTC' }} UTC</p>
    </article>
  `,
})
export class FinalizedInvoice {
  readonly invoice = input.required<InvoiceRecord>();
  readonly closed = output<void>();
  readonly busy = signal(false);
  readonly sentAt = signal<string | null>(null);
  readonly error = signal('');
  private readonly data = inject(InvoicesData);
  async download() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      const blob = await this.data.pdf(this.invoice().id);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a'); link.href = url; link.download = `${this.invoice().invoiceNumber}.pdf`;
      document.body.appendChild(link); link.click(); link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 30000);
    } catch (error) { this.error.set(errorMessage(error)); }
    finally { this.busy.set(false); }
  }
}
