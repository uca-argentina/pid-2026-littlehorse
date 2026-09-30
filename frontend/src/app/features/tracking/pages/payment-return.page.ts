import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { Cart } from '../../../core/cart/cart';
import { PaymentReturnService } from '../payment-return.service';

/** What the screen is showing. Paid or pending leaves it at once, for the order. */
type ReturnStatus = 'checking' | 'canceled' | 'paidTooLate' | 'nowhere' | 'unreachable';

/**
 * US-24: where Mercado Pago's page sends the customer back, whatever happened.
 * It asks the API — which asks Mercado Pago, never the address — and moves on:
 * paid or pending to the order, canceled to a way of trying again.
 */
@Component({
  selector: 'drinkit-payment-return-page',
  imports: [RouterLink],
  styleUrl: './tracking.page.scss',
  templateUrl: './payment-return.page.html',
})
export class PaymentReturnPage {
  private readonly payments = inject(PaymentReturnService);

  private readonly cart = inject(Cart);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  readonly venueSlug = input.required<string>();

  readonly code = input.required<string>();

  readonly token = input.required<string>();

  /**
   * Mercado Pago's payment_id, handed over by the route; the word "null" when
   * no payment was made.
   */
  readonly paymentId = input<string | null>(null);

  protected readonly status = signal<ReturnStatus>('checking');

  protected readonly checkoutLink = computed(() => ['/', this.venueSlug(), 'checkout']);

  protected readonly menuLink = computed(() => ['/', this.venueSlug(), 'menu']);

  constructor() {
    effect(() => {
      const venueSlug = this.venueSlug();

      untracked(() => {
        this.cart.open(venueSlug);
        this.report();
      });
    });
  }

  protected report(): void {
    this.status.set('checking');

    const paymentId = this.paymentId();

    this.payments
      .report(
        this.venueSlug(),
        this.code(),
        this.token(),
        paymentId && paymentId !== 'null' ? paymentId : null,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ status }) => {
          // Canceled: nothing was charged, and the drinks stay for another go.
          if (status === 'Canceled') return this.status.set('canceled');

          this.cart.clear();
          void this.router.navigate(['/', this.venueSlug(), 'orders', this.code(), this.token()], {
            replaceUrl: true,
          });
        },
        error: (error: unknown) => this.status.set(statusFor(error)),
      });
  }
}

function statusFor(error: unknown): ReturnStatus {
  if (!(error instanceof HttpErrorResponse) || error.status === 0) return 'unreachable';
  if (problemTypeOf(error) === ProblemTypes.paymentPaidAfterCancel) return 'paidTooLate';
  if (error.status === 404) return 'nowhere';

  return 'unreachable';
}
