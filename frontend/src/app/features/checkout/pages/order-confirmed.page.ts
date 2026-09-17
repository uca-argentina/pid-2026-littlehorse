import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * The order went through, and this is what to say at the bar.
 * </summary>
 * Everything it shows comes from the address, on purpose: somebody locks their
 * phone, comes back and reloads, and the code is still there. Reading it from
 * whatever the previous screen handed over would leave a blank page instead.
 */
@Component({
  selector: 'drinkit-order-confirmed-page',
  imports: [RouterLink],
  styleUrl: './order-confirmed.page.scss',
  templateUrl: './order-confirmed.page.html',
})
export class OrderConfirmedPage {
  readonly venueSlug = input.required<string>();

  /** The short code, from the path: "K-4821". */
  readonly code = input.required<string>();

  protected readonly menuLink = computed(() => ['/', this.venueSlug(), 'menu']);
}
