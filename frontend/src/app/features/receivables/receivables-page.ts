import { afterNextRender, Component, DestroyRef, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PageHeader } from '../../shared/ui/page-header';
import { errorMessage } from '../../core/auth/session';
import { paymentStatusLabel, Receivable, ReceivablesData } from './receivables-data';
import { InvoicePayments } from './invoice-payments';
@Component({
  selector: 'app-receivables-page', imports: [PageHeader, FormsModule, CurrencyPipe, RouterLink, InvoicePayments],
  styleUrl: '../../shared/ui/catalog.scss',
  template: `
    <app-page-header title="Receivables" description="See outstanding balances and record payments received." />
    @if (selected(); as id) {
      <div class="page-top"><a routerLink="/receivables">Back to receivables</a><a [routerLink]="['/invoices', id]">View issued invoice</a></div>
      @for (key of [id]; track key) { <app-invoice-payments [invoiceId]="key" /> }
    } @else {
      <form class="toolbar" (ngSubmit)="load()">
        <label class="search">Search receivables<input type="search" name="search" [(ngModel)]="search" maxlength="160" placeholder="Invoice number or client" /></label>
        <label>Payment status<select name="status" [(ngModel)]="status" (ngModelChange)="load()"><option value="">All statuses</option><option value="Unpaid">Unpaid</option><option value="PartiallyPaid">Partially Paid</option><option value="Paid">Paid</option></select></label>
        <label>Currency<select name="currency" [(ngModel)]="currency" (ngModelChange)="load()"><option value="">All currencies</option><option value="PHP">PHP</option><option value="USD">USD</option></select></label>
        <button type="submit" class="secondary-button">Search</button>
      </form>
      @if (loading()) { <p role="status" class="empty">Loading receivables…</p> }
      @else if (error()) { <p role="alert">{{ error() }} <button type="button" class="secondary-button" (click)="load()">Retry</button></p> }
      @else if (!items().length) { <div class="empty"><h2>No matching receivables</h2><p>Finalize an invoice or adjust your filters to see its balance here.</p></div> }
      @else {
        <div class="table-wrap"><table><caption class="sr-only">Receivables</caption><thead><tr><th>Invoice / Client</th><th>Due date</th><th>Total</th><th>Paid</th><th>Outstanding</th><th>Status</th><th>Action</th></tr></thead><tbody>
          @for (item of items(); track item.invoiceId) { <tr><td><strong>{{ item.invoiceNumber }}</strong><p>{{ item.clientName }}</p></td><td>{{ item.dueDate }}</td>
            <td>{{ item.total | currency:item.currency }} {{ item.currency }}</td><td>{{ item.amountPaid | currency:item.currency }}</td><td>{{ item.balanceDue | currency:item.currency }}</td>
            <td><span class="badge">{{ label(item.paymentStatus) }}</span></td><td><a [routerLink]="['/receivables',item.invoiceId]">{{ item.paymentStatus === 'Paid' ? 'View' : 'View / Record payment' }}</a></td></tr> }
        </tbody></table></div>
        <div class="mobile-list">@for (item of items(); track item.invoiceId) {
          <article class="record-card"><div class="card-title"><strong>{{ item.invoiceNumber }}</strong><span class="badge">{{ label(item.paymentStatus) }}</span></div>
            <p>{{ item.clientName }}</p><strong>Outstanding {{ item.balanceDue | currency:item.currency }} {{ item.currency }}</strong><p>Due {{ item.dueDate }}</p>
            <a [routerLink]="['/receivables',item.invoiceId]">{{ item.paymentStatus === 'Paid' ? 'View' : 'View / Record payment' }}</a></article>
        }</div>
      }
    }
  `,
})
export class ReceivablesPage {
  readonly items = signal<Receivable[]>([]); readonly selected = signal<string | null>(null); readonly loading = signal(false); readonly error = signal('');
  readonly label = paymentStatusLabel;
  search = ''; status = ''; currency = ''; private request = 0;
  private readonly data = inject(ReceivablesData); private readonly route = inject(ActivatedRoute); private readonly destroy = inject(DestroyRef);
  constructor() {
    afterNextRender(() => this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params => {
      this.selected.set(params.get('id')); if (!this.selected()) void this.load();
    }));
  }
  async load() {
    const request = ++this.request; this.loading.set(true); this.error.set('');
    try { const rows = await this.data.list(this.search, this.status, this.currency); if (request === this.request) this.items.set(rows); }
    catch (e) { if (request === this.request) this.error.set(errorMessage(e)); }
    finally { if (request === this.request) this.loading.set(false); }
  }
}
