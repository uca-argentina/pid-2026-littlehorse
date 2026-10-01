import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** No venue in the path: the cashier's token carries it, same as every other staff route. */
export const CASHIER_ORDERS_URL = '/api/cashier/orders';

/** Where the QR's token goes, in the body: it is the customer's proof, and addresses get logged. */
export const CASHIER_SCAN_URL = '/api/cashier/scan';

/** "Cobros de tu turno": what the signed-in cashier took during the shift. */
export const MY_COLLECTIONS_URL = '/api/cashier/collections';

export function cashierOrderUrl(code: string): string {
  return `${CASHIER_ORDERS_URL}/${encodeURIComponent(code)}`;
}

export function collectUrl(code: string): string {
  return `${cashierOrderUrl(code)}/collect`;
}

/** US-23: the customer left without paying. */
export function cancelAtTheTillUrl(code: string): string {
  return `${cashierOrderUrl(code)}/cancel`;
}

/** Taken from the generated contract, so nothing here can drift from the API. */
export type CashierOrder = components['schemas']['CashierOrderResponse'];

/** US-26: what the till asks and does. Both lists are httpResources on the page. */
@Injectable({ providedIn: 'root' })
export class CashierService {
  private readonly http = inject(HttpClient);

  find(code: string): Observable<CashierOrder> {
    return this.http.get<CashierOrder>(cashierOrderUrl(code));
  }

  scan(read: string): Observable<CashierOrder> {
    return this.http.post<CashierOrder>(CASHIER_SCAN_URL, { read });
  }

  collect(code: string): Observable<void> {
    return this.http.post<void>(collectUrl(code), {});
  }

  cancel(code: string): Observable<void> {
    return this.http.post<void>(cancelAtTheTillUrl(code), {});
  }
}
