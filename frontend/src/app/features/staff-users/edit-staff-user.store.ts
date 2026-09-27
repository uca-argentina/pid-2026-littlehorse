import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import type { StaffRole } from '../../core/staff/staff-roles';
import { StaffUsersService } from './staff-users.service';
import type { StaffUser } from './staff-users.service';

/**
 * How one of the three things this screen can do ended up. They are tracked
 * apart on purpose: a password that was refused must not make the role change
 * next to it look like it failed too.
 */
type ActionStatus =
  'idle' | 'sending' | 'saved' | 'passwordTooShort' | 'lastAdministrator' | 'unreachable';

/**
 * State of the screen that corrects somebody's account. Provided by the page
 * component, so it dies with the screen and a stale message never survives
 * into a later visit.
 */
@Injectable()
export class EditStaffUserStore {
  private readonly staffUsers = inject(StaffUsersService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly role = signal<ActionStatus>('idle');

  private readonly password = signal<ActionStatus>('idle');

  private readonly access = signal<ActionStatus>('idle');

  /** The account as the API last returned it, so the screen shows the new state. */
  private readonly current = signal<StaffUser | null>(null);

  readonly roleStatus = this.role.asReadonly();

  readonly passwordStatus = this.password.asReadonly();

  readonly accessStatus = this.access.asReadonly();

  readonly updated = this.current.asReadonly();

  readonly isBusy = computed(
    () => this.role() === 'sending' || this.password() === 'sending' || this.access() === 'sending',
  );

  changeRole(id: string, role: StaffRole): void {
    this.run(this.role, () => this.staffUsers.changeRole(id, role));
  }

  resetPassword(id: string, password: string): void {
    this.run(this.password, () => this.staffUsers.resetPassword(id, password));
  }

  deactivate(id: string): void {
    this.run(this.access, () => this.staffUsers.deactivate(id));
  }

  reactivate(id: string): void {
    this.run(this.access, () => this.staffUsers.reactivate(id));
  }

  /**
   * The four actions differ only in which request they send and which of the
   * three outcomes they report through. Writing them out four times would be
   * four places to forget the in-flight guard.
   *
   * A factory and not an observable: building the request is the caller's last
   * statement, so passing one already built would ask the service for it before
   * the guard could refuse, and a second tap would reach it anyway.
   */
  private run(
    status: ReturnType<typeof signal<ActionStatus>>,
    send: () => Observable<StaffUser>,
  ): void {
    if (status() === 'sending') return;

    status.set('sending');

    send()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (user) => {
          this.current.set(user);
          status.set('saved');
        },
        error: (error: unknown) => status.set(reasonFor(error)),
      });
  }
}

/**
 * Only the failures an administrator can act on get their own answer. The rest
 * — a dropped connection, a 500, a rule the form did not catch — are the same
 * thing from the screen's point of view: nothing changed.
 */
function reasonFor(error: unknown): ActionStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  const type = problemTypeOf(error);

  if (type === ProblemTypes.passwordTooShort) return 'passwordTooShort';
  if (type === ProblemTypes.lastAdministrator) return 'lastAdministrator';

  return 'unreachable';
}
