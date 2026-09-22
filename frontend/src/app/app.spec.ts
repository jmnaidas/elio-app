import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { AppShell } from './core/layout/app-shell';
import { SessionService } from './core/auth/session';
import { signal } from '@angular/core';
import { AuthPage } from './features/auth/auth-page';
import { Location } from '@angular/common';

function sessionMock(destination = '/') {
  return {
    load: vi.fn().mockResolvedValue(undefined),
    destination: signal(destination),
    authenticated: signal(destination !== '/login'),
    organization: signal({
      id: 'org-1',
      name: 'Example Studio',
      timeZone: 'Asia/Manila',
      defaultCurrency: 'PHP',
      version: 'v1',
    }),
    user: signal({
      id: 'user-1',
      email: 'owner@example.test',
      emailVerified: destination !== '/verify-email',
    }),
    pendingEmail: signal(''),
    mutate: vi.fn().mockResolvedValue(undefined),
    refresh: vi.fn().mockResolvedValue(undefined),
    logout: vi.fn().mockResolvedValue(undefined),
  };
}
function setup(destination = '/') {
  const session = sessionMock(destination);
  TestBed.configureTestingModule({
    providers: [provideRouter(routes), { provide: SessionService, useValue: session }],
  });
  return session;
}

describe('ELIO foundation', () => {
  it('renders accessible navigation and collapses the desktop sidebar', async () => {
    setup();
    const fixture = TestBed.createComponent(AppShell);
    await fixture.whenStable();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('nav')?.getAttribute('aria-label')).toBe('Main navigation');
    const button = root.querySelector<HTMLButtonElement>('[data-testid="collapse"]')!;
    button.click();
    await fixture.whenStable();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(root.textContent).not.toContain('₱');
  });
  for (const path of ['', 'invoices', 'receivables', 'clients', 'services', 'settings']) {
    it('loads /' + path, async () => {
      setup();
      const harness = await RouterTestingHarness.create('/' + path);
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe(
        path ? path[0].toUpperCase() + path.slice(1) : 'Dashboard',
      );
    });
  }
  it('shows a not-found page for an unknown URL', async () => {
    setup();
    const harness = await RouterTestingHarness.create('/missing');
    expect(harness.routeNativeElement?.textContent).toContain('Page not found');
  });
  for (const destination of ['/login', '/verify-email', '/onboarding']) {
    it(`redirects a protected route to ${destination} after restoring session`, async () => {
      const session = setup(destination);
      const harness = await RouterTestingHarness.create('/invoices');
      expect(TestBed.inject(Router).url).toBe(destination);
      expect(session.load).toHaveBeenCalled();
      expect(harness.routeNativeElement?.querySelector('.sidebar')).toBeNull();
    });
  }
  it('redirects a signed-in workspace owner away from login', async () => {
    setup();
    await RouterTestingHarness.create('/login');
    expect(TestBed.inject(Router).url).toBe('/');
  });
  it('shows a retry page when restoring session fails without redirect loops', async () => {
    const session = setup();
    session.load.mockRejectedValue(new Error('offline'));
    await RouterTestingHarness.create('/settings');
    expect(TestBed.inject(Router).url).toBe('/session-unavailable');
  });
  it('consumes a newly opened verification fragment while already on the same route', async () => {
    const session = setup('/login');
    const harness = await RouterTestingHarness.create('/verify-email');
    const page = harness.routeDebugElement!.componentInstance as AuthPage;
    await harness.navigateByUrl('/verify-email#userId=test-user&token=test-token');
    expect(harness.routeDebugElement!.componentInstance).toBe(page);
    expect(page.showEmail).toBe(false);
    expect(harness.routeNativeElement?.textContent).toContain('Verify email');
    expect(harness.routeNativeElement?.querySelector('input[type="email"]')).toBeNull();
    expect(TestBed.inject(Location).path(true)).toBe('/verify-email');
    await page.submit();
    expect(session.mutate).toHaveBeenCalledWith('/auth/verify-email', {
      userId: 'test-user',
      token: 'test-token',
    });
  });
  it('consumes a verification link on direct navigation', async () => {
    setup('/login');
    const harness = await RouterTestingHarness.create(
      '/verify-email#userId=test-user&token=test-token',
    );
    expect(harness.routeNativeElement?.textContent).toContain('Verify email');
    expect(harness.routeNativeElement?.querySelector('input[type="email"]')).toBeNull();
    expect(TestBed.inject(Location).path(true)).toBe('/verify-email');
  });
  for (const fragment of ['token=test-token', 'userId=test-user']) {
    it(`keeps the resend form for an incomplete verification fragment: ${fragment}`, async () => {
      setup('/login');
      const harness = await RouterTestingHarness.create('/verify-email');
      await harness.navigateByUrl(`/verify-email#${fragment}`);
      expect(harness.routeNativeElement?.textContent).toContain('Send verification link');
      expect(harness.routeNativeElement?.querySelector('input[type="email"]')).not.toBeNull();
    });
  }
});
