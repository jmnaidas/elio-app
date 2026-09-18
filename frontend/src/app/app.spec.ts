import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { App } from './app';

describe('ELIO foundation', () => {
  it('renders accessible navigation and collapses the desktop sidebar', async () => {
    TestBed.configureTestingModule({ imports: [App], providers: [provideRouter(routes)] });
    const fixture = TestBed.createComponent(App);
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
      TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
      const harness = await RouterTestingHarness.create('/' + path);
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe(
        path ? path[0].toUpperCase() + path.slice(1) : 'Dashboard',
      );
    });
  }
  it('shows a not-found page for an unknown URL', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
    const harness = await RouterTestingHarness.create('/missing');
    expect(harness.routeNativeElement?.textContent).toContain('Page not found');
  });
});
