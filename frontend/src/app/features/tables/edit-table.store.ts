import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { TablesService } from './tables.service';
import type { VipTableDescription } from './tables.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans,
 * so "sending" and "it failed" cannot both be true at once.
 */
type EditTableStatus = 'idle' | 'sending' | 'gone' | 'forbidden' | 'unreachable';

/**
 * State and transitions of the "edit table" screen. Provided by the page
 * component, so a failure on one table never shows up on the next one opened.
 */
@Injectable()
export class EditTableStore {
  private readonly tables = inject(TablesService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<EditTableStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, id: string, description: VipTableDescription): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.tables
      .update(id, description)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.state.set('idle');
          void this.router.navigate([venueSlug, 'staff', 'tables']);
        },
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }
}

/**
 * A role taken away never comes back on a retry, and a table that is gone
 * will not either; everything else is the same from here: nothing was saved.
 */
function reasonFor(error: unknown): EditTableStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';
  if (error.status === 404) return 'gone';

  return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
}
