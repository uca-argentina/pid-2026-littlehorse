import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { StaffUsersService } from './staff-users.service';
import type { NewStaffUser } from './staff-users.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans,
 * so "sending" and "that username is taken" cannot both be true at once.
 */
type NewStaffUserStatus = 'idle' | 'sending' | 'usernameTaken' | 'passwordTooShort' | 'unreachable';

/**
 * State and transitions of the "new staff user" screen. Provided by the page
 * component, not in root and not on the route: a component's providers die
 * with the component, so a stale failure never survives into a later visit.
 * (A route's injector would not — Angular creates it once and keeps it.)
 */
@Injectable()
export class NewStaffUserStore {
  private readonly staffUsers = inject(StaffUsersService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<NewStaffUserStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  submit(venueSlug: string, user: NewStaffUser): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.staffUsers
      .create(user)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.showTheListing(venueSlug),
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }

  /**
   * Straight back to the listing, which is where the new person shows up:
   * US-03's first criterion is that they appear there, and a form that stays
   * open afterwards makes it look like nothing happened.
   */
  private showTheListing(venueSlug: string): void {
    this.state.set('idle');

    this.router.navigate([venueSlug, 'staff', 'users']).then(
      (navigated) => {
        if (!navigated) this.state.set('unreachable');
      },
      () => this.state.set('unreachable'),
    );
  }
}

/**
 * Only the two failures an administrator can act on get their own answer. The
 * rest — a dropped connection, a 500, a domain rule the form did not catch —
 * are the same thing from the screen's point of view: nobody was created.
 */
function reasonFor(error: unknown): NewStaffUserStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  const type = problemTypeOf(error);

  if (type === ProblemTypes.usernameTaken) return 'usernameTaken';
  if (type === ProblemTypes.passwordTooShort) return 'passwordTooShort';

  return 'unreachable';
}
