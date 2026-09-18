import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { API_BASE_URL } from './api-config';
export const correlationInterceptor: HttpInterceptorFn = (request, next) => {
  const base = inject(API_BASE_URL).replace(/\/$/, '');
  if (
    (request.url === base || request.url.startsWith(base + '/')) &&
    !request.headers.has('X-Correlation-ID')
  ) {
    request = request.clone({ setHeaders: { 'X-Correlation-ID': crypto.randomUUID() } });
  }
  return next(request);
};
