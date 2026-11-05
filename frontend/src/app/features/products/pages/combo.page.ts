import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { concatMap, of } from 'rxjs';
import type { Observable } from 'rxjs';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { formatPrice } from '../../../shared/money/price';
import { CATEGORIES_URL } from '../../categories/categories.service';
import type { Category } from '../../categories/categories.service';
import { IMAGE_MAX_BYTES, IMAGE_TYPES, PRODUCTS_URL, ProductsService } from '../products.service';
import type { ComboPart, Product } from '../products.service';

/** One line of the components table, with what the screen reads beside it. */
interface PartRow {
  readonly productId: string;
  readonly name: string;
  readonly quantity: number;
  readonly unitPrice: number;
}

type Refusal = 'none' | 'nameTaken' | 'partGone' | 'comboInside' | 'imageBad' | 'unreachable';

/** The smallest combo the domain accepts: "varios productos", so two units. */
const MIN_UNITS = 2;

/**
 * US-50: creates a combo, or corrects one. One screen for both, because a
 * combo's name, price and components are read and changed together. Laid out
 * for a computer: the data on the left and the components table on the right.
 */
@Component({
  selector: 'drinkit-combo-page',
  imports: [AdminHeader, RouterLink],
  styleUrls: ['../../../shared/admin-desk/admin-desk.scss', './combo.page.scss'],
  templateUrl: './combo.page.html',
})
export class ComboPage {
  private readonly productsService = inject(ProductsService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** The combo being corrected, or nothing when it is a new one. */
  readonly id = input<string>();

  protected readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  protected readonly categories = httpResource<Category[]>(() => CATEGORIES_URL);

  protected readonly isEditing = computed(() => this.id() !== undefined);

  protected readonly name = signal('');

  protected readonly description = signal('');

  protected readonly price = signal('');

  protected readonly categoryId = signal('');

  private readonly parts = signal<readonly ComboPart[]>([]);

  /** The one to add next, from the selector over the table. */
  protected readonly picked = signal('');

  protected readonly pickedQuantity = signal('1');

  private readonly image = signal<File | null>(null);

  protected readonly imageName = computed(() => this.image()?.name ?? null);

  protected readonly attempted = signal(false);

  protected readonly isSending = signal(false);

  protected readonly refusal = signal<Refusal>('none');

  private readonly everything = computed(() =>
    this.products.hasValue() ? this.products.value() : [],
  );

  /** Filled once, when the combo arrives: a reload must not overwrite what is being typed. */
  private filled = false;

  /** Sold from its own stock and still on the menu: what a combo can be made of. */
  protected readonly choices = computed(() => {
    const taken = new Set(this.parts().map((part) => part.productId));

    return this.everything()
      .filter((product) => product.isActive && !product.isCombo && !taken.has(product.id))
      .sort((one, other) => one.name.localeCompare(other.name, 'es-AR'));
  });

  protected readonly rows = computed<PartRow[]>(() =>
    this.parts().map((part) => {
      const product = this.everything().find((candidate) => candidate.id === part.productId);

      return {
        productId: part.productId,
        name: product?.name ?? '',
        quantity: part.quantity,
        unitPrice: product?.price ?? 0,
      };
    }),
  );

  /** What it would cost bought separately, so a combo that charges more gets noticed. */
  protected readonly separately = computed(() =>
    this.rows().reduce((sum, row) => sum + row.unitPrice * row.quantity, 0),
  );

  protected readonly separatelyText = computed(() => formatPrice(this.separately()));

  protected readonly nameError = computed(() =>
    this.attempted() && this.name().trim() === '' ? 'Ponele un nombre.' : null,
  );

  protected readonly priceError = computed(() =>
    this.attempted() && !(Number(this.price()) > 0) ? 'El precio tiene que ser mayor a 0.' : null,
  );

  protected readonly categoryError = computed(() =>
    this.attempted() && this.categoryId() === '' ? 'Elegí una categoría.' : null,
  );

  protected readonly partsError = computed(() =>
    this.attempted() && this.parts().reduce((sum, part) => sum + part.quantity, 0) < MIN_UNITS
      ? 'Un combo necesita al menos 2 unidades en total.'
      : null,
  );

  protected readonly acceptedImages = IMAGE_TYPES.join(',');

  constructor() {
    effect(() => {
      const id = this.id();

      if (id === undefined || this.filled) return;

      const combo = this.everything().find((product) => product.id === id);

      if (combo === undefined) return;

      this.filled = true;
      this.name.set(combo.name);
      this.description.set(combo.description ?? '');
      this.price.set(String(combo.price));
      this.categoryId.set(combo.categoryId);
      this.parts.set(combo.components);
    });
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected addPart(): void {
    const quantity = Number(this.pickedQuantity());

    if (this.picked() === '' || !Number.isInteger(quantity) || quantity < 1) return;

    this.parts.update((parts) => [...parts, { productId: this.picked(), quantity }]);
    this.picked.set('');
    this.pickedQuantity.set('1');
  }

  protected removePart(productId: string): void {
    this.parts.update((parts) => parts.filter((part) => part.productId !== productId));
  }

  protected setQuantity(productId: string, event: Event): void {
    const quantity = Number(this.read(event));

    if (!Number.isInteger(quantity) || quantity < 1) return;

    this.parts.update((parts) =>
      parts.map((part) => (part.productId === productId ? { ...part, quantity } : part)),
    );
  }

  protected pickImage(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    const fits = file === null || (IMAGE_TYPES.includes(file.type) && file.size <= IMAGE_MAX_BYTES);

    this.refusal.set(fits ? 'none' : 'imageBad');
    this.image.set(fits ? file : null);
  }

  protected submit(): void {
    this.attempted.set(true);

    if (
      this.isSending() ||
      this.nameError() !== null ||
      this.priceError() !== null ||
      this.categoryError() !== null ||
      this.partsError() !== null
    )
      return;

    this.isSending.set(true);
    this.refusal.set('none');

    this.saved()
      .pipe(
        concatMap((combo) => {
          const image = this.image();

          return image === null
            ? of(combo)
            : this.productsService.uploadImage(combo.id, image).pipe(concatMap(() => of(combo)));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.isSending.set(false);
          void this.router.navigate([this.venueSlug(), 'staff', 'products']);
        },
        error: (error: unknown) => {
          this.isSending.set(false);
          this.refusal.set(reasonFor(error));
        },
      });
  }

  protected deactivate(): void {
    const id = this.id();

    if (id === undefined || this.isSending()) return;

    this.isSending.set(true);
    this.productsService
      .deactivate(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSending.set(false);
          void this.router.navigate([this.venueSlug(), 'staff', 'products']);
        },
        error: () => {
          this.isSending.set(false);
          this.refusal.set('unreachable');
        },
      });
  }

  private saved(): Observable<Product> {
    const data = {
      name: this.name().trim(),
      description: this.description().trim() === '' ? null : this.description().trim(),
      price: Number(this.price()),
      categoryId: this.categoryId(),
    };
    const components = [...this.parts()];
    const id = this.id();

    if (id === undefined) {
      return this.productsService.createCombo({ ...data, imageUrl: null, components });
    }

    return this.productsService
      .update(id, data)
      .pipe(concatMap(() => this.productsService.replaceComponents(id, components)));
  }
}

function reasonFor(error: unknown): Refusal {
  switch (problemTypeOf(error)) {
    case ProblemTypes.productNameTaken:
      return 'nameTaken';
    case ProblemTypes.productComboComponentNotFound:
      return 'partGone';
    case ProblemTypes.productComboInsideCombo:
      return 'comboInside';
    default:
      return 'unreachable';
  }
}
