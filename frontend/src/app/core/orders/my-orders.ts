import { Injectable, inject } from '@angular/core';
import { BrowserStore } from '../storage/browser-store';

export const MY_ORDERS_STORAGE_PREFIX = 'drinkit.orders.';

/** A night out, not forever: an order followed longer ago than this is forgotten. */
const KEPT_FOR_MS = 12 * 60 * 60 * 1000;

/** The address of one order the customer followed: enough to go back to it. */
export interface MyOrder {
  readonly code: string;
  readonly token: string;
}

interface StoredOrder extends MyOrder {
  /** When it was last followed, in epoch milliseconds. */
  readonly savedAt: number;
}

/**
 * The orders this phone followed, so the customer can get back to them from
 * the menu after leaving the tracking screen (US-34).
 *
 * Kept per venue, like the cart: another bar's order is not this bar's
 * business. Only the code and the token, never the status: what the order is
 * doing now is asked of the API, and a stored status would already be old.
 */
@Injectable({ providedIn: 'root' })
export class MyOrders {
  private readonly store = inject(BrowserStore);

  /** Newest first, and only the ones still worth going back to. */
  of(venueSlug: string): MyOrder[] {
    return this.read(venueSlug).map(({ code, token }) => ({ code, token }));
  }

  /** Called every time the tracking screen hears from the order: it keeps the order for longer. */
  remember(venueSlug: string, code: string, token: string): void {
    const others = this.read(venueSlug).filter((order) => order.code !== code);

    this.write(venueSlug, [{ code, token, savedAt: Date.now() }, ...others]);
  }

  forget(venueSlug: string, code: string): void {
    this.write(
      venueSlug,
      this.read(venueSlug).filter((order) => order.code !== code),
    );
  }

  private read(venueSlug: string): StoredOrder[] {
    const stored = this.store.read(MY_ORDERS_STORAGE_PREFIX + venueSlug);
    if (stored === null) return [];

    let parsed: unknown;
    try {
      parsed = JSON.parse(stored);
    } catch {
      return [];
    }

    if (!Array.isArray(parsed)) return [];

    const oldestKept = Date.now() - KEPT_FOR_MS;

    return parsed
      .filter(isStoredOrder)
      .filter((order) => order.savedAt >= oldestKept)
      .sort((a, b) => b.savedAt - a.savedAt);
  }

  private write(venueSlug: string, orders: StoredOrder[]): void {
    this.store.write(MY_ORDERS_STORAGE_PREFIX + venueSlug, JSON.stringify(orders));
  }
}

function isStoredOrder(value: unknown): value is StoredOrder {
  if (typeof value !== 'object' || value === null) return false;

  const { code, token, savedAt } = value as Record<string, unknown>;

  return typeof code === 'string' && typeof token === 'string' && typeof savedAt === 'number';
}
