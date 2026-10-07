import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { NightsService } from './nights.service';
import type { NewNight } from './nights.service';

/** One value rather than a set of booleans, so two of them cannot be true at once. */
type NewNightStatus = 'idle' | 'sending' | 'overlaps' | 'crewChanged' | 'unreachable';

/**
 * State and transitions of the "new night" screen. Provided by the page
 * component, so a stale failure dies with it, as in NewStaffUserStore.
 */
@Injectable()
export class NewNightStore {
  private readonly nights = inject(NightsService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<NewNightStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, night: NewNight): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.nights
      .create(night)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.showTheListing(venueSlug),
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }

  /** Straight back to the listing, where US-35 says the new night shows up. */
  private showTheListing(venueSlug: string): void {
    this.state.set('idle');

    this.router.navigate([venueSlug, 'staff', 'nights']).then(
      (navigated) => {
        if (!navigated) this.state.set('unreachable');
      },
      () => this.state.set('unreachable'),
    );
  }
}

/**
 * Only the failures an administrator can act on get their own answer. A domain
 * rule the form did not catch, a dropped connection or a 500 all mean the same
 * from here: no night was created.
 */
function reasonFor(error: unknown): NewNightStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  const type = problemTypeOf(error);

  if (type === ProblemTypes.nightOverlaps) return 'overlaps';
  if (type === ProblemTypes.nightCrewMemberNotFound) return 'crewChanged';

  return 'unreachable';
}
