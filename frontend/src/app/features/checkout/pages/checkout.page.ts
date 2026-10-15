import { NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { anonymously } from '../../../core/auth/anonymous-request';
import { Cart } from '../../../core/cart/cart';
import { VipTables, vipTableUrl } from '../../../core/vip-table/vip-tables';
import type { VipTableAccess } from '../../../core/vip-table/vip-tables';
import { problemTypeOf } from '../../../core/api/problem-type-of';
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
  imports: [NgTemplateOutlet, ReactiveFormsModule, RouterLink, GlassMark],
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

  private readonly vipTables = inject(VipTables);

  /** US-41: the VIP table this phone orders for, when a waiter's QR opened one. */
  protected readonly tableCode = computed(() => this.vipTables.codeOf(this.venueSlug()));

  protected readonly table = httpResource<VipTableAccess>(() => {
    const code = this.tableCode();

    return code === null
      ? undefined
      : { url: vipTableUrl(this.venueSlug(), code), context: anonymously() };
  });

  /** Ordering for a table: its balance pays, and there is nothing to choose (criterion 2). */
  protected readonly forTable = computed(() => this.tableCode() !== null);

  /** Criterion 4: the code ran out with its night, or the waiter replaced it. */
  protected readonly tableIsGone = computed(() => {
    const error = this.table.error();

    return error !== undefined && problemTypeOf(error) === ProblemTypes.vipAccountNotFound;
  });

  /** What the balance does not cover, or 0. US-42 will pay it; until then, the order has to fit. */
  private readonly shortBy = computed(() =>
    this.table.hasValue() ? Math.max(0, this.cart.total() - this.table.value().balance) : 0,
  );

  protected readonly shortfall = computed(() =>
    this.shortBy() > 0 ? formatPrice(this.shortBy()) : null,
  );

  protected readonly canPay = computed(
    () =>
      FULL_NAME.test(this.typed().trim()) &&
      (!this.forTable() || (this.table.hasValue() && this.shortBy() === 0)),
  );

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
   * The three of the wireframe. The VIP tables are out of this sprint, so the
   * table balance is drawn switched off rather than hidden: this is the screen
   * somebody learns, and nothing moves under them when it arrives.
   */
  protected readonly waysOfPaying = computed<readonly WayOfPaying[]>(() =>
    this.forTable()
      ? [
          {
            method: 'VipBalance',
            name: 'Saldo de la mesa',
            what: this.table.hasValue()
              ? `Disponible $ ${formatPrice(this.table.value().balance)}`
              : 'Buscando el saldo…',
            isBuilt: this.table.hasValue(),
          },
        ]
      : [
          {
            method: 'Digital',
            name: 'Pago digital',
            what: 'Tarjeta o Mercado Pago',
            isBuilt: true,
          },
          {
            method: 'Cash',
            name: 'Efectivo en caja',
            what: 'Te damos un código y pagás en la caja',
            isBuilt: true,
          },
          {
            method: 'VipBalance',
            name: 'Saldo de la mesa',
            what: 'Sólo con el QR de una mesa VIP',
            isBuilt: false,
          },
        ],
  );

  private readonly chosen = signal<PaymentMethod>('Digital');

  /** With a table there is only one way, whatever was tapped before the QR. */
  protected readonly method = computed<PaymentMethod>(() =>
    this.forTable() ? 'VipBalance' : this.chosen(),
  );

  /** Cash is not paid here: the order is confirmed and the money changes hands at the till. */
  protected readonly paysAtTheTill = computed(() => this.method() === 'Cash');

  constructor() {
    effect(() => this.cart.open(this.venueSlug()));

    // Criterion 3: another phone of the table spent first. The balance on
    // screen is old, so it is asked for again.
    effect(() => {
      if (this.store.whatWentWrong()?.type === ProblemTypes.vipAccountBalanceShort)
        this.table.reload();
    });
  }

  protected choose(method: PaymentMethod): void {
    this.chosen.set(method);
  }

  /** Back to ordering for the bar, like anybody without a table. */
  protected orderForTheBar(): void {
    this.vipTables.forget(this.venueSlug());
  }

  protected pay(): void {
    if (!this.canPay()) return;

    this.store.pay(this.venueSlug(), this.typed().trim(), this.method(), this.tableCode());
  }
}
