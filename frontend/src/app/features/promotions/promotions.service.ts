import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Promotion = components['schemas']['PromotionResponse'];

export type PromotionData = components['schemas']['PromotionRequest'];

/** Staff take their venue from the token: no slug in the path, like the products. */
export const PROMOTIONS_URL = '/api/staff/promotions';

/**
 * The days of the week as the screen lists them, Monday first, each with the
 * number the API uses (0 is Sunday, like JavaScript's getDay).
 */
export const WEEK: readonly {
  readonly day: number;
  readonly short: string;
  readonly long: string;
}[] = [
  { day: 1, short: 'Lun', long: 'Lunes' },
  { day: 2, short: 'Mar', long: 'Martes' },
  { day: 3, short: 'Mié', long: 'Miércoles' },
  { day: 4, short: 'Jue', long: 'Jueves' },
  { day: 5, short: 'Vie', long: 'Viernes' },
  { day: 6, short: 'Sáb', long: 'Sábado' },
  { day: 0, short: 'Dom', long: 'Domingo' },
];

/** "23:00:00" from the API, "23:00" for the screen and the time input. */
export function clock(time: string): string {
  return time.slice(0, 5);
}

@Injectable({ providedIn: 'root' })
export class PromotionsService {
  private readonly http = inject(HttpClient);

  create(promotion: PromotionData): Observable<Promotion> {
    return this.http.post<Promotion>(PROMOTIONS_URL, promotion);
  }

  update(id: string, promotion: PromotionData): Observable<Promotion> {
    return this.http.put<Promotion>(`${PROMOTIONS_URL}/${id}`, promotion);
  }

  /** US-54: switched on again. */
  activate(id: string): Observable<Promotion> {
    return this.http.post<Promotion>(`${PROMOTIONS_URL}/${id}/activate`, {});
  }

  /** US-54, criterion 4: stops applying to new orders; the confirmed ones keep their price. */
  deactivate(id: string): Observable<Promotion> {
    return this.http.post<Promotion>(`${PROMOTIONS_URL}/${id}/deactivate`, {});
  }
}
