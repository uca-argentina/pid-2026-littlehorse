import { NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { Cart } from '../../../core/cart/cart';
import { formatPrice } from '../../../shared/money/price';
import { GlassMark } from '../../../shared/glass-mark/glass-mark';
import { anonymously } from '../../../core/auth/anonymous-request';
import { CheckoutStore } from '../checkout.store';
import { paymentConfigurationUrl } from '../checkout.service';
import type { PaymentConfiguration, PaymentMethod } from '../checkout.service';
import { MercadoPagoWallet } from '../components/mercado-pago-wallet';

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
  imports: [NgTemplateOutlet, ReactiveFormsModule, RouterLink, GlassMark, MercadoPagoWallet],
  providers: [CheckoutStore],
  styleUrl: './checkout.page.scss',
  templateUrl: './checkout.page.html',
})
export class CheckoutPage {
  protected readonly cart = inject(Cart);

  protected readonly store = inject(CheckoutStore);

  /** For the template to tell one refusal from another. */
  protected readonly problems = ProblemTypes;

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

  /** Whether Mercado Pago's own button can be drawn here: its public key, when configured. */
  private readonly paymentConfiguration = httpResource<PaymentConfiguration>(() => ({
    url: paymentConfigurationUrl,
    context: anonymously(),
  }));

  /** The script could not draw it, or it broke: this screen's own button takes over. */
  protected readonly walletFailed = signal(false);

  /**
   * The public key to draw Mercado Pago's own button with (US-24, /mp-review
   * practice 7), or null to use this screen's own: none configured, not known
   * yet, or the button failed. Only once the name is complete — the official
   * button cannot be switched off, and nobody should tap one that will fail.
   * Once tapped it stays, whatever the name becomes: it is what takes them to
   * pay the order it has just confirmed.
   */
  protected readonly walletKey = computed(() => {
    const publicKey = this.paymentConfiguration.hasValue()
      ? this.paymentConfiguration.value().publicKey
      : null;
    // Cash is paid at the till, not on Mercado Pago's page.
    const wanted = !this.paysAtTheTill() && (this.canPay() || this.store.isPaying());

    return publicKey !== null && !this.walletFailed() && wanted ? publicKey : null;
  });

  /** What Mercado Pago's button does when tapped: confirm, and answer the checkout to open. */
  protected readonly payWithWallet = (): Promise<string> =>
    this.store.payWithWallet(this.venueSlug(), this.typed().trim());

  /**
   * The three of the wireframe. The VIP tables are out of this sprint, so the
   * table balance is drawn switched off rather than hidden: this is the screen
   * somebody learns, and nothing moves under them when it arrives.
   */
  protected readonly waysOfPaying: readonly WayOfPaying[] = [
    { method: 'Digital', name: 'Pago digital', what: 'Tarjeta o Mercado Pago', isBuilt: true },
    {
      method: 'Cash',
      name: 'Efectivo en caja',
      what: 'Te damos un código y pagás en la caja',
      isBuilt: true,
    },
    { method: 'VipBalance', name: 'Saldo de la mesa', what: 'Próximamente', isBuilt: false },
  ];

  protected readonly method = signal<PaymentMethod>('Digital');

  /** Cash is not paid here: the order is confirmed and the money changes hands at the till. */
  protected readonly paysAtTheTill = computed(() => this.method() === 'Cash');

  constructor() {
    effect(() => this.cart.open(this.venueSlug()));
  }

  protected pay(): void {
    if (!this.canPay()) return;

    this.store.pay(this.venueSlug(), this.typed().trim(), this.method());
  }
}
