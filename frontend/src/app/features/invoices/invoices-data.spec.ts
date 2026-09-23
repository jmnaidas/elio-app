import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { API_BASE_URL } from '../../core/http/api-config';
import { SessionService } from '../../core/auth/session';
import { InvoicesData, InvoiceInput } from './invoices-data';

describe('InvoicesData', () => {
  it('uses scoped list/detail endpoints and the existing CSRF mutation gateway', async () => {
    const mutate = vi.fn().mockResolvedValue({ id: 'draft' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: SessionService, useValue: { mutate } },
      ],
    });
    const data = TestBed.inject(InvoicesData);
    const http = TestBed.inject(HttpTestingController);
    const listing = data.list('Studio', 'PHP');
    const request = http.expectOne('/api/invoices?search=Studio&currency=PHP');
    expect(request.request.method).toBe('GET');
    request.flush([]);
    await listing;
    const detail = data.get('draft');
    http.expectOne('/api/invoices/draft').flush({ id: 'draft' });
    await detail;
    const input: InvoiceInput = {
      clientId: 'client',
      currency: 'PHP',
      issueDate: '2026-09-23',
      dueDate: '2026-09-30',
      notes: '',
      paymentInstructions: '',
      lines: [{ serviceId: null, description: 'Work', quantity: '0.25', unitPrice: '10.01' }],
    };
    await data.save(input);
    expect(mutate).toHaveBeenLastCalledWith('/invoices', input, 'POST');
    await data.save({ ...input, version: 'v1' }, 'draft');
    expect(mutate).toHaveBeenLastCalledWith(
      '/invoices/draft',
      { ...input, version: 'v1' },
      'PATCH',
    );
    http.verify();
  });
});
