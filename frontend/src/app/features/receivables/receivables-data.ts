import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-config';
import { SessionService } from '../../core/auth/session';
export interface Receivable {
  invoiceId: string; invoiceNumber: string; clientName: string; currency: string;
  issueDate: string; dueDate: string; total: string; amountPaid: string; balanceDue: string;
  paymentStatus: 'Unpaid' | 'PartiallyPaid' | 'Paid'; deliveryStatus: 'NotSent' | 'Sent';
}
export interface PaymentInput { amount: string; receivedAtUtc: string; method: string; reference: string; notes: string; }
export interface Payment extends PaymentInput { id: string; currency: string; createdAtUtc: string; createdBy: string; }
export interface ReceivableDetail { summary: Receivable; payments: Payment[]; }
export const paymentStatusLabel = (status: string) => status === 'PartiallyPaid' ? 'Partially Paid' : status;
@Injectable({ providedIn: 'root' })
export class ReceivablesData {
  private readonly http = inject(HttpClient); private readonly base = inject(API_BASE_URL); private readonly session = inject(SessionService);
  list(search = '', status = '', currency = '') {
    return firstValueFrom(this.http.get<Receivable[]>(`${this.base}/receivables`, { params: { search, status, currency } }));
  }
  get(id: string) { return firstValueFrom(this.http.get<ReceivableDetail>(`${this.base}/receivables/${id}`)); }
  record(id: string, input: PaymentInput) { return this.session.mutate<ReceivableDetail>(`/invoices/${id}/payments`, input); }
}
