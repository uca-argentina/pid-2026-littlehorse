import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Night = components['schemas']['NightResponse'];

export type NewNight = components['schemas']['CreateNightRequest'];

/** No venue in the path: the token carries it, as with every staff endpoint. */
export const NIGHTS_URL = '/api/nights';

@Injectable({ providedIn: 'root' })
export class NightsService {
  private readonly http = inject(HttpClient);

  create(night: NewNight): Observable<Night> {
    return this.http.post<Night>(NIGHTS_URL, night);
  }
}
