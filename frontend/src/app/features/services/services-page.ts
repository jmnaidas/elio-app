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
import { DatePipe, CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PageHeader } from '../../shared/ui/page-header';
import { errorMessage } from '../../core/auth/session';
import { ServicesData, ServiceRecord } from './services-data';
import { ServiceEditor } from './service-editor';

@Component({
  selector: 'app-services-page',
  imports: [PageHeader, FormsModule, DatePipe, CurrencyPipe, ServiceEditor],
  styleUrl: '../../shared/ui/catalog.scss',
  template: `
    <div class="page-top">
      <app-page-header
        title="Services"
        description="Your service library, ready to reuse."
      /><button #addButton type="button" class="primary-button" (click)="add()">Add service</button>
    </div>
    <form class="toolbar" (ngSubmit)="load()">
      <label class="search"
        >Search services<input
          type="search"
          name="search"
          [(ngModel)]="search"
          placeholder="Search by name"
          maxlength="160"
      /></label>
      <label
        >Status<select name="status" [(ngModel)]="status" (ngModelChange)="load()">
          <option value="active">Active</option>
          <option value="inactive">Inactive</option>
          <option value="all">All statuses</option>
        </select></label
      >
      <button type="submit" class="secondary-button">Search</button>
    </form>
    @if (error()) {
      <p class="form-error" role="alert">
        {{ error() }} <button type="button" class="text-button" (click)="load()">Retry</button>
      </p>
    }
    @if (loading()) {
      <p class="empty" role="status">Loading services…</p>
    } @else if (!error() && items().length === 0) {
      <section class="empty">
        <h2>
          {{
            search || status !== 'active'
              ? 'No matching services'
            : 'Your service library starts here'
          }}
        </h2>
        <p>
          {{
            search || status !== 'active'
              ? 'Try a different search or status filter.'
              : 'Save your first service to reduce repetition later.'
          }}
        </p>
      </section>
    } @else if (!error()) {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">
            Service list
          </caption>
          <thead>
            <tr>
              <th scope="col">Service</th>
              <th scope="col">Default price</th>
              <th scope="col">Currency</th>
              <th scope="col">Status</th>
              <th scope="col">Updated</th>
            </tr>
          </thead>
          <tbody>
            @for (item of items(); track item.id) {
              <tr>
                <td>
                  <button class="row-link" type="button" (click)="edit(item)">
                    {{ item.name }}
                  </button>
                </td>
                <td>{{ item.defaultUnitPrice | currency: item.currency : 'symbol' : '1.2-2' }}</td>
                <td>{{ item.currency }}</td>
                <td>
                  <span class="badge" [class.inactive]="!item.isActive">{{
                    item.isActive ? 'Active' : 'Inactive'
                  }}</span>
                </td>
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
              <button class="row-link" type="button" (click)="edit(item)">{{ item.name }}</button
              ><span class="badge" [class.inactive]="!item.isActive">{{
                item.isActive ? 'Active' : 'Inactive'
              }}</span>
            </div>
            <p>{{ item.defaultUnitPrice | currency: item.currency : 'symbol' : '1.2-2' }}</p>
            <p>{{ item.currency }} · Updated {{ item.updatedAtUtc | date: 'mediumDate' }}</p>
          </article>
        }
      </div>
      <p class="count" role="status">
        {{ items().length }} {{ items().length === 1 ? 'service' : 'services' }}
      </p>
    }
    @if (opening()) {
      <p role="status">Opening service…</p>
    }
    @if (editorOpen()) {
      <app-service-editor [initial]="selected()" (closed)="closeEditor()" (changed)="load()" />
    }
  `,
})
export class ServicesPage {
  private readonly data = inject(ServicesData);
  private readonly destroy = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly addButton = viewChild.required<ElementRef<HTMLButtonElement>>('addButton');
  readonly items = signal<ServiceRecord[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly opening = signal(false);
  readonly editorOpen = signal(false);
  readonly selected = signal<ServiceRecord | null>(null);
  search = '';
  status = 'active';
  private request = 0;
  constructor() {
    void this.load();
  }
  async load() {
    const request = ++this.request;
    this.loading.set(true);
    this.error.set('');
    try {
      const items = await this.data.list(this.search.trim(), this.status);
      if (!this.destroy.destroyed && request === this.request) this.items.set(items);
    } catch (error) {
      if (!this.destroy.destroyed && request === this.request) this.error.set(errorMessage(error));
    } finally {
      if (!this.destroy.destroyed && request === this.request) this.loading.set(false);
    }
  }
  add() {
    if (this.opening()) return;
    this.selected.set(null);
    this.editorOpen.set(true);
  }
  closeEditor() {
    this.editorOpen.set(false);
    // The original row may have disappeared after deactivation or a filter refresh.
    afterNextRender(() => this.addButton().nativeElement.focus(), { injector: this.injector });
  }
  async edit(item: ServiceRecord) {
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
