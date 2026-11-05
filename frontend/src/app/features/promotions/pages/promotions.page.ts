import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import type { Observable } from 'rxjs';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { PRODUCTS_URL } from '../../products/products.service';
import type { Product } from '../../products/products.service';
import { PROMOTIONS_URL, PromotionsService, WEEK, clock } from '../promotions.service';
import type { Promotion } from '../promotions.service';

/** One promotion as the table reads it. */
interface Row {
  readonly id: string;
  readonly name: string;
  readonly discount: string;
  readonly days: string;
  readonly window: string;
  readonly products: string;
  readonly isActive: boolean;
}

/**
 * US-54: every promotion of the venue, with the switch to turn each on or off.
 * Laid out for a computer, like the other administration screens.
 */
@Component({
  selector: 'drinkit-promotions-page',
  imports: [AdminHeader, RouterLink],
  styleUrls: ['../../../shared/admin-desk/admin-desk.scss'],
  templateUrl: './promotions.page.html',
})
export class PromotionsPage {
  private readonly service = inject(PromotionsService);

  private readonly destroyRef = inject(DestroyRef);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly promotions = httpResource<Promotion[]>(() => PROMOTIONS_URL);

  // Only for the names: the promotion carries ids.
  private readonly products = httpResource<Product[]>(() => PRODUCTS_URL);

  protected readonly failed = signal(false);

  protected readonly rows = computed<Row[]>(() => {
    const names = new Map(
      (this.products.hasValue() ? this.products.value() : []).map((product) => [
        product.id,
        product.name,
      ]),
    );

    return (this.promotions.hasValue() ? this.promotions.value() : []).map((promotion) => ({
      id: promotion.id,
      name: promotion.name,
      discount: `${promotion.discountPercent}%`,
      days: daysOf(promotion.days),
      window: `${clock(promotion.startsAt)} a ${clock(promotion.endsAt)}`,
      products: promotion.productIds.map((id) => names.get(id) ?? '…').join(', '),
      isActive: promotion.isActive,
    }));
  });

  protected activate(id: string): void {
    this.switchTo(this.service.activate(id));
  }

  protected deactivate(id: string): void {
    this.switchTo(this.service.deactivate(id));
  }

  private switchTo(change: Observable<Promotion>): void {
    this.failed.set(false);
    change.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.promotions.reload(),
      error: () => this.failed.set(true),
    });
  }
}

/** "Vie, Sáb" in week order, or "Todos los días". */
function daysOf(days: readonly number[]): string {
  return days.length === WEEK.length
    ? 'Todos los días'
    : WEEK.filter((entry) => days.includes(entry.day))
        .map((entry) => entry.short)
        .join(', ');
}
