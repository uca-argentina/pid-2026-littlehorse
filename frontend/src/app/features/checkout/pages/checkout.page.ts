import { Component, computed, effect, inject, input } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Cart } from '../../../core/cart/cart';
import { formatPrice } from '../../../shared/money/price';
import { GlassMark } from '../../../shared/glass-mark/glass-mark';
import { CheckoutStore } from '../checkout.store';
import type { PaymentMethod } from '../checkout.service';

/** One line of what is being paid for. */
interface PayingFor {
  readonly id: string;
  readonly what: string;
  readonly note: string | null;
  readonly total: string;
}

/** A way of paying, as the screen draws it. */
interface WayOfPaying {
  readonly method: PaymentMethod;
  readonly name: string;
  readonly what: string;
  readonly isBuilt: boolean;
}

/**
 * At least two words of letters.
 *
 * The server has the same rule and is the one that enforces it; this is here
 * so that "Euge" is answered before two seconds of "procesando el pago" and a
 * round trip, which is the worst possible moment to be told to try again.
 */
const FULL_NAME = /^\p{L}+(?:\s+\p{L}+)+$/u;

/**
 * Paying for the order. The last screen of the customer's night that asks for
 * anything: a name, and which way they are paying.
 */
@Component({
  selector: 'drinkit-checkout-page',
  imports: [ReactiveFormsModule, RouterLink, GlassMark],
  providers: [CheckoutStore],
  styleUrl: './checkout.page.scss',
  templateUrl: './checkout.page.html',
})
export class CheckoutPage {
  protected readonly cart = inject(Cart);

  protected readonly store = inject(CheckoutStore);

  readonly venueSlug = input.required<string>();

  protected readonly orderLink = computed(() => ['/', this.venueSlug(), 'order']);

  protected readonly menuLink = computed(() => ['/', this.venueSlug(), 'menu']);

  protected readonly customerName = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(FULL_NAME)],
  });

  // valueChanges and not statusChanges: a repeated 'INVALID' never notifies,
  // so the button would stay as it was after the second wrong keystroke.
  private readonly typed = toSignal(this.customerName.valueChanges, {
    initialValue: this.customerName.value,
  });

  protected readonly canPay = computed(() => FULL_NAME.test(this.typed().trim()));

  protected readonly lines = computed<PayingFor[]>(() =>
    this.cart.lines().map((line) => ({
      id: line.productId,
      what: `${line.quantity}× ${line.name}`,
      note: line.note,
      total: formatPrice(line.quantity * line.unitPrice),
    })),
  );

  protected readonly total = computed(() => formatPrice(this.cart.total()));

  /**
   * The three of the wireframe. Only one is built — the brief leaves the till
   * and the VIP tables out — and the other two are drawn switched off rather
   * than hidden, so this is the screen somebody learns and nothing moves under
   * them when they arrive.
   */
  protected readonly waysOfPaying: readonly WayOfPaying[] = [
    { method: 'Digital', name: 'Pago digital', what: 'Tarjeta o Mercado Pago', isBuilt: true },
    { method: 'Cash', name: 'Efectivo en caja', what: 'Próximamente', isBuilt: false },
    { method: 'VipBalance', name: 'Saldo de la mesa', what: 'Próximamente', isBuilt: false },
  ];

  constructor() {
    effect(() => this.cart.open(this.venueSlug()));
  }

  protected pay(): void {
    if (!this.canPay()) return;

    this.store.pay(this.venueSlug(), this.typed().trim(), 'Digital');
  }
}
