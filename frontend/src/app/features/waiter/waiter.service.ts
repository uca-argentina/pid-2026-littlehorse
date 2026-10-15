import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type WaiterTable = components['schemas']['WaiterTableResponse'];

export type TakenTable = components['schemas']['TakenTableResponse'];

/**
 * No venue in the path: everything here is done with a token, and the token
 * carries the venue. Putting it in the URL would offer a knob that must never
 * be turned.
 */
export const WAITER_TABLES_URL = '/api/waiter/tables';

export function takeTableUrl(orderId: string): string {
  return `${WAITER_TABLES_URL}/${orderId}/take`;
}

export function deliverTableUrl(orderId: string): string {
  return `${WAITER_TABLES_URL}/${orderId}/deliver`;
}

@Injectable({ providedIn: 'root' })
export class WaiterService {
  private readonly http = inject(HttpClient);

  /** "La llevo yo". Asking again, as the same waiter, changes nothing. */
  take(orderId: string): Observable<TakenTable> {
    return this.http.post<TakenTable>(takeTableUrl(orderId), {});
  }

  /**
   * "Entregado" (US-44): the table's order, left at its table. Only the waiter
   * who took it can; the answer has no body.
   */
  deliver(orderId: string): Observable<void> {
    return this.http.post<void>(deliverTableUrl(orderId), {});
  }
}
