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
import { ClientsData, ClientRecord } from './clients-data';

@Component({
  selector: 'app-client-editor',
  imports: [ReactiveFormsModule, DatePipe],
  styleUrl: '../../shared/ui/catalog.scss',
  template: `<dialog #dialog aria-labelledby="client-editor-title" (cancel)="cancel($event)">
    <div class="editor-top">
      <h2 id="client-editor-title">{{ record() ? 'Client details' : 'Add client' }}</h2>
      <button class="text-button" type="button" (click)="close()" [disabled]="busy()">Close</button>
    </div>
    <p class="form-intro">Keep billing contacts and preferences together.</p>
    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }
    @if (stale()) {
      <button type="button" class="text-button" (click)="reload()" [disabled]="busy()">
        Reload latest client
      </button>
    }
    @if (notice()) {
      <p class="notice" role="status">{{ notice() }}</p>
    }
    <form [formGroup]="form" (ngSubmit)="save()" [attr.aria-busy]="busy()">
      <fieldset [disabled]="busy()" class="editor-form">
        <label for="client-name">Client name</label
        ><input id="client-name" formControlName="name" maxlength="160" required autofocus />
        @if (invalid('name')) {
          <p class="field-error">Enter a name of at most 160 characters.</p>
        }

        <label for="client-email">Email address</label
        ><input
          id="client-email"
          type="email"
          formControlName="email"
          autocomplete="email"
          maxlength="254"
          required
        />
        @if (invalid('email')) {
          <p class="field-error">Enter a valid email address.</p>
        }
        <label for="client-phone">Phone <span class="field-help">(optional)</span></label
        ><input
          id="client-phone"
          type="tel"
          formControlName="phone"
          autocomplete="tel"
          maxlength="50"
        />
        <label for="client-address"
          >Billing address <span class="field-help">(optional)</span></label
        ><textarea
          id="client-address"
          formControlName="billingAddress"
          autocomplete="street-address"
          rows="3"
          maxlength="1000"
        ></textarea>
        <label for="client-notes">Notes <span class="field-help">(optional)</span></label
        ><textarea id="client-notes" formControlName="notes" rows="3" maxlength="2000"></textarea>
        <label for="client-currency">Preferred currency</label
        ><select id="client-currency" formControlName="currency">
          <option value="PHP">PHP · Philippine peso</option>
          <option value="USD">USD · US dollar</option>
        </select>
        <div class="actions">
          <button class="primary-button" type="submit">
            {{ busy() ? 'Saving…' : record() ? 'Save changes' : 'Create client' }}</button
          ><button type="button" class="secondary-button" (click)="close()">Cancel</button>
        </div>
      </fieldset>
    </form>
    @if (record(); as item) {
      <section class="status-section" aria-label="Client status">
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
          {{ item.isActive ? 'Deactivate client' : 'Reactivate client' }}
        </button>
        @if (confirming()) {
          <div class="confirm-box" role="group" aria-label="Confirm status change">
            <p>
              {{
                item.isActive
                  ? 'Deactivate this client? It will remain stored and can be reactivated later.'
                  : 'Reactivate this client and return it to the active list?'
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
export class ClientEditor {
  readonly initial = input<ClientRecord | null>(null);
  readonly closed = output<void>();
  readonly changed = output<void>();
  private readonly data = inject(ClientsData);
  private readonly session = inject(SessionService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  readonly record = signal<ClientRecord | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly stale = signal(false);
  readonly notice = signal('');
  readonly confirming = signal(false);
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(160), Validators.pattern(/\S/)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    phone: ['', Validators.maxLength(50)],
    billingAddress: ['', Validators.maxLength(1000)],
    notes: ['', Validators.maxLength(2000)],
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
  private apply(item: ClientRecord | null) {
    this.record.set(item);
    if (item)
      this.form.patchValue({
        ...item,
        phone: item.phone ?? '',
        billingAddress: item.billingAddress ?? '',
        notes: item.notes ?? '',
      });
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
      this.notice.set('Client saved.');
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
      this.notice.set(updated.isActive ? 'Client reactivated.' : 'Client deactivated.');
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
