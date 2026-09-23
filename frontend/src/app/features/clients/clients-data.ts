import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-config';
import { SessionService } from '../../core/auth/session';

export interface ClientInput {
  name: string;
  currency: string;
  email: string;
  phone: string | null;
  billingAddress: string | null;
  notes: string | null;
  version?: string;
}
export interface ClientRecord extends ClientInput {
  id: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: string;
}
@Injectable({ providedIn: 'root' })
export class ClientsData {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);
  private readonly session = inject(SessionService);
  list(search = '', status = 'active') {
    return firstValueFrom(
      this.http.get<ClientRecord[]>(`${this.base}/clients`, { params: { search, status } }),
    );
  }
  get(id: string) {
    return firstValueFrom(this.http.get<ClientRecord>(`${this.base}/clients/${id}`));
  }
  save(input: ClientInput, id?: string) {
    return this.session.mutate<ClientRecord>(
      `/clients${id ? '/' + id : ''}`,
      input,
      id ? 'PATCH' : 'POST',
    );
  }
  setActive(record: ClientRecord, active: boolean) {
    return this.session.mutate<ClientRecord>(
      `/clients/${record.id}/${active ? 'reactivate' : 'deactivate'}`,
      { version: record.version },
    );
  }
}
