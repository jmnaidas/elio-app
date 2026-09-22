import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { PageHeader } from '../../shared/ui/page-header';
import { OrganizationForm } from '../../shared/ui/organization-form';
import { errorMessage, OrganizationInput, SessionService } from '../../core/auth/session';
@Component({
  selector: 'app-settings-page',
  imports: [PageHeader, OrganizationForm],
  template: `<app-page-header
      title="Settings"
      description="Make the workspace work for your business."
    />
    <section class="settings-panel">
      <h2>Organization</h2>
      <p class="form-intro">Manage the essentials for your workspace.</p>
      @if (message()) {
        <p class="notice" role="status">{{ message() }}</p>
      }
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      @if (stale()) {
        <button class="text-button" type="button" (click)="reload()">Reload latest settings</button>
      }
      <app-organization-form
        [organization]="session.organization()"
        [busy]="busy()"
        (saved)="save($event)"
      />
    </section>`,
  styles: `
    .settings-panel {
      max-width: 580px;
      padding: 28px;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: 12px;
    }
    h2 {
      font-size: 20px;
    }
    @media (max-width: 480px) {
      .settings-panel {
        padding: 20px;
      }
    }
  `,
})
export class SettingsPage {
  readonly session = inject(SessionService);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly stale = signal(false);
  async save(input: OrganizationInput) {
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    this.stale.set(false);
    try {
      await this.session.saveOrganization(input);
      this.message.set('Organization settings saved.');
    } catch (error) {
      this.error.set(errorMessage(error));
      this.stale.set(error instanceof HttpErrorResponse && error.status === 409);
    } finally {
      this.busy.set(false);
    }
  }
  async reload() {
    this.busy.set(true);
    try {
      await this.session.refresh();
      this.stale.set(false);
      this.error.set('');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
