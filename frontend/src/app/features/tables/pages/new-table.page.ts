import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { NewTableStore } from '../new-table.store';
import { TABLE_NUMBER_MAX } from '../tables.service';

/**
 * Whole digits only, between 1 and the domain's limit. Judged on the trimmed
 * text and not through Number(): "5.5", "1e2" and "0x10" all parse, and none of
 * them is a table number.
 */
function validNumber(control: AbstractControl): ValidationErrors | null {
  const text = typeof control.value === 'string' ? control.value.trim() : '';

  if (!/^\d+$/.test(text)) return { notANumber: true };

  const value = Number(text);

  return value >= 1 && value <= TABLE_NUMBER_MAX ? null : { outOfRange: true };
}

@Component({
  selector: 'drinkit-new-table-page',
  imports: [AdminHeader, ReactiveFormsModule, RouterLink],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a rejected attempt into
  // the next visit. A component's providers die with the component.
  providers: [NewTableStore],
  styleUrl: './new-table.page.scss',
  templateUrl: './new-table.page.html',
})
export class NewTablePage {
  protected readonly store = inject(NewTableStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly numberMax = TABLE_NUMBER_MAX;

  protected readonly form = new FormGroup({
    number: new FormControl('', { nonNullable: true, validators: [validNumber] }),
  });

  /**
   * Whether the form has already been submitted once. Errors stay hidden until
   * then: flagging the number as missing while it is still being typed is noise.
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

  protected readonly numberError = computed(() => {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls.number.invalid
      ? `El número tiene que ser entero, entre 1 y ${TABLE_NUMBER_MAX}.`
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

    this.store.submit(this.venueSlug(), { number: Number(this.form.getRawValue().number.trim()) });
  }
}
