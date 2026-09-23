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
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { errorMessage, SessionService } from '../../core/auth/session';
import { ServicesData, ServiceRecord } from './services-data';

@Component({
  selector: 'app-service-editor',
  imports: [ReactiveFormsModule, DatePipe],
  styleUrl: '../../shared/ui/catalog.scss',
  template: `<dialog #dialog aria-labelledby="service-editor-title" (cancel)="cancel($event)">
    <div class="editor-top">
      <h2 id="service-editor-title">{{ record() ? 'Service details' : 'Add service' }}</h2>
      <button class="text-button" type="button" (click)="close()" [disabled]="busy()">Close</button>
    </div>
    <p class="form-intro">A reusable starting point for future invoice items.</p>
    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }
    @if (stale()) {
      <button type="button" class="text-button" (click)="reload()" [disabled]="busy()">
        Reload latest service
      </button>
    }
    @if (notice()) {
      <p class="notice" role="status">{{ notice() }}</p>
    }
    <form [formGroup]="form" (ngSubmit)="save()" [attr.aria-busy]="busy()">
      <fieldset [disabled]="busy()" class="editor-form">
        <label for="service-name">Service name</label
        ><input id="service-name" formControlName="name" maxlength="160" required autofocus />
        @if (invalid('name')) {
          <p class="field-error">Enter a name of at most 160 characters.</p>
        }

        <label for="service-description"
          >Description <span class="field-help">(optional)</span></label
        ><textarea
          id="service-description"
          formControlName="description"
          rows="3"
          maxlength="2000"
        ></textarea>
        <label for="service-price">Default unit price</label
        ><input
          id="service-price"
          type="text"
          inputmode="decimal"
          formControlName="defaultUnitPrice"
          aria-describedby="price-help"
          required
        />
        <p id="price-help" class="field-help">
          Zero or more, up to 12 digits and 2 decimal places. No currency conversion.
        </p>
        @if (invalid('defaultUnitPrice')) {
          <p class="field-error">Enter a non-negative price with at most 2 decimal places.</p>
        }
        <label for="service-currency">Currency</label
        ><select id="service-currency" formControlName="currency">
          <option value="PHP">PHP · Philippine peso</option>
          <option value="USD">USD · US dollar</option>
        </select>
        <div class="actions">
          <button class="primary-button" type="submit">
            {{ busy() ? 'Saving…' : record() ? 'Save changes' : 'Create service' }}</button
          ><button type="button" class="secondary-button" (click)="close()">Cancel</button>
        </div>
      </fieldset>
    </form>
    @if (record(); as item) {
      <section class="status-section" aria-label="Service status">
        <span class="badge" [class.inactive]="!item.isActive">{{
          item.isActive ? 'Active' : 'Inactive'
        }}</span>
        <p class="record-meta">
          Created {{ item.createdAtUtc | date: 'mediumDate' }} · Updated
          {{ item.updatedAtUtc | date: 'mediumDate' }}
        </p>
        <button
          type="button"
          class="text-button"
          [disabled]="busy()"
          (click)="confirming.set(true)"
        >
          {{ item.isActive ? 'Deactivate service' : 'Reactivate service' }}
        </button>
        @if (confirming()) {
          <div class="confirm-box" role="group" aria-label="Confirm status change">
            <p>
              {{
                item.isActive
                  ? 'Deactivate this service? It will remain stored and can be reactivated later.'
                  : 'Reactivate this service and return it to the active list?'
              }}
            </p>
            <button
              type="button"
              class="secondary-button"
              [disabled]="busy()"
              (click)="changeStatus()"
            >
              Confirm {{ item.isActive ? 'deactivation' : 'reactivation' }}
            </button>
            <button
              type="button"
              class="text-button"
              [disabled]="busy()"
              (click)="confirming.set(false)"
            >
              Keep current status
            </button>
          </div>
        }
      </section>
    }
  </dialog>`,
})
export class ServiceEditor {
  readonly initial = input<ServiceRecord | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();
  private readonly data = inject(ServicesData);
  private readonly session = inject(SessionService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  readonly record = signal<ServiceRecord | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly stale = signal(false);
  readonly notice = signal('');
  readonly confirming = signal(false);
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(160), Validators.pattern(/\S/)]],
    description: ['', Validators.maxLength(2000)],
    defaultUnitPrice: ['0.00', [Validators.required, Validators.pattern(/^\d{1,12}(\.\d{1,2})?$/)]],
    currency: [
      this.session.organization()?.defaultCurrency ?? 'PHP',
      [Validators.required, Validators.pattern(/^(PHP|USD)$/)],
    ],
  });
  constructor() {
    afterNextRender(() => {
      this.apply(this.initial());
      this.dialog().nativeElement.showModal();
    });
  }
  private apply(item: ServiceRecord | null) {
    this.record.set(item);
    if (item) this.form.patchValue({ ...item, description: item.description ?? '' });
  }
  invalid(name: string) {
    const control = this.form.get(name);
    return control?.touched && control.invalid;
  }
  cancel(event: Event) {
    event.preventDefault();
    if (!this.busy()) this.close();
  }
  close() {
    if (this.busy()) return;
    this.dialog().nativeElement.close();
    this.closed.emit();
  }
  async save() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.stale.set(false);
    this.notice.set('');
    try {
      const result = await this.data.save(
        { ...this.form.getRawValue(), version: this.record()?.version },
        this.record()?.id,
      );
      this.apply(result);
      this.notice.set('Service saved.');
      this.changed.emit();
    } catch (error) {
      this.handle(error);
    } finally {
      this.busy.set(false);
    }
  }
  async changeStatus() {
    const item = this.record();
    if (!item || !this.confirming() || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const updated = await this.data.setActive(item, !item.isActive);
      // Preserve any unsaved form edits when only changing active status.
      this.record.set(updated);
      this.confirming.set(false);
      this.changed.emit();
      this.notice.set(updated.isActive ? 'Service reactivated.' : 'Service deactivated.');
    } catch (error) {
      this.handle(error);
    } finally {
      this.busy.set(false);
    }
  }
  async reload() {
    const item = this.record();
    if (!item || this.busy()) return;
    this.busy.set(true);
    try {
      this.apply(await this.data.get(item.id));
      this.error.set('');
      this.stale.set(false);
      this.confirming.set(false);
    } catch (error) {
      this.handle(error);
    } finally {
      this.busy.set(false);
    }
  }
  private handle(error: unknown) {
    this.error.set(errorMessage(error));
    this.stale.set(
      typeof error === 'object' && error !== null && 'status' in error && error.status === 409,
    );
  }
}
