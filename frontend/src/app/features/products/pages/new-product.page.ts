import { Component, inject, input } from '@angular/core';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { NewProductStore } from '../new-product.store';
import { ProductForm } from '../product-form/product-form';
import type { ProductFormValue } from '../product-form/product-form';

@Component({
  selector: 'drinkit-new-product-page',
  imports: [AdminHeader, ProductForm],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a rejected attempt into
  // the next visit. A component's providers die with the component.
  providers: [NewProductStore],
  styleUrl: './new-product.page.scss',
  templateUrl: './new-product.page.html',
})
export class NewProductPage {
  protected readonly store = inject(NewProductStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /**
   * The availability switch is not sent: a new product always starts
   * available, and the API has no say in it at creation.
   */
  protected create({ name, description, categoryId, price, stock, photo }: ProductFormValue): void {
    this.store.submit(
      this.venueSlug(),
      // The picture goes up on its own request once the product exists; the
      // store handles the second step.
      { name, description, imageUrl: null, categoryId, price, stock },
      photo,
    );
  }
}
