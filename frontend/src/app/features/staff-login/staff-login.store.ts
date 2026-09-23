import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { SessionStorage } from '../../core/auth/session-storage';
import { staffLandingFor } from '../../core/auth/staff-landing';
import type { StaffCredentials } from '../../core/auth/staff-session';
import { StaffLoginService } from './staff-login.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans
 * so that "sending" and "invalid credentials" cannot both be true at once.
 */
type LoginStatus = 'idle' | 'sending' | 'invalidCredentials' | 'sessionExpired' | 'unreachable';

/**
 * State and transitions of the staff login screen. Provided by the page
 * component, not in root and not on the route: a component's providers die
 * with the component, so a stale failure never survives into a later visit.
 * (A route's injector would not — Angular creates it once and keeps it.)
 */
@Injectable()
export class StaffLoginStore {
  private readonly logins = inject(StaffLoginService);

  private readonly sessions = inject(SessionStorage);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<LoginStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  /** The screen was reopened because the shift's token ran out. */
  startAfterExpiry(): void {
    this.state.set('sessionExpired');
  }

  submit(venueSlug: string, credentials: StaffCredentials): void {
    if (this.isSending()) return;

    this.state.set('sending');

    this.logins
      .logIn(venueSlug, credentials)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => {
          this.sessions.remember(session);
          this.state.set('idle');
          this.leaveTheLoginScreen(staffLandingFor(session.role, venueSlug));
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  /**
   * A navigation that silently refuses leaves the person looking at the login
   * screen with no idea it worked. Better to say the app could not continue.
   */
  private leaveTheLoginScreen(landing: string[]): void {
    this.router.navigate(landing).then(
      (navigated) => {
        if (!navigated) this.state.set('unreachable');
      },
      () => this.state.set('unreachable'),
    );
  }

  // Clearing the password is the screen's job now that the form owns it; the
  // store only says what went wrong.
  private fail(error: unknown): void {
    this.state.set(isInvalidCredentials(error) ? 'invalidCredentials' : 'unreachable');
  }
}

/**
 * The one failure a rejected login reports. The API refuses to say whether the
 * username or the password was wrong, so neither does the screen: telling them
 * apart lets anyone find out who works at the venue by trying names.
 */
function isInvalidCredentials(error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 401 &&
    problemTypeOf(error) === ProblemTypes.invalidCredentials
  );
}
