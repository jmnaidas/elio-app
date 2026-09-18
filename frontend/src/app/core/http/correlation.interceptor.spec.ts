import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { correlationInterceptor } from './correlation.interceptor';

describe('API correlation', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationInterceptor])),
        provideHttpClientTesting(),
      ],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  it('adds a correlation ID only to API requests', () => {
    const client = TestBed.inject(HttpClient);
    const http = TestBed.inject(HttpTestingController);
    client.get('/api/health').subscribe();
    const api = http.expectOne('/api/health');
    expect(api.request.headers.get('X-Correlation-ID')).toBeTruthy();
    api.flush({});
    client.get('/api-other/resource').subscribe();
    const external = http.expectOne('/api-other/resource');
    expect(external.request.headers.has('X-Correlation-ID')).toBe(false);
    external.flush({});
  });
});
