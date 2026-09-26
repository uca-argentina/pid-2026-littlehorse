import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { STAFF_ROLE_DESCRIPTIONS, staffRoleName } from '../../../core/staff/staff-roles';
import type { StaffRole } from '../../../core/staff/staff-roles';
import { AuditNote } from '../../../shared/audit-note/audit-note';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { PasswordEye } from '../../../shared/password-eye/password-eye';
import { EditStaffUserStore } from '../edit-staff-user.store';
import { STAFF_USERS_URL } from '../staff-users.service';
import type { StaffUser } from '../staff-users.service';

/** Kept in step with StaffPasswordPolicy.MinLength on the server. */
const PASSWORD_MIN_LENGTH = 8;

@Component({
  selector: 'drinkit-edit-staff-user-page',
  imports: [AdminHeader, AuditNote, PasswordEye, ReactiveFormsModule, RouterLink],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a stale message from one
  // person's screen to the next one opened.
  providers: [EditStaffUserStore],
  styleUrl: './edit-staff-user.page.scss',
  templateUrl: './edit-staff-user.page.html',
})
export class EditStaffUserPage {
  protected readonly store = inject(EditStaffUserStore);

  /** Both from the path, bound by the router. */
  readonly venueSlug = input.required<string>();

  readonly id = input.required<string>();

  protected readonly roles = STAFF_ROLE_DESCRIPTIONS;

  /**
   * The venue's whole team, filtered here to the one being corrected. There is
   * no endpoint that serves a single user, and adding one to save a listing
   * that already fits in one response would be a request nobody needs — the
   * same trade the search on the listing makes.
   */
  protected readonly staff = httpResource<StaffUser[]>(() => STAFF_USERS_URL);

  /** What the API last said about them: the listing at first, then each answer. */
  protected readonly person = computed<StaffUser | undefined>(
    () =>
      this.store.updated() ??
      (this.staff.hasValue() ? this.staff.value() : []).find((user) => user.id === this.id()),
  );

  protected readonly username = computed(() => this.person()?.username ?? '');

  protected readonly isActive = computed(() => this.person()?.isActive ?? false);

  /** Loaded, and nobody here has that id. Not the same as still loading. */
  protected readonly isMissing = computed(
    () => this.staff.hasValue() && this.person() === undefined,
  );

  protected readonly pickedRole = new FormControl<StaffRole | null>(null);

  protected readonly password = new FormControl('', { nonNullable: true });

  /** Dots by default, same as everywhere else a password is typed. */
  protected readonly passwordVisible = signal(false);

  private readonly typed = toSignal(this.password.valueChanges, { initialValue: '' });

  private readonly passwordAttempted = signal(false);

  /** Whether the deactivation has been asked for once and is waiting to be confirmed. */
  protected readonly isConfirmingDeactivation = signal(false);

  /**
   * Checked here as well as on the server, because the venue's connection is
   * the slowest part of this screen and eight characters is not worth a round
   * trip to find out.
   */
  protected readonly passwordError = computed(() => {
    if (!this.passwordAttempted()) return null;

    return this.typed().trim().length < PASSWORD_MIN_LENGTH
      ? 'La contraseña tiene que tener al menos ocho caracteres.'
      : null;
  });

  protected readonly roleName = computed(() => staffRoleName(this.person()?.role ?? ''));

  constructor() {
    // The role they have now is what the picker starts on, so an administrator
    // sees what they are changing from rather than an empty group.
    effect(() => {
      const role = this.person()?.role;

      if (role !== undefined && this.pickedRole.value === null)
        this.pickedRole.setValue(role as StaffRole);
    });
  }

  protected saveRole(): void {
    const role = this.pickedRole.value;

    if (role === null) return;

    this.store.changeRole(this.id(), role);
  }

  protected savePassword(): void {
    this.passwordAttempted.set(true);

    if (this.passwordError() !== null) return;

    this.store.resetPassword(this.id(), this.password.value);
    this.password.reset();
    this.passwordAttempted.set(false);
  }

  /**
   * Asks before deactivating instead of doing it.
   *
   * Nothing on this screen undoes it in one step, and it sits in the same run
   * of buttons as saving a role and changing a password. Asking twice costs a
   * touch; getting it wrong costs somebody their access mid-shift.
   */
  protected askToDeactivate(): void {
    this.isConfirmingDeactivation.set(true);
  }

  protected cancelDeactivation(): void {
    this.isConfirmingDeactivation.set(false);
  }

  protected deactivate(): void {
    this.isConfirmingDeactivation.set(false);
    this.store.deactivate(this.id());
  }

  protected reactivate(): void {
    this.store.reactivate(this.id());
  }
}
