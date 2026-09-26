import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { CategoriesService } from './categories.service';
import type { NewCategory } from './categories.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans,
 * so "sending" and "that name is taken" cannot both be true at once.
 */
type NewCategoryStatus = 'idle' | 'sending' | 'nameTaken' | 'unreachable';

/**
 * State and transitions of the "new category" screen. Provided by the page
 * component, so it dies with the screen and a stale failure never survives into
 * a later visit.
 */
@Injectable()
export class NewCategoryStore {
  private readonly categories = inject(CategoriesService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<NewCategoryStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, category: NewCategory): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.categories
      .create(category)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.showTheListing(venueSlug),
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }

  /**
   * Back to the products, where the button that led here is and where the new
   * category is one of the choices of the next product loaded.
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
function reasonFor(error: unknown): NewCategoryStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return problemTypeOf(error) === ProblemTypes.categoryNameTaken ? 'nameTaken' : 'unreachable';
}
