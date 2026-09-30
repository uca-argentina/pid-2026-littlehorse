import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** No venue in the path: the station's token carries it, same as every other staff route. */
const KDS_ORDERS_URL = '/api/kds/orders';

/** US-20: what the bar's camera or reader read off the customer's phone. */
export const SCAN_URL = '/api/kds/scan';

/** Whose order a scan found and what it did — from the generated contract. */
export type ScannedOrder = components['schemas']['ScannedOrderResponse'];

/** What a scan did, or why it did nothing. */
export type ScanOutcome = components['schemas']['ScanOutcomeName'];

export function startPreparingUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/start-preparing`;
}

export function returnToQueueUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/return-to-queue`;
}

export function markReadyUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/mark-ready`;
}

export function returnToPreparationUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/return-to-preparation`;
}

export function deliverUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/deliver`;
}

export function undoDeliveryUrl(code: string): string {
  return `${KDS_ORDERS_URL}/${encodeURIComponent(code)}/undo-delivery`;
}

/**
 * US-16: what the bar does to an order. Nothing comes back: the board learns
 * the new state the same way every other tablet does, by reloading its queue.
 */
@Injectable({ providedIn: 'root' })
export class KdsOrdersService {
  private readonly http = inject(HttpClient);

  /** The board's "Preparar". Several chosen together are several calls to this. */
  startPreparing(code: string): Observable<void> {
    return this.http.post<void>(startPreparingUrl(code), {});
  }

  returnToQueue(code: string): Observable<void> {
    return this.http.post<void>(returnToQueueUrl(code), {});
  }

  /** US-18: "Listo". Several chosen together are several calls to this. */
  markReady(code: string): Observable<void> {
    return this.http.post<void>(markReadyUrl(code), {});
  }

  returnToPreparation(code: string): Observable<void> {
    return this.http.post<void>(returnToPreparationUrl(code), {});
  }

  /** "Entregado" by hand, when the order cannot be scanned. */
  deliver(code: string): Observable<void> {
    return this.http.post<void>(deliverUrl(code), {});
  }

  /** "Deshacer", right after a mistaken delivery. */
  undoDelivery(code: string): Observable<void> {
    return this.http.post<void>(undoDeliveryUrl(code), {});
  }

  /**
   * US-20: hands over the ready order a customer's QR leads to. Unlike the
   * buttons, it answers whose order it was and why it did nothing, if it did.
   */
  scan(read: string): Observable<ScannedOrder> {
    return this.http.post<ScannedOrder>(SCAN_URL, { read });
  }
}
