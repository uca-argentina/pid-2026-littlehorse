import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { Cart, NOTE_MAX_LENGTH } from '../../../core/cart/cart';
import { anonymously } from '../../../core/auth/anonymous-request';
import { GlassMark } from '../../../shared/glass-mark/glass-mark';
import { PRODUCT_PLACEHOLDER } from '../../../shared/product-image/product-placeholder';
import { formatPrice } from '../../../shared/money/price';
import { ThemeToggle } from '../../../shared/theme-toggle/theme-toggle';
import { menuUrl } from '../menu.service';
import type { Menu, MenuItem } from '../menu.service';

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

/** US-14: the tab the menu opens on. A category's id is a guid and never equals it. */
const ALL = 'all';

interface CategoryTab {
  readonly filter: string;
  readonly name: string;
}

/**
 * The menu a customer reads after scanning the venue's QR. The first screen of
 * the app that belongs to nobody in particular: no session, no account, no
 * install, and the venue comes from the address they arrived at.
 */
@Component({
  selector: 'drinkit-menu-page',
  imports: [GlassMark, RouterLink, ThemeToggle],
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

  /** Opens on 'all', same as every visit: nothing about the last visit is remembered. */
  protected readonly activeCategory = signal<string>(ALL);

  /**
   * A wrong address is not a bad connection. Offering "check your signal" and
   * a retry that can never succeed sends somebody to fight their network over
   * a QR that was never ours.
   */
  protected readonly isWrongAddress = computed(
    () => problemTypeOf(this.menu.error()) === ProblemTypes.venueNotFound,
  );

  // Cached rather than read straight from the resource: a reload (the retry
  // button) keeps the previous value around while it is in flight, which would
  // otherwise make the header flicker back to the raw slug mid-retry.
  private readonly lastVenueName = signal('');

  protected readonly venueName = computed(() => this.lastVenueName());

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  // Also gated on !isLoading(): a reload (venue switch or retry) keeps the
  // previous response's items around while the new one is in flight, and
  // acting on them (e.g. adding one to the cart) would act on the wrong venue.
  private readonly everything = computed<MenuItem[]>(() =>
    this.menu.hasValue() && !this.menu.isLoading() ? this.menu.value().items : [],
  );

  /**
   * "Todos" first, then the venue's own categories in the order it made them:
   * criterion 2 of US-14. None at all means nothing to split by, so no strip.
   */
  protected readonly categoryTabs = computed<CategoryTab[]>(() => {
    const categories =
      this.menu.hasValue() && !this.menu.isLoading() ? this.menu.value().categories : [];

    return categories.length === 0
      ? []
      : [
          { filter: ALL, name: 'Todos' },
          ...categories.map(({ id, name }) => ({ filter: id, name })),
        ];
  });

  protected readonly cards = computed<MenuCard[]>(() => {
    const term = this.search().trim().toLowerCase();
    const category = this.activeCategory();

    return (
      this.everything()
        .filter((item) => term === '' || item.name.toLowerCase().includes(term))
        // Criterion 3: sold-out ones included — this only narrows by category, the
        // way isOrderable already leaves sold-out and switched-off ones in.
        .filter((item) => category === ALL || item.categoryId === category)
        .map((item) => ({
          id: item.id,
          name: item.name,
          description: item.description,
          image: item.imageUrl ?? PRODUCT_PLACEHOLDER,
          amount: item.price,
          price: formatPrice(item.price),
          isOrderable: item.isOrderable,
        }))
    );
  });

  /** "1 ítem", "2 ítems". One drink is one, and the plural is not free. */
  protected readonly howMany = computed(() =>
    this.cart.count() === 1 ? '1 ítem' : `${this.cart.count()} ítems`,
  );

  protected readonly orderTotal = computed(() => formatPrice(this.cart.total()));

  protected readonly orderLink = computed(() => ['/', this.venueSlug(), 'order']);

  protected readonly noteMaxLength = NOTE_MAX_LENGTH;

  /**
   * Which card has its note field open, if any. One at a time: a field under
   * every card turns a menu somebody is reading into a form to fill in.
   */
  private readonly writingOn = signal<string | null>(null);

  protected isWritingOn(productId: string): boolean {
    return this.writingOn() === productId;
  }

  protected writeNoteOn(productId: string): void {
    this.writingOn.update((open) => (open === productId ? null : productId));
  }

  protected noteFor(productId: string, event: Event): void {
    this.cart.setNote(productId, (event.target as HTMLInputElement).value);
  }

  constructor() {
    // The order belongs to the venue whose address is open, and switching
    // venues has to switch orders rather than carry one into the other.
    effect(() => this.cart.open(this.venueSlug()));

    effect(() => {
      if (this.menu.hasValue()) this.lastVenueName.set(this.menu.value().venueName);
    });
  }

  protected addToOrder(card: MenuCard): void {
    this.cart.add({ id: card.id, name: card.name, price: card.amount, imageUrl: card.image });
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
