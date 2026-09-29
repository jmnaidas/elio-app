import {
  afterNextRender,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { PageHeader } from '../../shared/ui/page-header';
import { errorMessage } from '../../core/auth/session';
import { InvoicesData, InvoiceRecord } from './invoices-data';
import { InvoiceEditor } from './invoice-editor';

@Component({
  selector: 'app-invoices-page',
  imports: [PageHeader, FormsModule, CurrencyPipe, DatePipe, InvoiceEditor],
  styleUrl: '../../shared/ui/catalog.scss',
  template: `
    @if (editorOpen()) {
      <h1 class="sr-only">Invoices</h1>
      <app-invoice-editor [initial]="selected()" (closed)="closeEditor()" (changed)="load()" (issued)="showIssued($event)" />
    } @else {
      <div class="page-top">
        <app-page-header
          title="Invoices"
          description="Prepare drafts and keep issued invoices together."
        /><button #addButton type="button" class="primary-button" (click)="add()">
          Create draft
        </button>
      </div>
      <form class="toolbar" (ngSubmit)="load()">
        <label class="search"
          >Search invoices<input
            type="search"
            name="search"
            [(ngModel)]="search"
            placeholder="Client name, email, or invoice number"
            maxlength="160"
        /></label>
        <label
          >Currency<select name="currency" [(ngModel)]="currency" (ngModelChange)="load()">
            <option value="">All currencies</option>
            <option value="PHP">PHP</option>
            <option value="USD">USD</option>
          </select></label
        >
        <label>Status<select name="status" [(ngModel)]="status" (ngModelChange)="load()"><option value="">All statuses</option><option value="draft">Draft</option><option value="finalized">Finalized</option></select></label>
        <button type="submit" class="secondary-button">Search</button>
      </form>
      @if (error()) {
        <p class="form-error" role="alert">
          {{ error() }} <button class="text-button" type="button" (click)="load()">Retry</button>
        </p>
      }
      @if (loading()) {
        <p class="empty" role="status">Loading invoices…</p>
      } @else if (!error() && !items().length) {
        <section class="empty">
          <h2>
            {{ search || currency || status ? 'No matching invoices' : 'Your first invoice starts here' }}
          </h2>
          <p>
            {{
              search || currency || status
                ? 'Try another client or currency.'
                : 'Create a draft with a client and the work you are billing for.'
            }}
          </p>
        </section>
      } @else if (!error()) {
        <div class="table-wrap">
          <table>
            <caption class="sr-only">
              Invoices
            </caption>
            <thead>
              <tr>
                <th>Invoice / Client</th>
                <th>Issue date</th>
                <th>Due date</th>
                <th>Amount</th>
                <th>Currency</th>
                <th>Status</th>
                <th>Updated</th>
              </tr>
            </thead>
            <tbody>
              @for (item of items(); track item.id) {
                <tr>
                  <td>
                    <button class="row-link" type="button" (click)="edit(item)">
                      {{ item.invoiceNumber || item.clientName }}
                    </button>
                    @if (item.invoiceNumber) { <p>{{ item.clientName }}</p> }
                  </td>
                  <td>{{ item.issueDate }}</td>
                  <td>{{ item.dueDate }}</td>
                  <td>{{ item.total | currency: item.currency : 'symbol' : '1.2-2' }}</td>
                  <td>{{ item.currency }}</td>
                  <td><span class="badge">{{ item.lifecycle }}</span></td>
                  <td>{{ item.updatedAtUtc | date: 'mediumDate' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="mobile-list">
          @for (item of items(); track item.id) {
            <article class="record-card">
              <div class="card-title">
                <button class="row-link" type="button" (click)="edit(item)">
                  {{ item.invoiceNumber || item.clientName }}</button
                ><span class="badge">{{ item.lifecycle }}</span>
              </div>
              @if (item.invoiceNumber) { <p>{{ item.clientName }}</p> }
              <strong
                >{{ item.total | currency: item.currency : 'symbol' : '1.2-2' }}
                {{ item.currency }}</strong
              >
              <p>Issued {{ item.issueDate }} · Due {{ item.dueDate }}</p>
              <p>Updated {{ item.updatedAtUtc | date: 'mediumDate' }}</p>
            </article>
          }
        </div>
        <p class="count" role="status">
          {{ items().length }} {{ items().length === 1 ? 'invoice' : 'invoices' }}
        </p>
      }
      @if (opening()) {
        <p role="status">Opening invoice…</p>
      }
    }
  `,
})
export class InvoicesPage {
  private readonly data = inject(InvoicesData);
  private readonly destroy = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router, { optional: true });
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly addButton = viewChild<ElementRef<HTMLButtonElement>>('addButton');
  readonly items = signal<InvoiceRecord[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly opening = signal(false);
  readonly editorOpen = signal(false);
  readonly selected = signal<InvoiceRecord | null>(null);
  search = '';
  currency = '';
  status = '';
  private request = 0;
  constructor() {
    void this.load();
    this.route?.paramMap.pipe(takeUntilDestroyed()).subscribe(params => {
      const id = params.get('id');
      if (id) void this.open(id);
    });
  }
  async load() {
    const request = ++this.request;
    this.loading.set(true);
    this.error.set('');
    try {
      const items = this.status ? await this.data.list(this.search.trim(), this.currency, this.status) : await this.data.list(this.search.trim(), this.currency);
      if (!this.destroy.destroyed && request === this.request) this.items.set(items);
    } catch (error) {
      if (!this.destroy.destroyed && request === this.request) this.error.set(errorMessage(error));
    } finally {
      if (!this.destroy.destroyed && request === this.request) this.loading.set(false);
    }
  }
  add() {
    if (!this.opening()) {
      this.selected.set(null);
      this.editorOpen.set(true);
    }
  }
  closeEditor() {
    if (this.route?.snapshot.paramMap.get('id') && this.router) { void this.router.navigate(['/invoices']); return; }
    this.editorOpen.set(false);
    afterNextRender(() => this.addButton()?.nativeElement.focus(), { injector: this.injector });
  }
  async edit(item: InvoiceRecord) {
    if (this.router && item.lifecycle === 'Finalized') { await this.router.navigate(['/invoices', item.id]); return; }
    await this.open(item.id);
  }
  showIssued(item: InvoiceRecord) {
    if (this.router && this.route?.snapshot.paramMap.get('id') !== item.id) void this.router.navigate(['/invoices', item.id]);
  }
  private async open(id: string) {
    if (this.opening()) return;
    this.opening.set(true);
    this.error.set('');
    try {
      const current = await this.data.get(id);
      if (!this.destroy.destroyed) {
        this.selected.set(current);
        this.editorOpen.set(true);
      }
    } catch (error) {
      if (!this.destroy.destroyed) this.error.set(errorMessage(error));
    } finally {
      if (!this.destroy.destroyed) this.opening.set(false);
    }
  }
}
