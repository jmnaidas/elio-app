import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Session, SessionService } from './session';
const owner: Session = {
  user: { id: '1', email: 'owner@example.test', emailVerified: true },
  organization: {
    id: '2',
    name: 'Example',
    timeZone: 'Asia/Manila',
    defaultCurrency: 'PHP',
    version: '3',
  },
  membership: { id: '4', organizationId: '2', role: 'Owner' },
};
describe('SessionService', () => {
  let service: SessionService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('restores server state once for concurrent guard requests', async () => {
    const first = service.load();
    const second = service.load();
    http.expectOne('/api/auth/session').flush(owner);
    await Promise.all([first, second]);
    expect(service.destination()).toBe('/');
    expect(service.organization()?.name).toBe('Example');
    await service.load();
    http.expectNone('/api/auth/session');
  });
  it('obtains csrf protection before login and then restores the cookie session', async () => {
    const pending = service.login('owner@example.test', 'Example-password1!');
    http.expectOne('/api/auth/antiforgery').flush({});
    await Promise.resolve();
    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    login.flush(null, { status: 204, statusText: 'No Content' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    http.expectOne('/api/auth/session').flush(owner);
    await pending;
    expect(service.authenticated()).toBe(true);
  });
  it('clears session only after the server logs out successfully', async () => {
    const load = service.load();
    http.expectOne('/api/auth/session').flush(owner);
    await load;
    const pending = service.logout();
    http.expectOne('/api/auth/antiforgery').flush({});
    await Promise.resolve();
    http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await pending;
    expect(service.user()).toBeNull();
    expect(service.organization()).toBeNull();
    expect(service.destination()).toBe('/login');
  });
  it('keeps state on failed logout and permits session restoration retry', async () => {
    const failed = service.load();
    const assertion = expect(failed).rejects.toBeTruthy();
    http.expectOne('/api/auth/session').flush({}, { status: 503, statusText: 'Unavailable' });
    await assertion;
    const retry = service.load();
    http.expectOne('/api/auth/session').flush(owner);
    await retry;
    const logout = service.logout();
    const rejection = expect(logout).rejects.toBeTruthy();
    http.expectOne('/api/auth/antiforgery').flush({});
    await Promise.resolve();
    http.expectOne('/api/auth/logout').flush({}, { status: 400, statusText: 'Bad Request' });
    await rejection;
    expect(service.authenticated()).toBe(true);
  });
  it('routes unverified users to verification and verified users without memberships to onboarding', async () => {
    const load = service.load();
    http
      .expectOne('/api/auth/session')
      .flush({
        user: { ...owner.user, emailVerified: false },
        organization: null,
        membership: null,
      });
    await load;
    expect(service.destination()).toBe('/verify-email');
    const refresh = service.refresh();
    http
      .expectOne('/api/auth/session')
      .flush({ user: owner.user, organization: null, membership: null });
    await refresh;
    expect(service.destination()).toBe('/onboarding');
  });
});
