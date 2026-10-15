import { Injectable, inject, signal } from '@angular/core';
import type { components } from '../api/schema';
import { BrowserStore } from '../storage/browser-store';

/** What a table's QR opens: its number and what it has left to spend (US-41). */
export type VipTableAccess = components['schemas']['VipTableAccessResponse'];

export const VIP_TABLE_KEY_PREFIX = 'drinkit.vip-table.';

/** Where the menu and the checkout ask which table a code opens tonight. */
export function vipTableUrl(venueSlug: string, code: string): string {
  // Encoded: the code comes from a QR anybody can tamper with, and must stay
  // one segment of the path whatever it says.
  return `/api/${venueSlug}/vip-accounts/${encodeURIComponent(code)}`;
}

/**
 * The VIP table this phone orders for, per venue (US-41). The code comes from
 * the QR the waiter handed over; it is remembered on the device so the menu,
 * the checkout and a reload all keep ordering for that table. The API decides
 * whether it still opens anything: a code from last Saturday is just a code.
 */
@Injectable({ providedIn: 'root' })
export class VipTables {
  private readonly store = inject(BrowserStore);

  /** Bumped on every change, so whoever reads a code hears about it. */
  private readonly changes = signal(0);

  /**
   * The code this phone holds for the venue, or null. Read inside a computed
   * or a resource, it is read again whenever a code is remembered or forgotten.
   */
  codeOf(venueSlug: string): string | null {
    this.changes();

    const code = this.store.read(VIP_TABLE_KEY_PREFIX + venueSlug);

    return code === null || code === '' ? null : code;
  }

  remember(venueSlug: string, code: string): void {
    this.store.write(VIP_TABLE_KEY_PREFIX + venueSlug, code);
    this.changes.update((count) => count + 1);
  }

  /** Back to ordering for the bar, like anybody without a table. */
  forget(venueSlug: string): void {
    this.store.write(VIP_TABLE_KEY_PREFIX + venueSlug, '');
    this.changes.update((count) => count + 1);
  }
}
