import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { formatPrice } from '../../../shared/money/price';
import type { CashierOrder } from '../cashier.service';

/**
 * One order, ready to be collected: design/wireframes/CajeroConfirmar.dc.html.
 * Who and what on one side, what to charge and the button on the other. It
 * only shows and asks: the page does the collecting.
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

  readonly collect = output();

  readonly backToScanning = output();

  protected readonly price = formatPrice;
}
