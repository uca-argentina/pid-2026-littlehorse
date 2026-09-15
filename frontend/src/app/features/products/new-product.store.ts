import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap } from 'rxjs';
import { Router } from '@angular/router';
import { ProblemTypes } from '../../core/api/problem-types';
import { ProductsService } from './products.service';
import type { NewProduct } from './products.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans,
 * so "sending" and "that name is taken" cannot both be true at once.
 * 'imageFailed' is the odd one: the product exists by then, only its photo
 * did not make it, and the screen has to say so rather than offer to create
 * it again.
 */
type NewProductStatus = 'idle' | 'sending' | 'nameTaken' | 'unreachable' | 'imageFailed';

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

  /**
   * Two requests when there is a photo: the product first, because the upload
   * needs its id to be filed under, and the listing only once both are done —
   * arriving to a row with an empty square where the photo should be reads as
   * a failed upload.
   */
  submit(venueSlug: string, product: NewProduct, image: File | null): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.products
      .create(product)
      .pipe(
        // A failed upload is caught here and not in the error branch below:
        // by then the product is on the menu, and "nothing was created" would
        // be a lie.
        switchMap((created) =>
          image === null
            ? of('done' as const)
            : this.products.uploadImage(created.id, image).pipe(
                map(() => 'done' as const),
                catchError(() => of('imageFailed' as const)),
              ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (outcome) => {
          if (outcome === 'imageFailed') this.state.set('imageFailed');
          else this.showTheListing(venueSlug);
        },
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
