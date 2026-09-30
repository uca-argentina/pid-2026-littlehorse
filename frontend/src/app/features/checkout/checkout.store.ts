import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { forkJoin, timer } from 'rxjs';
import { Cart } from '../../core/cart/cart';
import { BrowserStore } from '../../core/storage/browser-store';
import { ProblemTypes } from '../../core/api/problem-types';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { CheckoutService } from './checkout.service';
import type { ConfirmedOrder, PaymentMethod } from './checkout.service';

/**
 * How long "procesando el pago" stays on screen at the very least.
 * </summary>
 * There is no gateway to wait for — the payment is simulated, as the brief
 * allows — so without this the screen would blink from a tap straight to a
 * code, which reads as nothing having happened. It is a floor and not a sleep:
 * a slow request takes as long as it takes.
 */
export const PROCESSING_PAUSE_MS = new InjectionToken<number>('PROCESSING_PAUSE_MS', {
  providedIn: 'root',
  factory: () => 2000,
});

/**
 * How the page leaves the app for the payment gateway's page (US-24). A token
 * so a test can see where it was sent without the browser actually going.
 */
export const LEAVE_FOR = new InjectionToken<(url: string) => void>('LEAVE_FOR', {
  providedIn: 'root',
  factory: () => (url: string) => window.location.assign(url),
});

/**
 * What the screen is doing. One value rather than a set of booleans, so
 * "paying" and "that drink ran out" cannot both be true at once.
 */
export type CheckoutStatus =
  'idle' | 'paying' | 'soldOut' | 'rejected' | 'unreachable' | 'gatewayUnavailable';

export const CHECKOUT_KEY_PREFIX = 'drinkit.checkout-key.';

@Injectable()
export class CheckoutStore {
  private readonly checkout = inject(CheckoutService);

  private readonly cart = inject(Cart);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly pause = inject(PROCESSING_PAUSE_MS);

  private readonly store = inject(BrowserStore);

  private readonly leaveFor = inject(LEAVE_FOR);

  private readonly state = signal<CheckoutStatus>('idle');

  private readonly refusal = signal<Refusal | null>(null);

  readonly status = this.state.asReadonly();

  /**
   * What went wrong, as data: the screen says it in Spanish. The API's own
   * detail is English and written for developers, so it is never shown.
   */
  readonly whatWentWrong = this.refusal.asReadonly();

  readonly isPaying = computed(() => this.state() === 'paying');

  /**
   * The key that tells one attempt at this order from a different order.
   *
   * Kept in the browser and not in this object, because the attempt that
   * matters most is the one nobody planned: the answer is lost on the way back,
   * and the customer does what anybody does on a bad signal — reloads and pays
   * again. A key that died with the screen would make that a second paid order,
   * which is the one thing this screen promises will not happen. Let go of in
   * showTheConfirmation, so the next round of the night is a new order.
   */
  private keyFor(venueSlug: string): string {
    const where = CHECKOUT_KEY_PREFIX + venueSlug;
    const kept = this.store.read(where);

    if (kept !== null && kept !== '') return kept;

    const fresh = crypto.randomUUID();

    this.store.write(where, fresh);

    return fresh;
  }

  pay(venueSlug: string, customerName: string, method: PaymentMethod): void {
    if (this.isPaying()) return;

    this.state.set('paying');
    this.refusal.set(null);

    forkJoin({
      // Both, so the wait is the longer of the two rather than one after the
      // other: a request that takes three seconds is not made to take five.
      shown: timer(this.pause),
      order: this.checkout.confirm(venueSlug, this.orderFor(venueSlug, customerName, method)),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ order }) =>
          order.paymentUrl
            ? this.sendToPay(venueSlug, order.paymentUrl)
            : this.showTheConfirmation(venueSlug, order),
        error: (error: unknown) => this.explain(error),
      });
  }

  /**
   * The order is the server's now and has a code of its own, so what is left on
   * this phone is a copy that nobody should be able to pay for twice.
   *
   * The token goes in the address and nowhere else: it is the only thing that
   * opens that order, and the one and only time the API hands it over is the
   * answer that just arrived.
   *
   * Emptied after the next screen is on and not before: this one is still
   * showing while that screen's code downloads, and an empty cart turns it into
   * "no hay nada para pagar" — announced out loud — in the second after a
   * payment went through.
   */
  private showTheConfirmation(venueSlug: string, order: ConfirmedOrder): void {
    this.state.set('idle');

    void this.router
      .navigate(['/', venueSlug, 'orders', order.code, order.trackingToken])
      .then(() => {
        this.cart.clear();
        this.store.write(CHECKOUT_KEY_PREFIX + venueSlug, '');
      });
  }

  /**
   * Mercado Pago's own button (Wallet Brick, US-24) calls this when it is
   * tapped: the order is confirmed and the button gets the checkout's id to
   * open. It redirects by itself, so nothing here leaves the page. No pause
   * either: the button shows its own "procesando".
   *
   * Rejected when the order is refused, so the button stops; what went wrong
   * is on the screen, in Spanish, as with any other payment.
   */
  payWithWallet(venueSlug: string, customerName: string): Promise<string> {
    this.state.set('paying');
    this.refusal.set(null);

    return new Promise<string>((resolve, reject) => {
      this.checkout
        .confirm(venueSlug, this.orderFor(venueSlug, customerName, 'Digital'))
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (order) => {
            // Paid on the spot after all — nothing to open on Mercado Pago.
            if (!order.paymentCheckoutId) {
              this.showTheConfirmation(venueSlug, order);
              reject(new Error('The order needs no checkout.'));

              return;
            }

            this.store.write(CHECKOUT_KEY_PREFIX + venueSlug, '');
            resolve(order.paymentCheckoutId);
          },
          error: (error: unknown) => {
            this.explain(error);
            reject(error);
          },
        });
    });
  }

  /** The order as the API takes it: the drinks and how many, never what they cost. */
  private orderFor(venueSlug: string, customerName: string, method: PaymentMethod) {
    return {
      customerName,
      method,
      idempotencyKey: this.keyFor(venueSlug),
      lines: this.cart.lines().map((line) => ({
        productId: line.productId,
        quantity: line.quantity,
        note: line.note,
      })),
    };
  }

  /**
   * Paid on Mercado Pago's page (US-24): the order exists and waits, so the key
   * that named this attempt is let go of. The cart stays: if the payment does
   * not go through, the order is canceled and the customer comes back to try
   * again with the same drinks.
   */
  private sendToPay(venueSlug: string, paymentUrl: string): void {
    this.store.write(CHECKOUT_KEY_PREFIX + venueSlug, '');
    this.leaveFor(paymentUrl);
  }

  private explain(error: unknown): void {
    // Status 0 is what a request that never got an answer looks like: the
    // venue's wifi, a phone that walked out of range. There is no document to
    // read and nothing our rules can explain.
    if (!(error instanceof HttpErrorResponse) || error.status === 0) {
      this.state.set('unreachable');

      return;
    }

    const problem = problemTypeOf(error);

    // The venue's menu moved under the order: a drink ran out, left the menu,
    // or somebody else took the last one. The screen sends them back to fix it.
    if (theMenuMoved(problem)) {
      this.refusal.set(refusalOf(error, problem));
      this.state.set('soldOut');

      return;
    }

    // Mercado Pago did not open a checkout. The order was canceled and its
    // drinks went back: trying again in a moment is the whole answer.
    if (problem === ProblemTypes.paymentGatewayUnavailable) {
      this.state.set('gatewayUnavailable');

      return;
    }

    // An answer that carries no problem document at all — a proxy, a gateway.
    // Nothing of ours refused this, so it is not something to explain.
    if (problem === undefined) {
      this.state.set('unreachable');

      return;
    }

    this.refusal.set(refusalOf(error, problem));
    this.state.set('rejected');
  }
}

/** The three that mean the order has to be looked at again, not rewritten. */
function theMenuMoved(problem: string | undefined): boolean {
  return (
    problem === ProblemTypes.orderSoldOut ||
    problem === ProblemTypes.orderNotOnTheMenu ||
    problem === ProblemTypes.orderStockMoved
  );
}

/** Why the order was refused: which rule, and which drink when it was one. */
export interface Refusal {
  readonly type: string | undefined;
  readonly productName: string | null;
}

/**
 * The problem's type, and the drink it names when the menu moved underneath.
 * Never its detail: that sentence is the API's, in English, for developers.
 */
function refusalOf(error: HttpErrorResponse, type: string | undefined): Refusal {
  const body: unknown = error.error;
  const productName =
    typeof body === 'object' && body !== null && 'productName' in body
      ? String((body as { productName: unknown }).productName)
      : null;

  return { type, productName };
}
