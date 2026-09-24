import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { concatMap, from, map, tap } from 'rxjs';
import type { Observable } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { ProductsService } from './products.service';
import type { Product, ProductCorrection } from './products.service';

/**
 * How saving ended up. 'partial' is the odd one: the correction went through
 * and something after it did not, so "nothing was saved" would be a lie.
 * 'stockMoved' is a partial with its own answer: sales left less than the
 * adjustment takes away, and looking at the stock again is the fix.
 */
type SaveStatus = 'idle' | 'sending' | 'nameTaken' | 'unreachable' | 'partial' | 'stockMoved';

type AccessStatus = 'idle' | 'sending' | 'unreachable';

/** Everything one press of "Guardar cambios" asks for. */
export interface ProductChanges {
  readonly correction: ProductCorrection;
  readonly photo: File | null;
  /** How much the stock moves, up or down. 0 leaves it alone. */
  readonly stockChange: number;
  /** null leaves the nightly switch where it is. */
  readonly isAvailable: boolean | null;
}

/**
 * State of the screen that corrects a product. Provided by the page
 * component, so it dies with the screen and a stale message never survives
 * into a later visit.
 */
@Injectable()
export class EditProductStore {
  private readonly products = inject(ProductsService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly saving = signal<SaveStatus>('idle');

  private readonly access = signal<AccessStatus>('idle');

  /** The product as the API last returned it, so the screen shows the new state. */
  private readonly current = signal<Product | null>(null);

  private readonly adjusted = signal(0);

  readonly status = this.saving.asReadonly();

  readonly accessStatus = this.access.asReadonly();

  readonly updated = this.current.asReadonly();

  /** How many adjustments reached the API, so the form never sends one twice. */
  readonly stockAdjustments = this.adjusted.asReadonly();

  readonly isBusy = computed(() => this.saving() === 'sending' || this.access() === 'sending');

  /**
   * One request per thing that changed, one after the other. The correction
   * goes first, because a taken name is the one refusal the administrator can
   * fix and nothing else should be sent past it. The stock goes before the
   * switch: the domain refuses to turn on a product with nothing to sell.
   */
  save(venueSlug: string, id: string, changes: ProductChanges): void {
    if (this.isBusy()) return;

    this.saving.set('sending');

    const steps: (() => Observable<Product>)[] = [
      () => this.products.update(id, changes.correction),
    ];

    const { photo, stockChange, isAvailable } = changes;

    // The upload answers with the address alone; the rest is what the
    // correction just returned.
    if (photo !== null)
      steps.push(() =>
        this.products
          .uploadImage(id, photo)
          .pipe(map(({ imageUrl }) => ({ ...(this.current() as Product), imageUrl }))),
      );

    if (stockChange !== 0)
      steps.push(() =>
        this.products
          .adjustStock(id, stockChange)
          .pipe(tap(() => this.adjusted.update((count) => count + 1))),
      );

    if (isAvailable !== null)
      steps.push(() =>
        isAvailable ? this.products.markAvailable(id) : this.products.markUnavailable(id),
      );

    let done = 0;

    from(steps)
      .pipe(
        concatMap((step) => step()),
        tap((product) => {
          this.current.set(product);
          done++;
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        complete: () => this.showTheListing(venueSlug),
        error: (error: unknown) =>
          this.saving.set(done === 0 ? reasonFor(error) : laterReasonFor(error)),
      });
  }

  deactivate(id: string): void {
    if (this.isBusy()) return;

    this.access.set('sending');

    this.products
      .deactivate(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (product) => {
          this.current.set(product);
          this.access.set('idle');
        },
        error: () => this.access.set('unreachable'),
      });
  }

  /** Straight back to the listing, like a new product: that is where the change shows. */
  private showTheListing(venueSlug: string): void {
    this.saving.set('idle');

    this.router.navigate([venueSlug, 'staff', 'products']).then(
      (navigated) => {
        if (!navigated) this.saving.set('unreachable');
      },
      () => this.saving.set('unreachable'),
    );
  }
}

/**
 * Only the one failure an administrator can act on gets its own answer. The
 * rest — a dropped connection, a 500, a rule the form did not catch — are the
 * same thing from the screen's point of view: nothing changed.
 */
function reasonFor(error: unknown): SaveStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return error.error?.type === ProblemTypes.productNameTaken ? 'nameTaken' : 'unreachable';
}

/** After the correction went through: something was saved, whatever failed. */
function laterReasonFor(error: unknown): SaveStatus {
  if (!(error instanceof HttpErrorResponse)) return 'partial';

  return error.error?.type === ProblemTypes.productStockMoved ? 'stockMoved' : 'partial';
}
