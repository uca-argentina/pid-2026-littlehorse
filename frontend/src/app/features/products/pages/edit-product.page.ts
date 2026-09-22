import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trimmedMinLength } from '../../../shared/forms/trimmed-min-length';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { EditProductStore } from '../edit-product.store';
import {
  IMAGE_MAX_BYTES,
  IMAGE_TYPES,
  PRODUCTS_URL,
  PRODUCT_PLACEHOLDER,
} from '../products.service';
import type { Product } from '../products.service';

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

type Field = 'name' | 'description' | 'price';

@Component({
  selector: 'drinkit-edit-product-page',
  imports: [ReactiveFormsModule, AdminHeader, RouterLink],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a stale message from one
  // product's screen to the next one opened.
  providers: [EditProductStore],
  styleUrl: './edit-product.page.scss',
  templateUrl: './edit-product.page.html',
})
export class EditProductPage {
  protected readonly store = inject(EditProductStore);

  /** Both from the path, bound by the router. */
  readonly venueSlug = input.required<string>();

  readonly id = input.required<string>();

  /**
   * The venue's whole menu, filtered here to the one being corrected. There is
   * no endpoint that serves a single product on its own, and adding one to
   * save a listing that already fits in one response would be a request
   * nobody needs — the same trade the search on the listing makes.
   */
  protected readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  /** What the API last said about it: the listing at first, then each answer. */
  protected readonly product = computed<Product | undefined>(
    () =>
      this.store.updated() ??
      (this.products.hasValue() ? this.products.value() : []).find((row) => row.id === this.id()),
  );

  /** Loaded, and nothing here has that id. Not the same as still loading. */
  protected readonly isMissing = computed(
    () => this.products.hasValue() && this.product() === undefined,
  );

  protected readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [trimmedMinLength(1), Validators.maxLength(NAME_MAX_LENGTH)],
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(DESCRIPTION_MAX_LENGTH)],
    }),
    price: new FormControl<number | null>(null, { validators: [Validators.required, positive] }),
  });

  /**
   * Whether the form has already been submitted once. Errors stay hidden until
   * then, same as the new product form.
   */
  private readonly attempted = signal(false);

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

  /**
   * How many arrived, not the new total: the API adds them to what is left.
   * Empty until somebody types, so a 0 left in place by accident cannot be sent.
   */
  protected readonly units = new FormControl<number | null>(null, {
    validators: [Validators.required, Validators.min(1), wholeNumber],
  });

  private readonly unitsTyped = toSignal(this.units.valueChanges, { initialValue: null });

  private readonly stockAttempted = signal(false);

  protected readonly unitsError = computed(() => {
    this.unitsTyped();

    if (!this.stockAttempted() || this.store.isBusy()) return null;

    return this.units.invalid ? 'Ingresá un número entero mayor a cero.' : null;
  });

  /** The picture on screen: the product's own, or the placeholder while it has none. */
  protected readonly photoUrl = computed(() => this.product()?.imageUrl ?? PRODUCT_PLACEHOLDER);

  /**
   * Checked here, before a byte goes up: the API would answer 415 and 413 to
   * these, but only after the whole file crossed the venue's connection.
   */
  protected readonly photoProblem = signal<string | null>(null);

  constructor() {
    // What is there today fills the form once it arrives, so an administrator
    // sees what they are changing rather than a blank one.
    effect(() => {
      const product = this.product();

      if (product !== undefined && !this.form.dirty)
        this.form.setValue({
          name: product.name,
          description: product.description ?? '',
          price: product.price,
        });
    });

    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template.
    effect(() => {
      if (this.store.isBusy()) {
        this.form.disable();
        this.units.disable();
      } else {
        this.form.enable();
        this.units.enable();
      }
    });
  }

  /**
   * Reads the control rather than re-deriving the rule, so the message and the
   * validator can never disagree.
   */
  private errorOf(field: Field, message: string): string | null {
    this.typed();

    if (!this.attempted() || this.store.isBusy()) return null;

    return this.form.controls[field].invalid ? message : null;
  }

  protected save(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.store.isBusy()) return;

    const { name, description, price } = this.form.getRawValue();

    // Validators.required already rejected this above; this is only what the
    // compiler needs to see.
    if (price === null) return;

    this.store.update(this.id(), {
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
      price,
    });
  }

  protected choosePhoto(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    const product = this.product();

    // Cleared so that choosing the same file again — after a failure, say —
    // still fires a change.
    input.value = '';

    if (file === undefined || product === undefined) return;

    if (!IMAGE_TYPES.includes(file.type)) {
      this.photoProblem.set('La foto tiene que ser JPEG, PNG o WebP.');
      return;
    }

    if (file.size > IMAGE_MAX_BYTES) {
      this.photoProblem.set('La foto no puede pesar más de 5 MB.');
      return;
    }

    this.photoProblem.set(null);
    this.store.uploadImage(product, file);
  }

  protected restock(): void {
    this.stockAttempted.set(true);

    if (this.units.invalid || this.store.isBusy()) return;

    const units = this.units.value;

    // Validators.required already rejected this above; this is only what the
    // compiler needs to see.
    if (units === null) return;

    this.store.restock(this.id(), units);
    this.units.reset();
    this.stockAttempted.set(false);
  }

  protected deactivate(): void {
    this.store.deactivate(this.id());
  }
}
