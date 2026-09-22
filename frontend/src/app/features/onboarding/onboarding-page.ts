import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthLayout } from '../auth/auth-layout';
import { OrganizationForm } from '../../shared/ui/organization-form';
import { errorMessage, OrganizationInput, SessionService } from '../../core/auth/session';
@Component({
  selector: 'app-onboarding-page',
  imports: [AuthLayout, OrganizationForm],
  template: `<app-auth-layout
    ><p class="eyebrow">YOUR WORKSPACE</p>
    <h1>A home for your business</h1>
    <p class="form-intro">Set the essentials. You can change these later in Settings.</p>
    @if (error()) {
      <p role="alert" class="form-error">{{ error() }}</p>
    }
    <app-organization-form [busy]="busy()" (saved)="save($event)" />
    <div class="auth-links">
      <button type="button" class="text-button" (click)="logout()" [disabled]="busy()">
        Sign out
      </button>
    </div></app-auth-layout
  >`,
})
export class OnboardingPage {
  readonly session = inject(SessionService);
  private readonly router = inject(Router);
  readonly busy = signal(false);
  readonly error = signal('');
  async save(input: OrganizationInput) {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.session.saveOrganization(input, true);
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  async logout() {
    this.busy.set(true);
    try {
      await this.session.logout();
      await this.router.navigateByUrl('/login');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
