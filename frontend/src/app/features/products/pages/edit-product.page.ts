import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { slowLoading } from '../../../shared/loading/slow-loading';
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

  protected readonly isSlow = slowLoading(() => this.products.isLoading());

  /** The fields on the left of the form, one outline each. */
  protected readonly skeletonFields = [1, 2, 3, 4];

  /** What the API last said about it: the listing at first, then each answer. */
  protected readonly product = computed<Product | undefined>(
    () =>
      this.store.updated() ??
      (this.products.hasValue() ? this.products.value() : []).find((row) => row.id === this.id()),
  );

  /**
   * Same split as the listing: a dropped connection goes away on a retry, a
   * role taken away never will, so only the first one offers it.
   */
  protected readonly failure = computed<'none' | 'forbidden' | 'unreachable'>(() => {
    const error = this.products.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  protected retry(): void {
    this.products.reload();
  }

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
