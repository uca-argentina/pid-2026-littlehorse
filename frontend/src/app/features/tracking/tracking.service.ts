import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { anonymously } from '../../core/auth/anonymous-request';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type TrackedOrder = components['schemas']['TrackedOrderResponse'];

/** Where an order is, from the contract: a status spelled wrong here fails to compile. */
export type CustomerOrderStatus = components['schemas']['CustomerOrderStatus'];

/**
 * The address of one order, token included.
 *
 * The token is the customer's only proof that the order is theirs — there is no
 * account to prove it with — so it travels in the path and never in a query
 * string, which is what proxies and analytics keep by default.
 */
export function trackingUrl(venueSlug: string, code: string, token: string): string {
  return `/api/${venueSlug}/orders/${encodeURIComponent(code)}/${encodeURIComponent(token)}`;
}

/** US-23: from the same link, and proven the same way — the token in it. */
export function cancelOrderUrl(venueSlug: string, code: string, token: string): string {
  return `${trackingUrl(venueSlug, code, token)}/cancel`;
}

@Injectable({ providedIn: 'root' })
export class TrackingService {
  private readonly http = inject(HttpClient);

  /** With no Authorization header: this is the customer's screen, not the bar's. */
  follow(venueSlug: string, code: string, token: string) {
    return this.http.get<TrackedOrder>(trackingUrl(venueSlug, code, token), {
      context: anonymously(),
    });
  }

  /** US-23: only while it waits to be paid at the till. */
  cancel(venueSlug: string, code: string, token: string) {
    return this.http.post<void>(
      cancelOrderUrl(venueSlug, code, token),
      {},
      { context: anonymously() },
    );
  }
}
