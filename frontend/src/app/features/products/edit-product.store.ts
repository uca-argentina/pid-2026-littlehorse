import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import type { Observable } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { ProductsService } from './products.service';
import type { Product, ProductCorrection } from './products.service';

/**
 * How one of the four things this screen can do ended up. Tracked apart on
 * purpose, same reasoning as EditStaffUserStore: a correction that gets
 * refused must not make the deactivation, the photo or the stock next to it
 * look like it failed too.
 */
type ActionStatus = 'idle' | 'sending' | 'saved' | 'nameTaken' | 'unreachable';

/**
 * State of the screen that corrects a product. Provided by the page
 * component, so it dies with the screen and a stale message never survives
 * into a later visit.
 */
@Injectable()
export class EditProductStore {
  private readonly products = inject(ProductsService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly details = signal<ActionStatus>('idle');

  private readonly access = signal<ActionStatus>('idle');

  private readonly photo = signal<ActionStatus>('idle');

  private readonly stock = signal<ActionStatus>('idle');

  /** The product as the API last returned it, so the screen shows the new state. */
  private readonly current = signal<Product | null>(null);

  readonly detailsStatus = this.details.asReadonly();

  readonly accessStatus = this.access.asReadonly();

  readonly photoStatus = this.photo.asReadonly();

  readonly stockStatus = this.stock.asReadonly();

  readonly updated = this.current.asReadonly();

  readonly isBusy = computed(
    () =>
      this.details() === 'sending' ||
      this.access() === 'sending' ||
      this.photo() === 'sending' ||
      this.stock() === 'sending',
  );

  update(id: string, correction: ProductCorrection): void {
    this.run(this.details, () => this.products.update(id, correction));
  }

  deactivate(id: string): void {
    this.run(this.access, () => this.products.deactivate(id));
  }

  restock(id: string, units: number): void {
    this.run(this.stock, () => this.products.restock(id, units));
  }

  /**
   * The upload answers with the new address and nothing else, so the product
   * that was on screen is kept and only its picture is swapped. Passing the
   * product in, and not just its id, is what makes that possible without the
   * store holding a second copy of it.
   */
  uploadImage(product: Product, image: File): void {
    this.run(this.photo, () =>
      this.products
        .uploadImage(product.id, image)
        .pipe(map(({ imageUrl }) => ({ ...product, imageUrl }))),
    );
  }

  /**
   * The actions differ only in which request they send. Writing them out
   * four times would be four places to forget the in-flight guard.
   */
  private run(
    status: ReturnType<typeof signal<ActionStatus>>,
    send: () => Observable<Product>,
  ): void {
    if (status() === 'sending') return;

    status.set('sending');

    send()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (product) => {
          this.current.set(product);
          status.set('saved');
        },
        error: (error: unknown) => status.set(reasonFor(error)),
      });
  }
}

/**
 * Only the one failure an administrator can act on gets its own answer. The
 * rest — a dropped connection, a 500, a rule the form did not catch — are the
 * same thing from the screen's point of view: nothing changed.
 */
function reasonFor(error: unknown): ActionStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return error.error?.type === ProblemTypes.productNameTaken ? 'nameTaken' : 'unreachable';
}
