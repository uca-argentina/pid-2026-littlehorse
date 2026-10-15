import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { TablesService } from './tables.service';
import type { NewVipTable } from './tables.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans,
 * so "sending" and "that number is taken" cannot both be true at once.
 */
type NewTableStatus = 'idle' | 'sending' | 'numberTaken' | 'unreachable';

/**
 * State and transitions of the "new table" screen. Provided by the page
 * component, so it dies with the screen and a stale failure never survives into
 * a later visit.
 */
@Injectable()
export class NewTableStore {
  private readonly tables = inject(TablesService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<NewTableStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, table: NewVipTable): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.tables
      .create(table)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.showTheListing(venueSlug),
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }

  /** Straight back to the listing, which is where the new table shows up. */
  private showTheListing(venueSlug: string): void {
    this.state.set('idle');

    this.router.navigate([venueSlug, 'staff', 'tables']).then(
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
function reasonFor(error: unknown): NewTableStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return problemTypeOf(error) === ProblemTypes.tableNumberTaken ? 'numberTaken' : 'unreachable';
}
