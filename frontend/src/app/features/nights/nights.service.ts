import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Night = components['schemas']['NightResponse'];

export type NewNight = components['schemas']['CreateNightRequest'];

/** US-37: what each product started the night with, what was loaded, sold and is left. */
export type NightStock = components['schemas']['NightStockResponse'];

export type NightStockLine = NightStock['lines'][number];

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

  /** A change and never a total, so a sale made while the screen was open is kept. */
  adjustStock(nightId: string, productId: string, change: number): Observable<NightStock> {
    return this.http.post<NightStock>(`${NIGHTS_URL}/${nightId}/stock/${productId}/adjust`, {
      change,
    });
  }
}
