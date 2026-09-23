import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../../core/http/api-config';
import { SessionService } from '../../core/auth/session';

export interface ServiceInput {
  name: string;
  currency: string;
  description: string | null;
  defaultUnitPrice: string;
  version?: string;
}
export interface ServiceRecord extends ServiceInput {
  id: string;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  version: string;
}
@Injectable({ providedIn: 'root' })
export class ServicesData {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);
  private readonly session = inject(SessionService);
  list(search = '', status = 'active') {
    return firstValueFrom(
      this.http.get<ServiceRecord[]>(`${this.base}/services`, { params: { search, status } }),
    );
  }
  get(id: string) {
    return firstValueFrom(this.http.get<ServiceRecord>(`${this.base}/services/${id}`));
  }
  save(input: ServiceInput, id?: string) {
    return this.session.mutate<ServiceRecord>(
      `/services${id ? '/' + id : ''}`,
      input,
      id ? 'PATCH' : 'POST',
    );
  }
  setActive(record: ServiceRecord, active: boolean) {
    return this.session.mutate<ServiceRecord>(
      `/services/${record.id}/${active ? 'reactivate' : 'deactivate'}`,
      { version: record.version },
    );
  }
}
