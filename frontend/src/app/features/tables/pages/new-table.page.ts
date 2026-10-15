import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { NewTableStore } from '../new-table.store';
import { TABLE_CAPACITY_MAX, TABLE_LOCATION_MAX_LENGTH, TABLE_NUMBER_MAX } from '../tables.service';

/**
 * Whole digits only, between 1 and the domain's limit. Judged on the trimmed
 * text and not through Number(): "5.5", "1e2" and "0x10" all parse, and none of
 * them is a table number or a head count.
 */
function wholeBetweenOneAnd(max: number) {
  return (control: AbstractControl): ValidationErrors | null => {
    const text = typeof control.value === 'string' ? control.value.trim() : '';

    if (!/^\d+$/.test(text)) return { notANumber: true };

    const value = Number(text);

    return value >= 1 && value <= max ? null : { outOfRange: true };
  };
}

/** Counted on the trimmed text, which is what the domain keeps. */
function locationFits(control: AbstractControl): ValidationErrors | null {
  const text = typeof control.value === 'string' ? control.value.trim() : '';

  return Validators.maxLength(TABLE_LOCATION_MAX_LENGTH)({ value: text } as AbstractControl);
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

  protected readonly capacityMax = TABLE_CAPACITY_MAX;

  protected readonly locationMaxLength = TABLE_LOCATION_MAX_LENGTH;

  protected readonly form = new FormGroup({
    number: new FormControl('', {
      nonNullable: true,
      validators: [wholeBetweenOneAnd(TABLE_NUMBER_MAX)],
    }),
    capacity: new FormControl('', {
      nonNullable: true,
      validators: [wholeBetweenOneAnd(TABLE_CAPACITY_MAX)],
    }),
    location: new FormControl('', { nonNullable: true, validators: [locationFits] }),
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

  protected readonly capacityError = computed(() => {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls.capacity.invalid
      ? `Tiene que ser un número entero, entre 1 y ${TABLE_CAPACITY_MAX} personas.`
      : null;
  });

  protected readonly locationError = computed(() => {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls.location.invalid
      ? `La ubicación puede tener hasta ${TABLE_LOCATION_MAX_LENGTH} caracteres.`
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

    const { number, capacity, location } = this.form.getRawValue();

    this.store.submit(this.venueSlug(), {
      number: Number(number.trim()),
      capacity: Number(capacity.trim()),
      location: location.trim() === '' ? null : location.trim(),
    });
  }
}
