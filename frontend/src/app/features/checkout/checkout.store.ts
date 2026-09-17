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
 * What the screen is doing. One value rather than a set of booleans, so
 * "paying" and "that drink ran out" cannot both be true at once.
 */
export type CheckoutStatus = 'idle' | 'paying' | 'soldOut' | 'rejected' | 'unreachable';

export const CHECKOUT_KEY_PREFIX = 'drinkit.checkout-key.';

@Injectable()
export class CheckoutStore {
  private readonly checkout = inject(CheckoutService);

  private readonly cart = inject(Cart);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly pause = inject(PROCESSING_PAUSE_MS);

  private readonly store = inject(BrowserStore);

  private readonly state = signal<CheckoutStatus>('idle');

  private readonly problem = signal('');

  readonly status = this.state.asReadonly();

  /** What the API said went wrong, in the words it used. Empty while nothing has. */
  readonly whatWentWrong = this.problem.asReadonly();

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
    this.problem.set('');

    forkJoin({
      // Both, so the wait is the longer of the two rather than one after the
      // other: a request that takes three seconds is not made to take five.
      shown: timer(this.pause),
      order: this.checkout.confirm(venueSlug, {
        customerName,
        method,
        idempotencyKey: this.keyFor(venueSlug),
        lines: this.cart.lines().map((line) => ({
          productId: line.productId,
          quantity: line.quantity,
          note: line.note,
        })),
      }),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ order }) => this.showTheConfirmation(venueSlug, order),
        error: (error: unknown) => this.explain(error),
      });
  }

  /**
   * The order is the server's now and has a code of its own, so what is left on
   * this phone is a copy that nobody should be able to pay for twice.
   *
   * Emptied after the confirmation is on screen and not before: this screen is
   * still the one showing while the next one's code downloads, and an empty
   * cart turns it into "no hay nada para pagar" — announced out loud — in the
   * second after a payment went through.
   */
  private showTheConfirmation(venueSlug: string, order: ConfirmedOrder): void {
    this.state.set('idle');

    void this.router.navigate(['/', venueSlug, 'orders', order.code]).then(() => {
      this.cart.clear();
      this.store.write(CHECKOUT_KEY_PREFIX + venueSlug, '');
    });
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
      this.problem.set(detailOf(error));
      this.state.set('soldOut');

      return;
    }

    // An answer that carries no problem document at all — a proxy, a gateway.
    // Nothing of ours refused this, so it is not something to explain.
    if (problem === undefined) {
      this.state.set('unreachable');

      return;
    }

    this.problem.set(detailOf(error));
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

/**
 * The sentence the API wrote for this exact failure — which drink ran out, what
 * is wrong with the name. Repeating those rules here would be a second copy
 * that drifts from the one the server enforces.
 */
function detailOf(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return '';

  const body: unknown = error.error;

  return typeof body === 'object' && body !== null && 'detail' in body
    ? String((body as { detail: unknown }).detail)
    : '';
}
