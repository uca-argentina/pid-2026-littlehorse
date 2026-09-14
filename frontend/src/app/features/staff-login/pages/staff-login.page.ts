import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { PasswordEye } from '../../../shared/password-eye/password-eye';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import { StaffLoginStore } from '../staff-login.store';

/**
 * Validators.required accepts a field holding only spaces, and the API would
 * reject that after a round trip the person waits for.
 */
function notBlank(control: AbstractControl): ValidationErrors | null {
  return typeof control.value === 'string' && control.value.trim().length === 0
    ? { notBlank: true }
    : null;
}

@Component({
  selector: 'drinkit-staff-login-page',
  imports: [PasswordEye, ReactiveFormsModule, VenueBrand],
  // On the component and not on the route, for the same reason as the staff
  // form: a route's injector is created once and kept, so the store would
  // carry a failed attempt into the next visit.
  providers: [StaffLoginStore],
  styleUrl: './staff-login.page.scss',
  templateUrl: './staff-login.page.html',
})
export class StaffLoginPage {
  protected readonly store = inject(StaffLoginStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** From the query string, set by the guard when it turned someone away. */
  readonly expired = input<string | undefined>(undefined);

  protected readonly form = new FormGroup({
    username: new FormControl('', {
      nonNullable: true,
      // Deliberately no check on the username's shape: the venue decides what a
      // username looks like, and guessing here would reject people the server
      // accepts.
      validators: [Validators.required, notBlank],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  /**
   * Reactive forms report through RxJS and this app is zoneless, so the form's
   * status has to be bridged into the signal graph. Without this the template
   * would never re-evaluate and the button would stay disabled forever.
   */
  private readonly formStatus = toSignal(this.form.statusChanges, {
    initialValue: this.form.status,
  });

  /**
   * Dots by default. The eye is here because this screen is used on a tablet in
   * the dark, with a password somebody dictated: typing it blind and getting
   * back only "usuario o contraseña incorrectos" is how a shift starts with
   * three failed attempts and nobody knowing which half was wrong.
   */
  protected readonly passwordVisible = signal(false);

  protected readonly passwordType = computed(() => (this.passwordVisible() ? 'text' : 'password'));

  protected readonly canSubmit = computed(
    () => this.formStatus() === 'VALID' && !this.store.isSending(),
  );

  constructor() {
    effect(() => {
      if (this.expired() !== undefined) this.store.startAfterExpiry();
    });

    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template — binding [disabled] on a
    // control that a form directive owns is what Angular warns about.
    effect(() => {
      if (this.store.isSending()) this.form.disable();
      else this.form.enable();
    });

    // The password never survives a failed attempt: the person retypes it, and
    // it does not sit in a field on a tablet anyone behind the bar can pick up.
    effect(() => {
      const status = this.store.status();

      if (status === 'invalidCredentials' || status === 'unreachable')
        this.form.controls.password.reset();
    });
  }

  protected submit(): void {
    if (!this.canSubmit()) return;

    const { username, password } = this.form.getRawValue();

    this.store.submit(this.venueSlug(), { username: username.trim(), password });
  }
}
