import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { anonymously } from '../../core/auth/anonymous-request';
import type { components } from '../../core/api/schema';
import { menuUrl } from '../menu/menu.service';
import type { Menu } from '../menu/menu.service';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type ConfirmOrderRequest = components['schemas']['ConfirmOrderRequest'];

export type ConfirmedOrder = components['schemas']['ConfirmedOrderResponse'];

/** The ways of paying, by the names the API answers to. */
export type PaymentMethod = components['schemas']['PaymentMethodName'];

/**
 * The venue is in the path because this is the customer's door and there is no
 * token to carry one. It comes from the QR they scanned.
 */
export function ordersUrl(venueSlug: string): string {
  return `/api/${venueSlug}/orders`;
}

@Injectable({ providedIn: 'root' })
export class CheckoutService {
  private readonly http = inject(HttpClient);

  /**
   * With no Authorization header, ever. A staff session left behind on this
   * phone would make the API price the order against that venue's menu.
   */
  /** The menu as it is now, to take the current prices when the one the cart held moved (US-55). */
  menu(venueSlug: string) {
    return this.http.get<Menu>(menuUrl(venueSlug), { context: anonymously() });
  }

  confirm(venueSlug: string, order: ConfirmOrderRequest) {
    return this.http.post<ConfirmedOrder>(ordersUrl(venueSlug), order, { context: anonymously() });
  }
}
