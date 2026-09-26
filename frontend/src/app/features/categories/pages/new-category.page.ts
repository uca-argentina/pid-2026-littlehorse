import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { trimmedMinLength } from '../../../shared/forms/trimmed-min-length';
import { NewCategoryStore } from '../new-category.store';

/** Kept in step with Category.NameMaxLength in the domain. */
const NAME_MAX_LENGTH = 40;

@Component({
  selector: 'drinkit-new-category-page',
  imports: [AdminHeader, ReactiveFormsModule, RouterLink],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a rejected attempt into
  // the next visit. A component's providers die with the component.
  providers: [NewCategoryStore],
  styleUrl: './new-category.page.scss',
  templateUrl: './new-category.page.html',
})
export class NewCategoryPage {
  protected readonly store = inject(NewCategoryStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [trimmedMinLength(1), Validators.maxLength(NAME_MAX_LENGTH)],
    }),
  });

  /**
   * Whether the form has already been submitted once. Errors stay hidden until
   * then: flagging the name as missing while it is still being typed is noise.
   */
  private readonly attempted = signal(false);

  /**
   * Reactive forms report through RxJS and this app is zoneless, so the form
   * has to be bridged into the signal graph. valueChanges and not
   * statusChanges: every keystroke emits a fresh object, which does notify.
   */
  private readonly typed = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly nameError = computed(() => {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls.name.invalid
      ? 'Ponele un nombre a la categoría, de hasta cuarenta caracteres.'
      : null;
  });

  constructor() {
    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template.
    effect(() => {
      if (this.store.isSending()) this.form.disable();
      else this.form.enable();
    });
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.store.isSending()) return;

    this.store.submit(this.venueSlug(), { name: this.form.getRawValue().name.trim() });
  }
}
