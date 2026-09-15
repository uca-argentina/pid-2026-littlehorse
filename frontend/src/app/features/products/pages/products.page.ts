import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { PRODUCTS_URL, PRODUCT_PLACEHOLDER } from '../products.service';
import type { Product } from '../products.service';

/** One row of the listing, with the price already written the way the menu writes it. */
interface ProductRow {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  /** The product's own picture, or the placeholder when it has none. */
  readonly imageUrl: string;
  readonly price: string;
  readonly stock: number;
  readonly isAvailable: boolean;
  readonly isSoldOut: boolean;
  readonly isActive: boolean;
}

/** Which products the listing is narrowed to, or all of them. */
type StockFilter = 'all' | 'soldOut' | 'inactive';

/** One of the pills above the list, with what it would leave on screen. */
interface FilterPill {
  readonly filter: StockFilter;
  readonly name: string;
  readonly count: number;
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

  /**
   * What the administrator typed, and which state they narrowed to. Both are
   * applied here and not by the API: a venue's whole menu fits in one
   * response, and asking the server on every keystroke would put the venue's
   * connection between somebody and the product they are looking at.
   */
  protected readonly search = signal('');

  protected readonly stockFilter = signal<StockFilter>('all');

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  private readonly everything = computed<ProductRow[]>(() =>
    (this.products.hasValue() ? this.products.value() : []).map((product) => ({
      id: product.id,
      name: product.name,
      description: product.description ?? null,
      imageUrl: product.imageUrl ?? PRODUCT_PLACEHOLDER,
      price: pesos(product.price),
      stock: product.stock,
      isAvailable: product.isAvailable,
      isSoldOut: product.isSoldOut,
      isActive: product.isActive,
    })),
  );

  /** Whatever matches what was typed, before the state narrows it further. */
  private readonly matchingTheSearch = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (term === '') return this.everything();

    return this.everything().filter((row) => row.name.toLowerCase().includes(term));
  });

  protected readonly rows = computed(() => {
    const filter = this.stockFilter();

    if (filter === 'all') return this.matchingTheSearch();

    return this.matchingTheSearch().filter((row) =>
      filter === 'soldOut' ? row.isSoldOut : !row.isActive,
    );
  });

  /**
   * Counted over what the search left, not over the whole menu. A pill that
   * promises three and then shows none reads as a filter that is broken.
   */
  protected readonly pills = computed<FilterPill[]>(() => {
    const matching = this.matchingTheSearch();

    return [
      { filter: 'all', name: 'Todos', count: matching.length },
      {
        filter: 'soldOut',
        name: 'Sin stock',
        count: matching.filter((row) => row.isSoldOut).length,
      },
      {
        filter: 'inactive',
        name: 'Dados de baja',
        count: matching.filter((row) => !row.isActive).length,
      },
    ];
  });

  /**
   * The two failures are worth telling apart: one goes away when the signal
   * comes back, and the other never will, however many times they retry.
   */
  protected readonly failure = computed<ListingFailure>(() => {
    const error = this.products.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  /** Nothing is loaded at all. Not the same as nothing matching, and said differently. */
  protected readonly isEmpty = computed(
    () => this.products.hasValue() && this.everything().length === 0,
  );

  /** There are products, and none of them survived the search or the filter. */
  protected readonly nothingMatches = computed(
    () => this.everything().length > 0 && this.rows().length === 0,
  );

  /**
   * US-06, criterion 5. Set on the element rather than in the row: the row is
   * recomputed from the API response, and would put the broken address back.
   */
  protected showPlaceholder(event: Event): void {
    const picture = event.target as HTMLImageElement;

    if (!picture.src.endsWith(PRODUCT_PLACEHOLDER)) picture.src = PRODUCT_PLACEHOLDER;
  }

  protected narrowTo(filter: StockFilter): void {
    this.stockFilter.set(filter);
  }

  protected searchFor(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
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
