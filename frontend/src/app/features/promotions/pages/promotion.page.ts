import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { PRODUCTS_URL } from '../../products/products.service';
import type { Product } from '../../products/products.service';
import { PROMOTIONS_URL, PromotionsService, WEEK, clock } from '../promotions.service';
import type { Promotion } from '../promotions.service';

type Refusal = 'none' | 'productGone' | 'invalid' | 'unreachable';

/**
 * US-54: creates a promotion, or corrects one. One screen for both. Laid out
 * for a computer: the rule on the left (name, discount, days, window) and the
 * products it covers on the right.
 */
@Component({
  selector: 'drinkit-promotion-page',
  imports: [AdminHeader, RouterLink],
  styleUrls: ['../../../shared/admin-desk/admin-desk.scss', './promotion.page.scss'],
  templateUrl: './promotion.page.html',
})
export class PromotionPage {
  private readonly service = inject(PromotionsService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** The promotion being corrected, or nothing when it is a new one. */
  readonly id = input<string>();

  protected readonly week = WEEK;

  protected readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  // The API has no "one promotion" read: the list is small, and the screen
  // finds its own in it.
  private readonly promotions = httpResource<Promotion[]>(() =>
    this.id() === undefined ? undefined : PROMOTIONS_URL,
  );

  protected readonly isEditing = computed(() => this.id() !== undefined);

  protected readonly name = signal('');

  protected readonly discount = signal('');

  protected readonly days = signal<readonly number[]>([]);

  protected readonly startsAt = signal('');

  protected readonly endsAt = signal('');

  protected readonly search = signal('');

  /** In the order they were ticked. */
  protected readonly chosen = signal<readonly string[]>([]);

  protected readonly attempted = signal(false);

  protected readonly isSending = signal(false);

  protected readonly refusal = signal<Refusal>('none');

  /** Filled once, when the promotion arrives: a reload must not overwrite what is being typed. */
  private filled = false;

  /** Still on the menu, combos included, narrowed by what is typed. */
  protected readonly choices = computed(() => {
    const wanted = this.search().trim().toLocaleLowerCase('es-AR');

    return (this.products.hasValue() ? this.products.value() : [])
      .filter(
        (product) => product.isActive && product.name.toLocaleLowerCase('es-AR').includes(wanted),
      )
      .sort((one, other) => one.name.localeCompare(other.name, 'es-AR'));
  });

  protected readonly nameError = computed(() =>
    this.attempted() && this.name().trim() === '' ? 'Ponele un nombre.' : null,
  );

  protected readonly discountError = computed(() => {
    const percent = Number(this.discount());

    return this.attempted() && !(Number.isInteger(percent) && percent >= 1 && percent <= 99)
      ? 'El descuento tiene que ser un número entero de 1 a 99.'
      : null;
  });

  protected readonly daysError = computed(() =>
    this.attempted() && this.days().length === 0 ? 'Elegí al menos un día.' : null,
  );

  protected readonly windowError = computed(() => {
    if (!this.attempted()) return null;
    if (this.startsAt() === '' || this.endsAt() === '')
      return 'Elegí la hora de inicio y la de fin.';

    return this.startsAt() === this.endsAt() ? 'El inicio y el fin no pueden ser iguales.' : null;
  });

  protected readonly productsError = computed(() =>
    this.attempted() && this.chosen().length === 0 ? 'Elegí al menos un producto.' : null,
  );

  constructor() {
    effect(() => {
      const id = this.id();

      if (id === undefined || this.filled || !this.promotions.hasValue()) return;

      const promotion = this.promotions.value().find((candidate) => candidate.id === id);

      if (promotion === undefined) return;

      this.filled = true;
      this.name.set(promotion.name);
      this.discount.set(String(promotion.discountPercent));
      this.days.set(promotion.days);
      this.startsAt.set(clock(promotion.startsAt));
      this.endsAt.set(clock(promotion.endsAt));
      this.chosen.set(promotion.productIds);
    });
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected toggleDay(day: number): void {
    this.days.update((days) =>
      days.includes(day) ? days.filter((current) => current !== day) : [...days, day],
    );
  }

  protected toggleProduct(productId: string): void {
    this.chosen.update((ids) =>
      ids.includes(productId)
        ? ids.filter((current) => current !== productId)
        : [...ids, productId],
    );
  }

  protected submit(): void {
    this.attempted.set(true);

    if (
      this.isSending() ||
      this.nameError() !== null ||
      this.discountError() !== null ||
      this.daysError() !== null ||
      this.windowError() !== null ||
      this.productsError() !== null
    )
      return;

    this.isSending.set(true);
    this.refusal.set('none');

    // The API reads a time of day with seconds; the input gives "23:00".
    const data = {
      name: this.name().trim(),
      discountPercent: Number(this.discount()),
      days: [...this.days()],
      startsAt: `${this.startsAt()}:00`,
      endsAt: `${this.endsAt()}:00`,
      productIds: [...this.chosen()],
    };
    const id = this.id();

    (id === undefined ? this.service.create(data) : this.service.update(id, data))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSending.set(false);
          void this.router.navigate([this.venueSlug(), 'staff', 'promotions']);
        },
        error: (error: unknown) => {
          this.isSending.set(false);
          this.refusal.set(reasonFor(error));
        },
      });
  }
}

function reasonFor(error: unknown): Refusal {
  const type = problemTypeOf(error);

  if (type === ProblemTypes.promotionProductNotFound) return 'productGone';

  return type?.startsWith('urn:drinkit:problem:promotion:') === true ? 'invalid' : 'unreachable';
}
