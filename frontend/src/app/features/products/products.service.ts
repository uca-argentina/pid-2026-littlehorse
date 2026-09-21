import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Product = components['schemas']['ProductResponse'];

export type NewProduct = components['schemas']['CreateProductRequest'];

export type ProductImage = components['schemas']['ProductImageResponse'];

/**
 * What the form lets through before a byte goes up. Kept in step with
 * UploadProductImageHandler on the server, which decides the same by the
 * bytes rather than by what the browser says the file is.
 */
export const IMAGE_TYPES: readonly string[] = ['image/jpeg', 'image/png', 'image/webp'];

export const IMAGE_MAX_BYTES = 5 * 1024 * 1024;

export { PRODUCT_PLACEHOLDER } from '../../shared/product-image/product-placeholder';

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

  /** Multipart, with the file in the "image" part, as the API expects it. */
  uploadImage(productId: string, image: File): Observable<ProductImage> {
    const form = new FormData();
    form.append('image', image, image.name);

    return this.http.put<ProductImage>(`${PRODUCTS_URL}/${productId}/image`, form);
  }

  /** US-07: the nightly switch off. The customer keeps seeing the product, dimmed. */
  markUnavailable(productId: string): Observable<Product> {
    return this.http.post<Product>(`${PRODUCTS_URL}/${productId}/mark-unavailable`, {});
  }

  /** US-07: the switch back on, e.g. after restocking. Does not touch stock. */
  markAvailable(productId: string): Observable<Product> {
    return this.http.post<Product>(`${PRODUCTS_URL}/${productId}/mark-available`, {});
  }
}
