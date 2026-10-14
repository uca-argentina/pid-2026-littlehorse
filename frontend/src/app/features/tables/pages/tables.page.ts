import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { slowLoading } from '../../../shared/loading/slow-loading';
import { TABLES_URL, TablesService } from '../tables.service';
import type { VipTable } from '../tables.service';

/** Why the listing is not on screen. */
type ListingFailure = 'none' | 'forbidden' | 'unreachable';

@Component({
  selector: 'drinkit-tables-page',
  imports: [AdminHeader, RouterLink],
  styleUrl: './tables.page.scss',
  templateUrl: './tables.page.html',
})
export class TablesPage {
  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  private readonly service = inject(TablesService);

  private readonly destroyRef = inject(DestroyRef);

  /**
   * The Resource API rather than a subscription: loading, error and value
   * arrive as signals, which is exactly the three states this screen draws.
   */
  protected readonly tables = httpResource<VipTable[]>(() => TABLES_URL);

  protected readonly isSlow = slowLoading(() => this.tables.isLoading());

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  protected readonly rows = computed<VipTable[]>(() =>
    this.tables.hasValue() ? this.tables.value() : [],
  );

  protected readonly isEmpty = computed(() => this.tables.hasValue() && this.rows().length === 0);

  /**
   * The two failures are worth telling apart: one goes away when the signal
   * comes back, and the other never will, however many times they retry.
   */
  protected readonly failure = computed<ListingFailure>(() => {
    const error = this.tables.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  /** The table being changed, so a double tap cannot send the change twice. */
  protected readonly changing = signal<string | null>(null);

  /** Set when a put-away or a reactivation did not go through. */
  protected readonly changeFailed = signal(false);

  protected putAway(table: VipTable): void {
    this.change(table, this.service.deactivate(table.id));
  }

  protected bringBack(table: VipTable): void {
    this.change(table, this.service.reactivate(table.id));
  }

  private change(table: VipTable, request: ReturnType<TablesService['deactivate']>): void {
    if (this.changing() !== null) return;

    this.changing.set(table.id);
    this.changeFailed.set(false);

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.changing.set(null);
        this.tables.reload();
      },
      error: () => {
        this.changing.set(null);
        this.changeFailed.set(true);
      },
    });
  }
}
