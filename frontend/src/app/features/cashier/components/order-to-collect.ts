import { DatePipe } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { formatPrice } from '../../../shared/money/price';
import type { CashierOrder } from '../cashier.service';

/**
 * One order, ready to be collected: design/wireframes/CajeroConfirmar.dc.html.
 * Who and what on one side, what to charge and the button on the other. It
 * only shows and asks: the page does the collecting, and the canceling.
 */
@Component({
  selector: 'drinkit-order-to-collect',
  imports: [DatePipe],
  styleUrl: './order-to-collect.scss',
  templateUrl: './order-to-collect.html',
})
export class OrderToCollect {
  readonly order = input.required<CashierOrder>();

  /** While the collection is on its way: the button says so and takes no second tap. */
  readonly collecting = input(false);

  /** While the cancellation is on its way (US-23). */
  readonly canceling = input(false);

  readonly collect = output();

  readonly backToScanning = output();

  /** US-23: the customer left without paying, confirmed. */
  readonly cancelOrder = output();

  /** "Cancelar pedido" was tapped once: it asks before doing it. */
  protected readonly confirmingCancel = signal(false);

  /** Something is on its way: no second tap on anything. */
  protected readonly busy = computed(() => this.collecting() || this.canceling());

  protected readonly price = formatPrice;
}
