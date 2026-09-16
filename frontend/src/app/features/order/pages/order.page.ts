import { Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Cart, NOTE_MAX_LENGTH } from '../../../core/cart/cart';
import { formatPrice } from '../../../shared/money/price';

/** One line of the order, in the shape the template draws. */
interface OrderLine {
  readonly id: string;
  readonly name: string;
  readonly note: string;
  readonly quantity: number;
  readonly total: string;
}

/**
 * The order somebody put together, before it is an order on the server: what
 * was added, how many of each, an aclaración per drink, and what it all costs.
 * Everything here comes from the device — this screen asks the API for nothing,
 * so it holds up on the connection of a packed venue.
 */
@Component({
  selector: 'drinkit-order-page',
  imports: [RouterLink],
  styleUrl: './order.page.scss',
  templateUrl: './order.page.html',
})
export class OrderPage {
  protected readonly cart = inject(Cart);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly noteMaxLength = NOTE_MAX_LENGTH;

  /** Back, and "Agregar más tragos": both land on the carta of this venue. */
  protected readonly menuLink = computed(() => ['/', this.venueSlug(), 'menu']);

  protected readonly lines = computed<OrderLine[]>(() =>
    this.cart.lines().map((line) => ({
      id: line.productId,
      name: line.name,
      note: line.note ?? '',
      quantity: line.quantity,
      total: formatPrice(line.quantity * line.unitPrice),
    })),
  );

  /**
   * The same number twice, on purpose: the wireframe shows Subtotal and Total
   * because a service charge or a tip will land between them, and a summary
   * that grows a row later is one somebody has to learn twice.
   */
  protected readonly subtotal = computed(() => formatPrice(this.cart.total()));

  protected readonly total = this.subtotal;

  constructor() {
    // The order belongs to the venue whose address is open. Somebody who lands
    // straight here from a bookmark gets that venue's order and no other.
    effect(() => this.cart.open(this.venueSlug()));
  }

  protected addOne(line: OrderLine): void {
    // Price and name come from the line that is already in the order: this
    // screen never saw the menu, and re-adding must not invent either.
    const existing = this.cart.lines().find((stored) => stored.productId === line.id);

    if (existing === undefined) return;

    this.cart.add({ id: existing.productId, name: existing.name, price: existing.unitPrice });
  }

  protected takeOneOut(line: OrderLine): void {
    this.cart.subtract(line.id);
  }

  protected takeItOut(line: OrderLine): void {
    this.cart.remove(line.id);
  }

  protected noteFor(line: OrderLine, event: Event): void {
    this.cart.setNote(line.id, (event.target as HTMLInputElement).value);
  }
}
