import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionService } from '../../core/auth/session';
import { ServicesData } from './services-data';

describe('ServicesData', () => {
  it('uses scoped API queries and the existing CSRF-protected mutation path', async () => {
    const session = { mutate: vi.fn().mockResolvedValue({}) };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionService, useValue: session },
      ],
    });
    const api = TestBed.inject(ServicesData);
    const http = TestBed.inject(HttpTestingController);
    const list = api.list('Example', 'inactive');
    const request = http.expectOne((r) => r.url === '/api/services');
    expect(request.request.params.get('search')).toBe('Example');
    expect(request.request.params.get('status')).toBe('inactive');
    request.flush([]);
    await list;
    const record = {
      id: 'record-1',
      name: 'Example',
      currency: 'PHP',
      description: null,
      defaultUnitPrice: '12.34',
      isActive: true,
      version: 'v1',
      createdAtUtc: '2026-09-23T00:00:00Z',
      updatedAtUtc: '2026-09-23T00:00:00Z',
    };
    await api.save(record, record.id);
    expect(session.mutate).toHaveBeenCalledWith('/services/record-1', record, 'PATCH');
    await api.setActive(record, false);
    expect(session.mutate).toHaveBeenLastCalledWith('/services/record-1/deactivate', {
      version: 'v1',
    });
    http.verify();
  });
});
