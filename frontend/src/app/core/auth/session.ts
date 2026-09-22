import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../http/api-config';
export interface Organization {
  id: string;
  name: string;
  timeZone: string;
  defaultCurrency: string;
  version: string;
}
export interface OrganizationInput {
  name: string;
  timeZone: string;
  defaultCurrency: string;
  version?: string;
}
export interface Session {
  user: { id: string; email: string; emailVerified: boolean } | null;
  organization: Organization | null;
  membership: { id: string; organizationId: string; role: string } | null;
}
const anonymous: Session = { user: null, organization: null, membership: null };
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);
  private readonly state = signal<Session>(anonymous);
  private pending?: Promise<void>;
  private loaded = false;
  readonly session = this.state.asReadonly();
  readonly user = computed(() => this.state().user);
  readonly organization = computed(() => this.state().organization);
  readonly authenticated = computed(() => !!this.user());
  readonly pendingEmail = signal('');
  readonly destination = computed(() =>
    !this.user()
      ? '/login'
      : !this.user()!.emailVerified
        ? '/verify-email'
        : !this.organization()
          ? '/onboarding'
          : '/',
  );
  load(): Promise<void> {
    if (this.loaded) return Promise.resolve();
    return (this.pending ??= this.refresh().finally(() => (this.pending = undefined)));
  }
  async refresh() {
    this.state.set(await firstValueFrom(this.http.get<Session>(`${this.base}/auth/session`)));
    this.loaded = true;
  }
  async mutate<T>(path: string, body: unknown, method: 'POST' | 'PATCH' = 'POST'): Promise<T> {
    // Refresh protection after identity changes or logout in another tab. Never replay mutations.
    await firstValueFrom(this.http.get(`${this.base}/auth/antiforgery`));
    return firstValueFrom(this.http.request<T>(method, `${this.base}${path}`, { body }));
  }
  async login(email: string, password: string) {
    await this.mutate('/auth/login', { email, password });
    await this.refresh();
  }
  async logout() {
    await this.mutate('/auth/logout', {});
    this.state.set(anonymous);
    this.loaded = true;
  }
  async saveOrganization(input: OrganizationInput, create = false) {
    const organization = await this.mutate<Organization>(
      '/organization',
      input,
      create ? 'POST' : 'PATCH',
    );
    await this.refresh();
    return organization;
  }
}
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0)
      return 'ELIO could not be reached. Check your connection and try again.';
    if (error.status === 429) return 'Too many attempts. Please wait a minute before trying again.';
    if (error.status === 401)
      return 'Sign-in was unsuccessful or your session has expired. Please sign in again.';
    const errors = error.error?.errors as Record<string, string[]> | undefined;
    return errors
      ? Object.values(errors).flat().join(' ')
      : (error.error?.title ?? 'The request could not be completed. Please try again.');
  }
  return 'Something went wrong. Please try again.';
}
