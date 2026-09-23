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
      <app-invoice-editor [initial]="selected()" (closed)="closeEditor()" (changed)="load()" />
    } @else {
      <div class="page-top">
        <app-page-header
          title="Invoices"
          description="Prepare clear, accurate drafts at your own pace."
        /><button #addButton type="button" class="primary-button" (click)="add()">
          Create draft
        </button>
      </div>
      <form class="toolbar" (ngSubmit)="load()">
        <label class="search"
          >Search clients<input
            type="search"
            name="search"
            [(ngModel)]="search"
            placeholder="Client name or email"
            maxlength="160"
        /></label>
        <label
          >Currency<select name="currency" [(ngModel)]="currency" (ngModelChange)="load()">
            <option value="">All currencies</option>
            <option value="PHP">PHP</option>
            <option value="USD">USD</option>
          </select></label
        >
        <button type="submit" class="secondary-button">Search</button>
      </form>
      @if (error()) {
        <p class="form-error" role="alert">
          {{ error() }} <button class="text-button" type="button" (click)="load()">Retry</button>
        </p>
      }
      @if (loading()) {
        <p class="empty" role="status">Loading drafts…</p>
      } @else if (!error() && !items().length) {
        <section class="empty">
          <h2>
            {{ search || currency ? 'No matching drafts' : 'Your first invoice starts here' }}
          </h2>
          <p>
            {{
              search || currency
                ? 'Try another client or currency.'
                : 'Create a draft with a client and the work you are billing for.'
            }}
          </p>
        </section>
      } @else if (!error()) {
        <div class="table-wrap">
          <table>
            <caption class="sr-only">
              Draft invoices
            </caption>
            <thead>
              <tr>
                <th>Client</th>
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
                      {{ item.clientName }}
                    </button>
                  </td>
                  <td>{{ item.issueDate }}</td>
                  <td>{{ item.dueDate }}</td>
                  <td>{{ item.total | currency: item.currency : 'symbol' : '1.2-2' }}</td>
                  <td>{{ item.currency }}</td>
                  <td><span class="badge">Draft</span></td>
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
                  {{ item.clientName }}</button
                ><span class="badge">Draft</span>
              </div>
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
          {{ items().length }} {{ items().length === 1 ? 'draft' : 'drafts' }}
        </p>
      }
      @if (opening()) {
        <p role="status">Opening draft…</p>
      }
    }
  `,
})
export class InvoicesPage {
  private readonly data = inject(InvoicesData);
  private readonly destroy = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly addButton = viewChild<ElementRef<HTMLButtonElement>>('addButton');
  readonly items = signal<InvoiceRecord[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly opening = signal(false);
  readonly editorOpen = signal(false);
  readonly selected = signal<InvoiceRecord | null>(null);
  search = '';
  currency = '';
  private request = 0;
  constructor() {
    void this.load();
  }
  async load() {
    const request = ++this.request;
    this.loading.set(true);
    this.error.set('');
    try {
      const items = await this.data.list(this.search.trim(), this.currency);
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
    this.editorOpen.set(false);
    afterNextRender(() => this.addButton()?.nativeElement.focus(), { injector: this.injector });
  }
  async edit(item: InvoiceRecord) {
    if (this.opening()) return;
    this.opening.set(true);
    this.error.set('');
    try {
      const current = await this.data.get(item.id);
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
