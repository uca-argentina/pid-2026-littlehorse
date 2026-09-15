import { Injectable, computed, signal } from '@angular/core';

/** One drink in the order, with however many of it were asked for. */
export interface CartLine {
  readonly productId: string;
  readonly name: string;
  readonly unitPrice: number;
  readonly quantity: number;
  /** "sin hielo". Belongs to this drink and not to the whole order. */
  readonly note: string | null;
}

/** What the menu hands over when somebody taps the plus on a card. */
export interface CartAddition {
  readonly id: string;
  readonly name: string;
  readonly price: number;
}

export const CART_STORAGE_PREFIX = 'drinkit.cart.';

/**
 * The order somebody is putting together, before there is any order on the
 * server. Kept per venue: a night that starts at one bar and continues at
 * another is two orders, and neither should show up inside the other.
 */
/**
 * In localStorage rather than sessionStorage: US-10 asks that an order survive
 * somebody taking a phone call, and a phone is free to kill the tab while the
 * screen is off. The name and the price are copied in beside the id — the cart
 * has to draw itself with no network, and what it shows has to be what was
 * agreed to when it was added.
 */
@Injectable({ providedIn: 'root' })
export class Cart {
  private readonly venue = signal<string | null>(null);

  private readonly stored = signal<CartLine[]>([]);

  readonly lines = this.stored.asReadonly();

  /** Units, not lines: three drinks are three, however many cards they came from. */
  readonly count = computed(() => this.stored().reduce((units, line) => units + line.quantity, 0));

  readonly total = computed(() =>
    this.stored().reduce((sum, line) => sum + line.quantity * line.unitPrice, 0),
  );

  readonly isEmpty = computed(() => this.count() === 0);

  /** Called by a venue's screen when it opens. Loads that venue's order. */
  open(venueSlug: string): void {
    if (this.venue() === venueSlug) return;

    this.venue.set(venueSlug);
    this.stored.set(read(venueSlug));
  }

  /**
   * Adding a drink that is already there raises its quantity rather than
   * opening a second line for it: two taps on one card mean two of that drink.
   */
  add(product: CartAddition): void {
    const existing = this.stored().find((line) => line.productId === product.id);

    this.save(
      existing === undefined
        ? [
            ...this.stored(),
            {
              productId: product.id,
              name: product.name,
              unitPrice: product.price,
              quantity: 1,
              note: null,
            },
          ]
        : this.stored().map((line) =>
            line.productId === product.id ? { ...line, quantity: line.quantity + 1 } : line,
          ),
    );
  }

  /**
   * Takes one out. The last one leaves the drink altogether rather than
   * leaving a line sitting at zero: a card showing "0" next to a minus is a
   * control that does nothing, and the menu would have to explain it.
   */
  subtract(productId: string): void {
    this.save(
      this.stored().flatMap((line) => {
        if (line.productId !== productId) return [line];
        if (line.quantity <= 1) return [];

        return [{ ...line, quantity: line.quantity - 1 }];
      }),
    );
  }

  /** How many of one drink are in the order. Zero when it is not. */
  quantityOf(productId: string): number {
    return this.stored().find((line) => line.productId === productId)?.quantity ?? 0;
  }

  private save(lines: CartLine[]): void {
    this.stored.set(lines);

    const venueSlug = this.venue();

    if (venueSlug === null) return;

    try {
      localStorage.setItem(CART_STORAGE_PREFIX + venueSlug, JSON.stringify(lines));
    } catch {
      // A private window, a browser blocking site data, storage that is full.
      // Losing the order when the tab closes is bad; a menu that refuses to
      // work at all is worse, and the signal above already holds it for now.
    }
  }
}

function read(venueSlug: string): CartLine[] {
  try {
    const stored = localStorage.getItem(CART_STORAGE_PREFIX + venueSlug);

    return stored === null ? [] : (JSON.parse(stored) as CartLine[]);
  } catch {
    // Nothing there, or something half-written by an older version of the app.
    // Starting clean beats rendering an order nobody can explain.
    return [];
  }
}
