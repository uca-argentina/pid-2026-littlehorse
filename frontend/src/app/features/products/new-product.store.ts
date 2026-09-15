import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ProblemTypes } from '../../core/api/problem-types';
import { ProductsService } from './products.service';
import type { NewProduct } from './products.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans,
 * so "sending" and "that name is taken" cannot both be true at once.
 */
type NewProductStatus = 'idle' | 'sending' | 'nameTaken' | 'unreachable';

/**
 * State and transitions of the "new product" screen. Provided by the page
 * component, not in root and not on the route: a component's providers die
 * with the component, so a stale failure never survives into a later visit.
 */
@Injectable()
export class NewProductStore {
  private readonly products = inject(ProductsService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<NewProductStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, product: NewProduct): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.products
      .create(product)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.showTheListing(venueSlug),
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }

  /**
   * Straight back to the listing, which is where the new product shows up:
   * US-06's first criterion is that it appears on the menu, and a form that
   * stays open afterwards makes it look like nothing happened.
   */
  private showTheListing(venueSlug: string): void {
    this.state.set('idle');

    this.router.navigate([venueSlug, 'staff', 'products']).then(
      (navigated) => {
        if (!navigated) this.state.set('unreachable');
      },
      () => this.state.set('unreachable'),
    );
  }
}

/**
 * Only the one failure an administrator can act on gets its own answer. The
 * rest — a dropped connection, a 500, a rule the form did not catch — are the
 * same thing from the screen's point of view: nothing was created.
 */
function reasonFor(error: unknown): NewProductStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return error.error?.type === ProblemTypes.productNameTaken ? 'nameTaken' : 'unreachable';
}
