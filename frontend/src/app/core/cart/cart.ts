import { Injectable, computed, inject, signal } from '@angular/core';
import { BrowserStore } from '../storage/browser-store';

/** One drink in the order, with however many of it were asked for. */
export interface CartLine {
  readonly productId: string;
  readonly name: string;
  readonly imageUrl: string | null;
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
  /** Whatever the card was showing — the real photo or the placeholder. */
  readonly imageUrl?: string | null;
}

export const CART_STORAGE_PREFIX = 'drinkit.cart.';

/**
 * How long an aclaración can be. Enough for "sin hielo, con mucho limón" and
 * short enough to be read at a glance on a ticket in a dark bar: a paragraph
 * would be read by nobody and would slow the whole queue down.
 */
export const NOTE_MAX_LENGTH = 120;

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
  private readonly store = inject(BrowserStore);

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
    this.stored.set(this.read(venueSlug));
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
              imageUrl: product.imageUrl ?? null,
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

  /**
   * Takes the drink out altogether, however many there were. The minus walks a
   * quantity down one at a time; this is for somebody who changed their mind
   * about the drink and should not have to tap it away five times.
   */
  remove(productId: string): void {
    this.save(this.stored().filter((line) => line.productId !== productId));
  }

  /**
   * Writes the aclaración onto one drink. Blank takes it back rather than
   * leaving an empty one: the bar would be handed a line with a blank remark on
   * it and have to wonder what was meant.
   */
  setNote(productId: string, note: string): void {
    const written = note.trim().slice(0, NOTE_MAX_LENGTH);

    this.save(
      this.stored().map((line) =>
        line.productId === productId ? { ...line, note: written === '' ? null : written } : line,
      ),
    );
  }

  /**
   * Empties it. Called once the order is the server's and has a code of its
   * own: going back to the carta then starts a new one rather than showing the
   * one that was already paid for.
   */
  clear(): void {
    this.save([]);
  }

  /** What was written on one drink. Null when nothing was. */
  noteOf(productId: string): string | null {
    return this.stored().find((line) => line.productId === productId)?.note ?? null;
  }

  /** How many of one drink are in the order. Zero when it is not. */
  quantityOf(productId: string): number {
    return this.stored().find((line) => line.productId === productId)?.quantity ?? 0;
  }

  private save(lines: CartLine[]): void {
    this.stored.set(lines);

    const venueSlug = this.venue();

    if (venueSlug === null) return;

    this.store.write(CART_STORAGE_PREFIX + venueSlug, JSON.stringify(lines));
  }

  private read(venueSlug: string): CartLine[] {
    const stored = this.store.read(CART_STORAGE_PREFIX + venueSlug);

    if (stored === null) return [];

    try {
      const parsed: unknown = JSON.parse(stored);

      if (!Array.isArray(parsed)) return [];

      return parsed.map(asCartLine).filter((line) => line !== null);
    } catch {
      // Something half-written, or left by an older version of the app.
      // Starting clean beats rendering an order nobody can explain.
      return [];
    }
  }
}

/**
 * One line of an order as read back out of the browser, or null if what was
 * there is not one.
 *
 * What is in storage was written by whatever version of the app this phone
 * opened last, and it outlives the code that wrote it. Parsing is not the same
 * as being an order: a line that lost its price would add up to NaN on screen,
 * and one that lost its id would break the stepper — so those four are
 * demanded. The picture and the note are not: a line written before either
 * existed is still a drink somebody chose, and reading it as "no picture, no
 * note" keeps their order instead of throwing it away over a field that was
 * added later.
 *
 * Lines that do not survive are dropped one by one rather than emptying the
 * whole order, which is the gentler of the two failures for somebody standing
 * at a bar.
 */
function asCartLine(value: unknown): CartLine | null {
  if (typeof value !== 'object' || value === null) return null;

  const line = value as Partial<Record<keyof CartLine, unknown>>;

  if (typeof line.productId !== 'string' || line.productId === '') return null;
  if (typeof line.name !== 'string') return null;
  if (typeof line.unitPrice !== 'number' || !Number.isFinite(line.unitPrice)) return null;
  if (typeof line.quantity !== 'number' || !Number.isInteger(line.quantity)) return null;
  if (line.quantity <= 0) return null;

  return {
    productId: line.productId,
    name: line.name,
    imageUrl: typeof line.imageUrl === 'string' ? line.imageUrl : null,
    unitPrice: line.unitPrice,
    quantity: line.quantity,
    note: typeof line.note === 'string' ? line.note : null,
  };
}
