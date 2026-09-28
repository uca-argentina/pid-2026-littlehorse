import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

/** No venue in the path: the station's token carries it, same as every other staff route. */
const KDS_ORDERS_URL = '/api/kds/orders';

export function startPreparingUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/start-preparing`;
}

export function returnToQueueUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/return-to-queue`;
}

/**
 * US-16: what the bar does to an order. Nothing comes back: the board learns
 * the new state the same way every other tablet does, by reloading its queue.
 */
@Injectable({ providedIn: 'root' })
export class KdsOrdersService {
  private readonly http = inject(HttpClient);

  /** The board's "Imprimir". Several chosen together are several calls to this. */
  startPreparing(code: string): Observable<void> {
    return this.http.post<void>(startPreparingUrl(code), {});
  }

  returnToQueue(code: string): Observable<void> {
    return this.http.post<void>(returnToQueueUrl(code), {});
  }
}
