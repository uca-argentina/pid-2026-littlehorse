import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { STAFF_ROLE_DESCRIPTIONS } from '../../../core/staff/staff-roles';
import type { StaffRole } from '../../../core/staff/staff-roles';
import { PasswordEye } from '../../../shared/password-eye/password-eye';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { NewStaffUserStore } from '../new-staff-user.store';

/** Kept in step with CreateStaffUserHandler.PasswordMinLength on the server. */
const PASSWORD_MIN_LENGTH = 8;

/** Kept in step with StaffUser's own rule in the domain. */
const USERNAME_MIN_LENGTH = 3;

/**
 * Validators.minLength counts the spaces, so "   " passes a minimum of three.
 * The server trims before judging, and a form that disagrees with it rejects
 * what the API would have accepted, or the other way round.
 */
function trimmedMinLength(minimum: number) {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = typeof control.value === 'string' ? control.value.trim() : '';

    return value.length >= minimum ? null : { tooShort: { minimum } };
  };
}

@Component({
  selector: 'drinkit-new-staff-user-page',
  imports: [PasswordEye, ReactiveFormsModule, AdminHeader, RouterLink],
  // On the component and not on the route. A route's injector is created once
  // per route config and kept, so a store provided there outlives the screen:
  // cancel after a rejected attempt, come back, and the old message is still
  // on an empty form. A component's providers die with the component.
  providers: [NewStaffUserStore],
  styleUrl: './new-staff-user.page.scss',
  templateUrl: './new-staff-user.page.html',
})
export class NewStaffUserPage {
  protected readonly store = inject(NewStaffUserStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly roles = STAFF_ROLE_DESCRIPTIONS;

  protected readonly form = new FormGroup({
    username: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, trimmedMinLength(USERNAME_MIN_LENGTH)],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, trimmedMinLength(PASSWORD_MIN_LENGTH)],
    }),
    // Nothing picked for them. Picking the role is the one real decision on
    // this screen, and a default of "the least privileged" was handing out
    // mozo — which has no screen yet — to anyone who skipped the field.
    role: new FormControl<StaffRole | null>(null, { validators: [Validators.required] }),
  });

  /**
   * Whether the form has already been submitted once. Errors stay hidden until
   * then: flagging a username as too short while it is still being typed is
   * noise, not help.
   */
  private readonly attempted = signal(false);

  /**
   * Reactive forms report through RxJS and this app is zoneless, so the form has
   * to be bridged into the signal graph. Without this the template would never
   * re-evaluate and no message would ever appear.
   *
   * valueChanges and not statusChanges: the group's status stays 'INVALID' while
   * two fields are wrong, and re-emitting an identical value notifies no signal,
   * so fixing one field would leave its message on screen until the whole form
   * turned valid. Every keystroke emits a fresh object, which does notify.
   */
  private readonly typed = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly usernameError = computed(() =>
    this.errorOf('username', 'El usuario tiene que tener al menos tres caracteres.'),
  );

  protected readonly passwordError = computed(() =>
    this.errorOf('password', 'La contraseña tiene que tener al menos ocho caracteres.'),
  );

  protected readonly roleError = computed(() => this.errorOf('role', 'Elegí un rol.'));

  /**
   * Dots by default: this is a laptop on a bar and somebody walks past. The eye
   * exists because the administrator has to read this password out loud to the
   * person it belongs to, and retyping it to check what they wrote is what
   * makes anyone settle for something short.
   */
  protected readonly passwordVisible = signal(false);

  protected readonly passwordType = computed(() => (this.passwordVisible() ? 'text' : 'password'));

  constructor() {
    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template — binding [disabled] on a
    // control a form directive owns is what Angular warns about.
    effect(() => {
      if (this.store.isSending()) this.form.disable();
      else this.form.enable();
    });
  }

  /**
   * Reads the control rather than re-deriving the rule, so the message and the
   * validator can never disagree. Touching the value signal first is what makes
   * this recompute: the control itself is not reactive.
   */
  private errorOf(field: 'username' | 'password' | 'role', message: string): string | null {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls[field].invalid ? message : null;
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.store.isSending()) return;

    const { username, password, role } = this.form.getRawValue();

    // Validators.required already rejected a null role above; this is only
    // what the compiler needs to see.
    if (role === null) return;

    this.store.submit(this.venueSlug(), { username: username.trim(), password, role });
  }
}
