import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { forkJoin, timer } from 'rxjs';
import { Cart } from '../../core/cart/cart';
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

@Injectable()
export class CheckoutStore {
  private readonly checkout = inject(CheckoutService);

  private readonly cart = inject(Cart);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly pause = inject(PROCESSING_PAUSE_MS);

  private readonly state = signal<CheckoutStatus>('idle');

  private readonly problem = signal('');

  readonly status = this.state.asReadonly();

  /** What the API said went wrong, in the words it used. Empty while nothing has. */
  readonly whatWentWrong = this.problem.asReadonly();

  readonly isPaying = computed(() => this.state() === 'paying');

  /**
   * Generated once, when the screen is built, and sent with every attempt: the
   * signal in a venue is bad and browsers retry on their own. The same key
   * coming back twice has to mean one order, which is criterion 6.
   */
  private readonly idempotencyKey = crypto.randomUUID();

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
        idempotencyKey: this.idempotencyKey,
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
   */
  private showTheConfirmation(venueSlug: string, order: ConfirmedOrder): void {
    this.cart.clear();
    this.state.set('idle');

    void this.router.navigate(['/', venueSlug, 'orders', order.code]);
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
