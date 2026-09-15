import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trimmedMinLength } from '../../../shared/forms/trimmed-min-length';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import { NewProductStore } from '../new-product.store';

/** Kept in step with Product's own rules in the domain. */
const NAME_MAX_LENGTH = 80;

const DESCRIPTION_MAX_LENGTH = 200;

/** Strictly above zero: a free product is a mistake, not an offer. */
function positive(control: AbstractControl): ValidationErrors | null {
  return typeof control.value === 'number' && control.value > 0 ? null : { positive: true };
}

/** Whole units: half a bottle is not something the bar can sell. */
function wholeNumber(control: AbstractControl): ValidationErrors | null {
  return Number.isInteger(control.value) ? null : { wholeNumber: true };
}

type Field = 'name' | 'description' | 'price' | 'stock';

@Component({
  selector: 'drinkit-new-product-page',
  imports: [ReactiveFormsModule, RouterLink, VenueBrand],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a rejected attempt into
  // the next visit. A component's providers die with the component.
  providers: [NewProductStore],
  styleUrl: './new-product.page.scss',
  templateUrl: './new-product.page.html',
})
export class NewProductPage {
  protected readonly store = inject(NewProductStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [trimmedMinLength(1), Validators.maxLength(NAME_MAX_LENGTH)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(DESCRIPTION_MAX_LENGTH)],
    }),
    // Nothing typed for them: a price of zero left in place by accident would
    // pass straight into the menu.
    price: new FormControl<number | null>(null, { validators: [Validators.required, positive] }),
    stock: new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(0), wholeNumber],
    }),
  });

  /**
   * Whether the form has already been submitted once. Errors stay hidden until
   * then: flagging a price as missing while it is still being typed is noise.
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

  protected readonly nameError = computed(() =>
    this.errorOf('name', 'Ponele un nombre al producto, de hasta ochenta caracteres.'),
  );

  protected readonly descriptionError = computed(() =>
    this.errorOf('description', 'La descripción no puede pasar los doscientos caracteres.'),
  );

  protected readonly priceError = computed(() =>
    this.errorOf('price', 'El precio tiene que ser mayor a cero.'),
  );

  protected readonly stockError = computed(() =>
    this.errorOf('stock', 'El stock tiene que ser un número entero, cero o más.'),
  );

  constructor() {
    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template.
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
  private errorOf(field: Field, message: string): string | null {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls[field].invalid ? message : null;
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.store.isSending()) return;

    const { name, description, price, stock } = this.form.getRawValue();

    // Validators.required already rejected these above; this is only what the
    // compiler needs to see.
    if (price === null || stock === null) return;

    this.store.submit(this.venueSlug(), {
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
      // The picture is uploaded from the listing once the product exists.
      imageUrl: null,
      price,
      stock,
    });
  }
}
