import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-config';
import { SessionService } from '../../core/auth/session';

export interface InvoiceLineInput {
  id?: string;
  serviceId: string | null;
  description: string;
  quantity: string;
  unitPrice: string;
}
export interface InvoiceInput {
  clientId: string;
  currency: string;
  issueDate: string;
  dueDate: string;
  notes: string;
  paymentInstructions: string;
  lines: InvoiceLineInput[];
  version?: string;
}
export interface InvoiceRecord extends Omit<InvoiceInput, 'notes' | 'paymentInstructions'> {
  id: string;
  notes: string | null;
  paymentInstructions: string | null;
  clientName: string;
  clientEmail: string;
  billingAddress: string | null;
  clientIsActive: boolean;
  lifecycle: 'Draft';
  subtotal: string;
  total: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: string;
  lines: (InvoiceLineInput & { id: string; lineTotal: string; sortOrder: number })[];
}
@Injectable({ providedIn: 'root' })
export class InvoicesData {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);
  private readonly session = inject(SessionService);
  list(search = '', currency = '') {
    return firstValueFrom(
      this.http.get<InvoiceRecord[]>(`${this.base}/invoices`, { params: { search, currency } }),
    );
  }
  get(id: string) {
    return firstValueFrom(this.http.get<InvoiceRecord>(`${this.base}/invoices/${id}`));
  }
  save(input: InvoiceInput, id?: string) {
    return this.session.mutate<InvoiceRecord>(
      `/invoices${id ? '/' + id : ''}`,
      input,
      id ? 'PATCH' : 'POST',
    );
  }
}
