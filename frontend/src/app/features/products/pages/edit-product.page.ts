import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { EditProductStore } from '../edit-product.store';
import { ProductForm } from '../product-form/product-form';
import type { ProductFormValue } from '../product-form/product-form';
import { PRODUCTS_URL } from '../products.service';
import type { Product } from '../products.service';

@Component({
  selector: 'drinkit-edit-product-page',
  imports: [AdminHeader, ProductForm, RouterLink],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a stale message from one
  // product's screen to the next one opened.
  providers: [EditProductStore],
  styleUrl: './edit-product.page.scss',
  templateUrl: './edit-product.page.html',
})
export class EditProductPage {
  protected readonly store = inject(EditProductStore);

  /** Both from the path, bound by the router. */
  readonly venueSlug = input.required<string>();

  readonly id = input.required<string>();

  /**
   * The venue's whole menu, filtered here to the one being corrected. There is
   * no endpoint that serves a single product on its own, and adding one to
   * save a listing that already fits in one response would be a request
   * nobody needs — the same trade the search on the listing makes.
   */
  protected readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  /** What the API last said about it: the listing at first, then each answer. */
  protected readonly product = computed<Product | undefined>(
    () =>
      this.store.updated() ??
      (this.products.hasValue() ? this.products.value() : []).find((row) => row.id === this.id()),
  );

  /** Loaded, and nothing here has that id. Not the same as still loading. */
  protected readonly isMissing = computed(
    () => this.products.hasValue() && this.product() === undefined,
  );

  protected save({
    name,
    description,
    categoryId,
    price,
    stock,
    photo,
    isAvailable,
  }: ProductFormValue): void {
    const product = this.product();

    if (product === undefined) return;

    this.store.save(this.venueSlug(), this.id(), {
      correction: { name, description, price, categoryId },
      photo,
      stockChange: stock,
      isAvailable: isAvailable === product.isAvailable ? null : isAvailable,
    });
  }

  /** Nothing on this screen brings a product back, so the first tap only asks. */
  protected readonly isConfirmingDeactivation = signal(false);

  protected askToDeactivate(): void {
    this.isConfirmingDeactivation.set(true);
  }

  protected cancelDeactivation(): void {
    this.isConfirmingDeactivation.set(false);
  }

  protected deactivate(): void {
    this.isConfirmingDeactivation.set(false);
    this.store.deactivate(this.id());
  }
}
