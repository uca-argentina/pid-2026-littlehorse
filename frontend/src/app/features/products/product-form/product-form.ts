import { httpResource } from '@angular/common/http';
import type { ElementRef } from '@angular/core';
import {
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CATEGORIES_URL } from '../../categories/categories.service';
import type { Category } from '../../categories/categories.service';
import { AuditNote } from '../../../shared/audit-note/audit-note';
import { trimmedMinLength } from '../../../shared/forms/trimmed-min-length';
import { IMAGE_MAX_BYTES, IMAGE_TYPES } from '../products.service';
import type { Product } from '../products.service';

/** Kept in step with Product's own rules in the domain. */
const NAME_MAX_LENGTH = 80;

const DESCRIPTION_MAX_LENGTH = 200;

/** Strictly above zero: a free product is a mistake, not an offer. */
function positive(control: AbstractControl): ValidationErrors | null {
  return typeof control.value === 'number' && control.value > 0 ? null : { positive: true };
}

type Field = 'name' | 'description' | 'price' | 'stock' | 'categoryId';

/** Units that arrived, or the real total when it was loaded wrong. */
type StockMode = 'add' | 'set';

/** What the form hands over once it is valid, already trimmed. */
export interface ProductFormValue {
  readonly name: string;
  readonly description: string | null;
  /** US-14: asked at creation, and correctable from the same field afterwards. */
  readonly categoryId: string;
  readonly price: number;
  /** For a new product, how many there are. When correcting one, how much it moves; 0 is none. */
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
  imports: [AuditNote, ReactiveFormsModule, RouterLink],
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

  /** The product being corrected. Without one, the form creates a new product. */
  readonly product = input<Product | undefined>(undefined);

  /**
   * How many adjustments already reached the API. Saving again after a
   * failure further down must not send them twice: they move the stock, they
   * do not set it.
   */
  readonly stockAdjusted = input(0);

  readonly submitted = output<ProductFormValue>();

  protected readonly isEditing = computed(() => this.product() !== undefined);

  /**
   * When correcting, the stock is shown and not offered as a field: a total
   * typed over it would erase the sales made while the screen was open.
   * "Ajustar" opens it, on the units that arrived or on the real total.
   * Declared before the form: its stock rule reads the mode as it is built.
   */
  protected readonly isAdjusting = signal(false);

  protected readonly stockMode = signal<StockMode>('add');

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
      validators: [(control) => this.stockRule(control)],
    }),
    // US-14: obligatoria, at creation and while correcting one alike.
    categoryId: new FormControl<string | null>(null, { validators: [Validators.required] }),
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

  /**
   * The venue's own categories, which is what the administrator picks from: a
   * list that changes with the button that adds one, so it is asked for and
   * not written into the screen. Loading, failing and being empty each get
   * their own line in the template.
   */
  protected readonly categories = httpResource<Category[]>(() => CATEGORIES_URL);

  protected readonly categoryError = computed(() =>
    this.errorOf('categoryId', 'Elegí una categoría.'),
  );

  protected readonly stockError = computed(() => {
    if (!this.isEditing())
      return this.errorOf('stock', 'El stock tiene que ser un número entero, cero o más.');

    // Checked while typing, unlike the rest: a wrong number next to the total
    // it would lead to reads as accepted until the save is refused.
    this.typed();
    const typed = this.form.controls.stock;

    if (this.isSending() || !(typed.dirty || this.attempted())) return null;
    if (!typed.invalid) return null;

    return this.stockMode() === 'add'
      ? 'Poné un número entero mayor a cero.'
      : 'El stock real tiene que ser un número entero, cero o más.';
  });

  /** A whole number is in the field, right or wrong. */
  protected readonly hasStockTyped = computed(() => Number.isInteger(this.typed().stock));

  /**
   * How much the stock moves, whichever way it was typed: what arrived, or the
   * real total minus what there is. 0 while the field is empty or wrong. This,
   * and never the total, is what gets sent.
   */
  private readonly stockChange = computed(() => {
    const typed = this.typed().stock;
    const now = this.product()?.stock ?? 0;

    if (typeof typed !== 'number' || !Number.isInteger(typed)) return 0;
    if (this.stockMode() === 'add') return typed > 0 ? typed : 0;

    return typed >= 0 ? typed - now : 0;
  });

  /** The total the product ends up with, shown before saving. */
  protected readonly stockAfter = computed(() => (this.product()?.stock ?? 0) + this.stockChange());

  /** "−180" or "+12", as the correction shows it. */
  protected readonly stockDifference = computed(() => {
    const change = this.stockChange();

    return change < 0 ? `−${-change}` : `+${change}`;
  });

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

  private readonly stockField = viewChild<ElementRef<HTMLInputElement>>('stockField');

  private readonly injector = inject(Injector);

  /**
   * Running out locks the switch, same as in the listing: the domain refuses
   * to turn on a product with nothing to sell. An adjustment that leaves some
   * unlocks it, because the stock is saved before the switch.
   */
  protected readonly canSwitch = computed(() => {
    const product = this.product();

    return product === undefined || !product.isSoldOut || this.stockAfter() > 0;
  });

  constructor() {
    // Filled once, when the product arrives, and never again: refilling on a
    // later answer from the API would wipe what is being typed.
    let filled = false;

    effect(() => {
      const product = this.product();

      if (product === undefined || filled) return;

      filled = true;
      this.form.setValue({
        name: product.name,
        description: product.description ?? '',
        price: product.price,
        stock: null,
        categoryId: product.categoryId,
      });
      this.isAvailable.set(product.isAvailable);
    });

    effect(() => {
      if (this.stockAdjusted() > 0) this.closeAdjust();
    });

    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template.
    effect(() => {
      if (this.isSending()) this.form.disable();
      else this.form.enable();
    });

    inject(DestroyRef).onDestroy(() => this.setPhoto(null));
  }

  /**
   * A new product needs a stock, zero included. A correction does not: empty
   * leaves the stock alone. What arrived has to be one or more; a real total,
   * zero or more.
   */
  private stockRule(control: AbstractControl): ValidationErrors | null {
    const typed: unknown = control.value;

    if (this.isEditing() && typed === null) return null;

    const least = this.isEditing() && this.stockMode() === 'add' ? 1 : 0;

    return Number.isInteger(typed) && (typed as number) >= least ? null : { stock: true };
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

  protected openAdjust(): void {
    this.isAdjusting.set(true);
    this.chooseStockMode('add');
  }

  /** A number typed for one mode means something else in the other, so it goes. */
  protected chooseStockMode(mode: StockMode): void {
    this.stockMode.set(mode);
    this.form.controls.stock.reset(null);

    // The field only exists once it is drawn; one tap should be enough to
    // start typing on a tablet.
    afterNextRender(() => this.stockField()?.nativeElement.focus(), { injector: this.injector });
  }

  protected closeAdjust(): void {
    this.isAdjusting.set(false);
    this.form.controls.stock.reset(null);
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

    // The stock rule reads the mode, which may have arrived after the value.
    this.form.controls.stock.updateValueAndValidity();

    if (this.form.invalid || this.photoIsInvalid() || this.isSending()) return;

    const { name, description, price, stock, categoryId } = this.form.getRawValue();

    // Validators.required already rejected these above; this is only what the
    // compiler needs to see.
    if (price === null || categoryId === null) return;

    this.submitted.emit({
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
      categoryId,
      price,
      stock: this.isEditing() ? this.stockChange() : (stock ?? 0),
      photo: this.photo(),
      // A switch that could not be moved keeps what the product already had.
      isAvailable: this.canSwitch() ? this.isAvailable() : (this.product()?.isAvailable ?? true),
    });
  }
}
