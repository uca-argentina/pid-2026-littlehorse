import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { ProblemTypes } from '../../../core/api/problem-types';
import { Cart } from '../../../core/cart/cart';
import { anonymously } from '../../../core/auth/anonymous-request';
import { PRODUCT_PLACEHOLDER } from '../../../shared/product-image/product-placeholder';
import { menuUrl } from '../menu.service';
import type { Menu, MenuItem } from '../menu.service';

/**
 * Argentine format, with both decimals always. Built once rather than per card:
 * a menu redraws on every keystroke of the search, and a formatter is not free.
 */
const PRICE = new Intl.NumberFormat('es-AR', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** One card, with everything already in the shape the template draws. */
interface MenuCard {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly image: string;
  readonly amount: number;
  readonly price: string;
  readonly isOrderable: boolean;
}

/**
 * The menu a customer reads after scanning the venue's QR. The first screen of
 * the app that belongs to nobody in particular: no session, no account, no
 * install, and the venue comes from the address they arrived at.
 */
@Component({
  selector: 'drinkit-menu-page',
  styleUrl: './menu.page.scss',
  templateUrl: './menu.page.html',
})
export class MenuPage {
  protected readonly cart = inject(Cart);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /**
   * The Resource API rather than a subscription: loading, error and value
   * arrive as signals, which is exactly the states criteria 3, 4 and 5 ask for.
   */
  protected readonly menu = httpResource<Menu>(() => ({
    url: menuUrl(this.venueSlug()),

    // With no Authorization header, ever. The API prefers a token's venue over
    // the slug on staff endpoints, and a session left behind on this phone is
    // not what the QR on the wall points at.
    context: anonymously(),
  }));

  protected readonly search = signal('');

  /**
   * A wrong address is not a bad connection. Offering "check your signal" and
   * a retry that can never succeed sends somebody to fight their network over
   * a QR that was never ours.
   */
  protected readonly isWrongAddress = computed(
    () => problemTypeOf(this.menu.error()) === ProblemTypes.venueNotFound,
  );

  protected readonly venueName = computed(() =>
    this.menu.hasValue() ? this.menu.value().venueName : '',
  );

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  private readonly everything = computed<MenuItem[]>(() =>
    this.menu.hasValue() ? this.menu.value().items : [],
  );

  protected readonly cards = computed<MenuCard[]>(() => {
    const term = this.search().trim().toLowerCase();

    return this.everything()
      .filter((item) => term === '' || item.name.toLowerCase().includes(term))
      .map((item) => ({
        id: item.id,
        name: item.name,
        description: item.description,
        image: item.imageUrl ?? PRODUCT_PLACEHOLDER,
        amount: item.price,
        price: PRICE.format(item.price),
        isOrderable: item.isOrderable,
      }));
  });

  /** "1 ítem", "2 ítems". One drink is one, and the plural is not free. */
  protected readonly howMany = computed(() =>
    this.cart.count() === 1 ? '1 ítem' : `${this.cart.count()} ítems`,
  );

  protected readonly orderTotal = computed(() => PRICE.format(this.cart.total()));

  constructor() {
    // The order belongs to the venue whose address is open, and switching
    // venues has to switch orders rather than carry one into the other.
    effect(() => this.cart.open(this.venueSlug()));
  }

  protected addToOrder(card: MenuCard): void {
    this.cart.add({ id: card.id, name: card.name, price: card.amount });
  }

  protected takeOneOut(card: MenuCard): void {
    this.cart.subtract(card.id);
  }

  /** The venue has not loaded anything. Criterion 5, and its own message. */
  protected readonly isEmpty = computed(
    () => this.menu.hasValue() && this.everything().length === 0,
  );

  /** There is a menu, and the search left nothing of it. A different message. */
  protected readonly nothingMatches = computed(
    () => this.everything().length > 0 && this.cards().length === 0,
  );

  /**
   * reload() and not a counter folded into the request: the address does not
   * change between attempts, so a resource built from it would compare equal
   * and never ask again.
   */
  protected retry(): void {
    this.menu.reload();
  }

  protected searchFor(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  /**
   * The address was good enough for the domain and the storage still failed, or
   * the picture was deleted behind our back. Criterion 5 of US-06: the customer
   * sees the product anyway, never a broken image.
   */
  protected useThePlaceholder(event: Event): void {
    const image = event.target as HTMLImageElement;

    if (!image.src.endsWith(PRODUCT_PLACEHOLDER)) image.src = PRODUCT_PLACEHOLDER;
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
