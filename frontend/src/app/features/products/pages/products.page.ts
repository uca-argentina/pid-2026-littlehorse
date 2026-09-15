import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { PRODUCTS_URL } from '../products.service';
import type { Product } from '../products.service';

/** One row of the listing, with the price already written the way the menu writes it. */
interface ProductRow {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly imageUrl: string | null;
  readonly price: string;
  readonly stock: number;
  readonly isAvailable: boolean;
  readonly isSoldOut: boolean;
  readonly isActive: boolean;
}

/** Why the listing is not on screen. */
type ListingFailure = 'none' | 'forbidden' | 'unreachable';

/**
 * Pesos as the menu writes them: "$ 4.500", and "$ 5.200,50" only when there
 * are cents — never "$ 5.200,5". The API sends a plain number, and Intl knows
 * the venue's separators.
 */
function pesos(amount: number): string {
  const digits = Number.isInteger(amount) ? 0 : 2;

  return new Intl.NumberFormat('es-AR', {
    style: 'currency',
    currency: 'ARS',
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(amount);
}

@Component({
  selector: 'drinkit-products-page',
  imports: [AdminHeader, RouterLink],
  styleUrl: './products.page.scss',
  templateUrl: './products.page.html',
})
export class ProductsPage {
  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /**
   * The Resource API rather than a subscription: loading, error and value
   * arrive as signals, which is exactly the three states this screen draws.
   */
  protected readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  protected readonly rows = computed<ProductRow[]>(() =>
    (this.products.hasValue() ? this.products.value() : []).map((product) => ({
      id: product.id,
      name: product.name,
      description: product.description ?? null,
      imageUrl: product.imageUrl ?? null,
      price: pesos(product.price),
      stock: product.stock,
      isAvailable: product.isAvailable,
      isSoldOut: product.isSoldOut,
      isActive: product.isActive,
    })),
  );

  /**
   * The two failures are worth telling apart: one goes away when the signal
   * comes back, and the other never will, however many times they retry.
   */
  protected readonly failure = computed<ListingFailure>(() => {
    const error = this.products.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  protected readonly isEmpty = computed(() => this.products.hasValue() && this.rows().length === 0);
}

/**
 * A resource reports the failure wrapped, keeping the original underneath in
 * "cause", so the response has to be dug out rather than cast.
 */
function problemTypeOf(error: unknown): string | undefined {
  const response = error instanceof HttpErrorResponse ? error : (error as Error | null)?.cause;

  return response instanceof HttpErrorResponse
    ? (response.error as { type?: string } | null)?.type
    : undefined;
}
