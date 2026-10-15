import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Night = components['schemas']['NightResponse'];

export type NewNight = components['schemas']['CreateNightRequest'];

/** US-37: bar by bar, what each product was loaded with, sold and has left, and what the storeroom has. */
export type NightStock = components['schemas']['NightStockResponse'];

/** A VIP table the night sells, with its budget and what it spent (US-40). */
export type NightTable = components['schemas']['NightTableResponse'];

export type NightTableCode = components['schemas']['NightTableCodeResponse'];

export type NightStockBar = NightStock['bars'][number];

export type NightStockLine = NightStockBar['lines'][number];

/** No venue in the path: the token carries it, as with every staff endpoint. */
export const NIGHTS_URL = '/api/nights';

@Injectable({ providedIn: 'root' })
export class NightsService {
  private readonly http = inject(HttpClient);

  create(night: NewNight): Observable<Night> {
    return this.http.post<Night>(NIGHTS_URL, night);
  }

  /** The whole form, as on creation. The API decides what a night still allows. */
  update(id: string, night: NewNight): Observable<Night> {
    return this.http.put<Night>(`${NIGHTS_URL}/${id}`, night);
  }

  /**
   * Moves units between the storeroom and one bar: into it when positive, back
   * when negative. A change and never a total, so a sale made while the screen
   * was open is kept.
   */
  /** US-40: a new code for the table's QR. The old one stops working; the balance stays. */
  regenerateTableCode(nightId: string, tableId: string): Observable<NightTableCode> {
    return this.http.post<NightTableCode>(
      `${NIGHTS_URL}/${nightId}/tables/${tableId}/access-code`,
      {},
    );
  }

  adjustStock(
    nightId: string,
    kdsId: string,
    productId: string,
    change: number,
  ): Observable<NightStock> {
    return this.http.post<NightStock>(
      `${NIGHTS_URL}/${nightId}/stock/${kdsId}/${productId}/adjust`,
      { change },
    );
  }
}
