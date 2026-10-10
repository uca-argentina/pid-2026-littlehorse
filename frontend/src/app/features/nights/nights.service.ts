import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Night = components['schemas']['NightResponse'];

export type NewNight = components['schemas']['CreateNightRequest'];

/** One product of a night's stock (US-37): what it loaded, what it sold and what is left. */
export type NightStockLine = components['schemas']['NightStockLineResponse'];

/** What a product's stock of a night amounts to once an adjustment is in. */
export type NightStockFigures = components['schemas']['NightStockFiguresResponse'];

/** No venue in the path: the token carries it, as with every staff endpoint. */
export const NIGHTS_URL = '/api/nights';

/** Reading it opens the stock of the night when it does not exist yet. */
export function nightStockUrl(nightId: string): string {
  return `${NIGHTS_URL}/${nightId}/stock`;
}

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
   * Moves a product's stock of a night by a number of units, up or down. Never
   * a new total: the API adds it to what is there, so a sale made while the
   * screen was open is kept.
   */
  adjustStock(nightId: string, productId: string, change: number): Observable<NightStockFigures> {
    return this.http.post<NightStockFigures>(`${nightStockUrl(nightId)}/${productId}/adjust`, {
      change,
    });
  }
}
