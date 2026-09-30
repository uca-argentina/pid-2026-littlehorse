import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { anonymously } from '../../core/auth/anonymous-request';
import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type PaymentReturn = components['schemas']['PaymentReturnResponse'];

/** The order's own address, token included, plus where its payment is reported. */
export function paymentReturnUrl(venueSlug: string, code: string, token: string): string {
  return `/api/${venueSlug}/orders/${encodeURIComponent(code)}/${encodeURIComponent(token)}/payment`;
}

/** US-24: the customer is back from Mercado Pago's page. */
@Injectable({ providedIn: 'root' })
export class PaymentReturnService {
  private readonly http = inject(HttpClient);

  /** With no Authorization header: this is the customer's screen, not the bar's. */
  report(venueSlug: string, code: string, token: string, paymentId: string | null) {
    return this.http.post<PaymentReturn>(
      paymentReturnUrl(venueSlug, code, token),
      { paymentId },
      { context: anonymously() },
    );
  }
}
