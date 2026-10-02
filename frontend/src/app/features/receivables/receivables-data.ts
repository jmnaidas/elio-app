import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-config';
import { SessionService } from '../../core/auth/session';
export interface Receivable {
  invoiceId: string; invoiceNumber: string; clientName: string; currency: string;
  issueDate: string; dueDate: string; total: string; amountPaid: string; balanceDue: string;
  dueState: 'NotDue' | 'DueSoon' | 'DueToday' | 'Overdue' | 'Paid'; daysOverdue: number; asOfDate: string; timeZone: string; clientEmail: string;
  paymentStatus: 'Unpaid' | 'PartiallyPaid' | 'Paid'; deliveryStatus: 'NotSent' | 'Sent';
}
export interface PaymentInput { amount: string; receivedAtUtc: string; method: string; reference: string; notes: string; }
export interface Payment extends PaymentInput { id: string; currency: string; createdAtUtc: string; createdBy: string; }
export interface ReceivableDetail { summary: Receivable; payments: Payment[]; }
export interface InvoiceReminder { id: string; recipientEmail: string; attemptedAtUtc: string; sentAtUtc: string | null; status: 'Pending' | 'Sent' | 'Failed'; channel: string; failureCode: string | null; amountPaid: string; balanceDue: string; }
export const dueStateLabel = (state: string) => ({ NotDue: 'Not due', DueSoon: 'Due soon', DueToday: 'Due today', Overdue: 'Overdue', Paid: 'Paid' }[state] || state);
export const paymentStatusLabel = (status: string) => status === 'PartiallyPaid' ? 'Partially Paid' : status;
@Injectable({ providedIn: 'root' })
export class ReceivablesData {
  private readonly http = inject(HttpClient); private readonly base = inject(API_BASE_URL); private readonly session = inject(SessionService);
  list(search = '', status = '', currency = '', dueState = '') {
    return firstValueFrom(this.http.get<Receivable[]>(`${this.base}/receivables`, { params: { search, status, currency, dueState } }));
  }
  get(id: string) { return firstValueFrom(this.http.get<ReceivableDetail>(`${this.base}/receivables/${id}`)); }
  reminders(id: string) { return firstValueFrom(this.http.get<InvoiceReminder[]>(`${this.base}/invoices/${id}/reminders`)); }
  remind(id: string, recipientEmail: string) { return this.session.mutate<InvoiceReminder>(`/invoices/${id}/reminders`, { recipientEmail }); }
  record(id: string, input: PaymentInput) { return this.session.mutate<ReceivableDetail>(`/invoices/${id}/payments`, input); }
}
