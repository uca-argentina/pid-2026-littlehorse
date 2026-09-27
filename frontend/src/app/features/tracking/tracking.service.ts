import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { anonymously } from '../../core/auth/anonymous-request';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type TrackedOrder = components['schemas']['TrackedOrderResponse'];

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

@Injectable({ providedIn: 'root' })
export class TrackingService {
  private readonly http = inject(HttpClient);

  /** With no Authorization header: this is the customer's screen, not the bar's. */
  follow(venueSlug: string, code: string, token: string) {
    return this.http.get<TrackedOrder>(trackingUrl(venueSlug, code, token), {
      context: anonymously(),
    });
  }
}
