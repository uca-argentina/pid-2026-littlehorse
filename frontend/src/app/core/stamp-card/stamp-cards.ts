import { httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { anonymously } from '../auth/anonymous-request';
import type { components } from '../api/schema';
import { BrowserStore } from '../storage/browser-store';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type StampCardView = components['schemas']['StampCardResponse'];

/**
 * Where the phone keeps its card, one per venue (US-58): the card is the
 * venue's, and the same phone at another bar holds another one.
 */
export const STAMP_CARD_KEY_PREFIX = 'drinkit.stamp-card.';

/**
 * The token travels in this header and never in the address: query strings are
 * what proxies and logs keep, and the token is the whole identity of the card.
 */
export const STAMP_CARD_HEADER = 'X-Stamp-Card';

export function stampCardUrl(venueSlug: string): string {
  return `/api/${venueSlug}/stamp-card`;
}

/**
 * The customer's stamp card, shared by the menu, the checkout and the tracking
 * screen: one request, one answer, and the one place that knows the token.
 * Without an account, the token in localStorage is all there is of "who": delete
 * the data or change phone and the stamps are gone, which was decided on
 * 2026-11-04 when choosing not to have accounts.
 */
@Injectable({ providedIn: 'root' })
export class StampCards {
  private readonly store = inject(BrowserStore);

  private readonly venue = signal<string | null>(null);

  private readonly token = signal<string | null>(null);

  private readonly card = httpResource<StampCardView>(() => {
    const venueSlug = this.venue();

    if (venueSlug === null) return undefined;

    const token = this.token();
    const headers: Record<string, string> = token === null ? {} : { [STAMP_CARD_HEADER]: token };

    return {
      url: stampCardUrl(venueSlug),
      headers,
      // No Authorization header, ever: this is the customer's door.
      context: anonymously(),
    };
  });

  /** The card to draw, or null while it is unknown and when the venue has it switched off. */
  readonly view = computed<StampCardView | null>(() => {
    if (!this.card.hasValue()) return null;

    const answer = this.card.value();

    return answer.isActive ? answer : null;
  });

  /** Called by a venue's screen when it opens. Loads that venue's card. */
  open(venueSlug: string): void {
    if (this.venue() === venueSlug) return;

    this.venue.set(venueSlug);
    this.token.set(this.tokenOf(venueSlug));
  }

  tokenOf(venueSlug: string): string | null {
    const kept = this.store.read(STAMP_CARD_KEY_PREFIX + venueSlug);

    return kept === null || kept === '' ? null : kept;
  }

  /** The first order hands the token over, and the next ones bring it back. */
  remember(venueSlug: string, token: string): void {
    this.store.write(STAMP_CARD_KEY_PREFIX + venueSlug, token);

    if (this.venue() === venueSlug) this.token.set(token);
  }

  /** After something moved the stamps: a delivery, a reward, another phone. */
  refresh(): void {
    this.card.reload();
  }
}
