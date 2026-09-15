import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Product = components['schemas']['ProductResponse'];

export type NewProduct = components['schemas']['CreateProductRequest'];

/**
 * No venue in the path, unlike the login request. Everything here is done with
 * a token, and the token carries the venue: putting it in the URL would offer
 * a knob that must never be turned.
 */
export const PRODUCTS_URL = '/api/staff/products';

@Injectable({ providedIn: 'root' })
export class ProductsService {
  private readonly http = inject(HttpClient);

  create(product: NewProduct): Observable<Product> {
    return this.http.post<Product>(PRODUCTS_URL, product);
  }
}
