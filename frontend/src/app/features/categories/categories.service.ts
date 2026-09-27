import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Category = components['schemas']['CategoryResponse'];

export type NewCategory = components['schemas']['CreateCategoryRequest'];

/**
 * No venue in the path: everything here is done with a token, and the token
 * carries the venue. Putting it in the URL would offer a knob that must never
 * be turned.
 */
export const CATEGORIES_URL = '/api/staff/categories';

@Injectable({ providedIn: 'root' })
export class CategoriesService {
  private readonly http = inject(HttpClient);

  create(category: NewCategory): Observable<Category> {
    return this.http.post<Category>(CATEGORIES_URL, category);
  }
}
