import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trimmedMinLength } from '../../../shared/forms/trimmed-min-length';
import { IMAGE_MAX_BYTES, IMAGE_TYPES } from '../products.service';

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

/** What the form hands over once it is valid, already trimmed. */
export interface ProductFormValue {
  readonly name: string;
  readonly description: string | null;
  readonly price: number;
  readonly stock: number;
  readonly photo: File | null;
  readonly isAvailable: boolean;
}

/**
 * The product form, shared by the screen that creates a product and the one
 * that corrects it, so the two can never drift apart. It knows nothing about
 * the API: it validates, and hands over what was typed.
 */
@Component({
  selector: 'drinkit-product-form',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './product-form.scss',
  templateUrl: './product-form.html',
})
export class ProductForm {
  readonly venueSlug = input.required<string>();

  readonly heading = input.required<string>();

  readonly submitLabel = input.required<string>();

  readonly sendingLabel = input.required<string>();

  readonly isSending = input(false);

  /** Nothing left to submit: the way out replaces the actions. */
  readonly isFinished = input(false);

  readonly submitted = output<ProductFormValue>();

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

  /**
   * The photo lives outside the reactive form: a file input cannot be bound
   * to a FormControl, and there is nothing to type into it anyway.
   */
  protected readonly photo = signal<File | null>(null);

  /**
   * An object URL for the chosen file, so the administrator sees the picture
   * itself and not a file name. Object URLs hold the file in memory until
   * they are released, so every one made here is revoked when replaced,
   * removed, or when the screen goes away.
   */
  protected readonly photoPreview = signal<string | null>(null);

  /**
   * Checked here, before a byte goes up: the API would answer 415 and 413 to
   * these, but only after the whole file crossed the venue's connection.
   */
  protected readonly photoError = computed(() => {
    const file = this.photo();

    if (file === null || !this.attempted()) return null;
    if (!IMAGE_TYPES.includes(file.type)) return 'La foto tiene que ser JPEG, PNG o WebP.';
    if (file.size > IMAGE_MAX_BYTES) return 'La foto no puede pesar más de 5 MB.';

    return null;
  });

  /** Dragging over the zone: what shows the drop will land. */
  protected readonly dragging = signal(false);

  protected readonly isAvailable = signal(true);

  constructor() {
    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template.
    effect(() => {
      if (this.isSending()) this.form.disable();
      else this.form.enable();
    });

    inject(DestroyRef).onDestroy(() => this.setPhoto(null));
  }

  /**
   * Reads the control rather than re-deriving the rule, so the message and the
   * validator can never disagree. Touching the value signal first is what makes
   * this recompute: the control itself is not reactive.
   */
  private errorOf(field: Field, message: string): string | null {
    this.typed();

    if (!this.attempted() || this.isSending()) return null;

    return this.form.controls[field].invalid ? message : null;
  }

  protected choosePhoto(event: Event): void {
    this.setPhoto((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected dragOver(event: DragEvent): void {
    // Without this the browser opens the file instead of handing it over.
    event.preventDefault();
    this.dragging.set(true);
  }

  protected dragLeave(): void {
    this.dragging.set(false);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.setPhoto(event.dataTransfer?.files[0] ?? null);
  }

  protected removePhoto(): void {
    this.setPhoto(null);
  }

  protected toggleAvailability(): void {
    this.isAvailable.update((available) => !available);
  }

  /** The one place a preview is made or released, so none is ever leaked. */
  private setPhoto(file: File | null): void {
    const previous = this.photoPreview();

    if (previous !== null) URL.revokeObjectURL(previous);

    this.photo.set(file);
    this.photoPreview.set(file === null ? null : URL.createObjectURL(file));
  }

  private photoIsInvalid(): boolean {
    const file = this.photo();

    return file !== null && (!IMAGE_TYPES.includes(file.type) || file.size > IMAGE_MAX_BYTES);
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.photoIsInvalid() || this.isSending()) return;

    const { name, description, price, stock } = this.form.getRawValue();

    // Validators.required already rejected these above; this is only what the
    // compiler needs to see.
    if (price === null || stock === null) return;

    this.submitted.emit({
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
      price,
      stock,
      photo: this.photo(),
      isAvailable: this.isAvailable(),
    });
  }
}
