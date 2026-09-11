import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ProblemTypes } from '../../core/api/problem-types';
import { SessionStorage } from '../../core/auth/session-storage';
import { StaffLoginService } from './staff-login.service';

/**
 * What the screen is doing right now. One value rather than a set of booleans
 * so that "sending" and "invalid credentials" cannot both be true at once.
 */
type LoginStatus = 'idle' | 'sending' | 'invalidCredentials' | 'sessionExpired' | 'unreachable';

/**
 * State and transitions of the staff login screen. Provided by the feature's
 * route, not in root: it dies with the screen, so a stale failure never
 * survives into a later visit.
 */
@Injectable()
export class StaffLoginStore {
  private readonly logins = inject(StaffLoginService);

  private readonly sessions = inject(SessionStorage);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<LoginStatus>('idle');

  readonly username = signal('');

  readonly password = signal('');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  /**
   * Both fields carry something and nothing is in flight. Deliberately not a
   * check on the username's shape: the venue decides what a username looks
   * like, and guessing here would reject people the server accepts.
   */
  readonly canSubmit = computed(
    () => this.username().trim().length > 0 && this.password().length > 0 && !this.isSending(),
  );

  /** The screen was reopened because the shift's token ran out. */
  startAfterExpiry(): void {
    this.state.set('sessionExpired');
  }

  submit(venueSlug: string): void {
    if (!this.canSubmit()) return;

    this.state.set('sending');

    this.logins
      .logIn(venueSlug, { username: this.username().trim(), password: this.password() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => {
          this.sessions.remember(session);
          this.state.set('idle');
          this.leaveTheLoginScreen(venueSlug);
        },
        error: (error: unknown) => this.fail(error),
      });
  }

  /**
   * A navigation that silently refuses leaves the person looking at the login
   * screen with no idea it worked. Better to say the app could not continue.
   */
  private leaveTheLoginScreen(venueSlug: string): void {
    this.router.navigate([venueSlug, 'staff']).then(
      (navigated) => {
        if (!navigated) this.state.set('unreachable');
      },
      () => this.state.set('unreachable'),
    );
  }

  private fail(error: unknown): void {
    // The password is cleared on every failure: the person retypes it, and it
    // does not sit in a field on a tablet anyone behind the bar can pick up.
    this.password.set('');

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
    error.error?.type === ProblemTypes.invalidCredentials
  );
}
