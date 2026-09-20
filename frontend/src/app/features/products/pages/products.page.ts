import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import type { WritableSignal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { PRODUCTS_URL, PRODUCT_PLACEHOLDER, ProductsService } from '../products.service';
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
  readonly isToggling: boolean;
  readonly toggleFailed: boolean;
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
  private readonly productsService = inject(ProductsService);

  private readonly destroyRef = inject(DestroyRef);

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

  /**
   * US-07: the rows waiting on the switch, so a second tap on one of them is
   * ignored instead of racing the first request. A set and not a single id:
   * turning three drinks off in a row is one tap after another, and the second
   * tap must not re-enable the button of the first while it is still in flight.
   */
  private readonly toggling = signal<ReadonlySet<string>>(new Set());

  /**
   * The rows whose last switch never reached the API. Kept per row rather than
   * as one message for the screen, because the warning has to sit where the
   * thumb already is: on a long menu a note at the top is off-screen.
   */
  private readonly toggleFailures = signal<ReadonlySet<string>>(new Set());

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
      isToggling: this.toggling().has(product.id),
      toggleFailed: this.toggleFailures().has(product.id),
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

  /**
   * US-07: flips the nightly switch. A full reload rather than patching the
   * row by hand keeps this screen agreeing with whatever the API actually
   * saved, at the cost of one extra request the venue's connection can afford.
   *
   * A failure is said out loud and not swallowed: silence here leaves somebody
   * walking away sure they took a drink off sale while the bar keeps selling
   * it, which is the exact thing this story exists to prevent.
   */
  protected toggleAvailability(row: ProductRow): void {
    if (row.isToggling) return;

    this.markAs(this.toggling, row.id, true);
    this.markAs(this.toggleFailures, row.id, false);

    const request = row.isAvailable
      ? this.productsService.markUnavailable(row.id)
      : this.productsService.markAvailable(row.id);

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.markAs(this.toggling, row.id, false);
        this.products.reload();
      },
      error: () => {
        this.markAs(this.toggling, row.id, false);
        this.markAs(this.toggleFailures, row.id, true);
      },
    });
  }

  /** A new set every time, so the computed rows above see the change. */
  private markAs(which: WritableSignal<ReadonlySet<string>>, id: string, member: boolean): void {
    which.update((ids) => {
      const next = new Set(ids);

      if (member) next.add(id);
      else next.delete(id);

      return next;
    });
  }
}
