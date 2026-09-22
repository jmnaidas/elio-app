import { Component, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Organization, OrganizationInput } from '../../core/auth/session';
@Component({
  selector: 'app-organization-form',
  imports: [ReactiveFormsModule],
  template: `<form
    [formGroup]="form"
    (ngSubmit)="submit()"
    class="elio-form"
    [attr.aria-busy]="busy()"
  >
    <label for="organization-name">Organization name</label
    ><input
      id="organization-name"
      formControlName="name"
      autocomplete="organization"
      maxlength="120"
      required
    />
    @if (form.controls.name.touched && form.controls.name.invalid) {
      <p class="field-error">Use 2–120 characters for your organization name.</p>
    }
    <label for="timezone">Timezone</label
    ><select id="timezone" formControlName="timeZone" required>
      @for (zone of zones; track zone) {
        <option [value]="zone">{{ zone.replaceAll('_', ' ') }}</option>
      }
    </select>
    <p class="field-help">Your business’s local timezone, including daylight saving changes.</p>
    <label for="currency">Default currency</label
    ><select id="currency" formControlName="defaultCurrency" required>
      <option value="PHP">PHP · Philippine peso</option>
      <option value="USD">USD · US dollar</option>
    </select>
    <p class="field-help">The default for future invoices. ELIO does not convert currencies.</p>
    <button type="submit" class="primary-button" [disabled]="busy()">
      {{ busy() ? 'Saving…' : organization() ? 'Save changes' : 'Create workspace' }}
    </button>
  </form>`,
})
export class OrganizationForm {
  readonly organization = input<Organization | null>(null);
  readonly busy = input(false);
  readonly saved = output<OrganizationInput>();
  readonly zones = [...new Set(['Asia/Manila', 'Etc/UTC', ...Intl.supportedValuesOf('timeZone')])];
  readonly form = inject(FormBuilder).nonNullable.group({
    name: [
      '',
      [
        Validators.required,
        Validators.minLength(2),
        Validators.maxLength(120),
        Validators.pattern(/\S.*\S/),
      ],
    ],
    timeZone: ['Asia/Manila', Validators.required],
    defaultCurrency: ['PHP', Validators.required],
  });
  constructor() {
    effect(() => {
      const organization = this.organization();
      if (organization) this.form.patchValue(organization);
    });
  }
  submit() {
    this.form.markAllAsTouched();
    if (this.form.valid && !this.busy())
      this.saved.emit({ ...this.form.getRawValue(), version: this.organization()?.version });
  }
}
