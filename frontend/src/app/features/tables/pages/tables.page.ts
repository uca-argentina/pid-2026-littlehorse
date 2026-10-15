import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { slowLoading } from '../../../shared/loading/slow-loading';
import { TABLES_URL, TablesService } from '../tables.service';
import type { VipTable } from '../tables.service';

/** Why the listing is not on screen, or why a change did not go through. */
type Failure = 'none' | 'forbidden' | 'unreachable';

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

  protected readonly failure = computed<Failure>(() => {
    const error = this.tables.error();

    if (error === undefined) return 'none';

    return failureOf(error);
  });

  /** Whether a change is in flight, so a double tap cannot send it twice. */
  protected readonly changing = signal(false);

  /** Why the last put-away or reactivation did not go through. */
  protected readonly changeFailure = signal<Failure>('none');

  /** "10 personas · Al lado de la cabina", or just the people when it has no location. */
  protected detailsOf(table: VipTable): string {
    const people = `${table.capacity} ${table.capacity === 1 ? 'persona' : 'personas'}`;

    return table.location ? `${people} · ${table.location}` : people;
  }

  protected putAway(table: VipTable): void {
    this.change(this.service.deactivate(table.id));
  }

  protected bringBack(table: VipTable): void {
    this.change(this.service.reactivate(table.id));
  }

  private change(request: ReturnType<TablesService['deactivate']>): void {
    if (this.changing()) return;

    this.changing.set(true);
    this.changeFailure.set('none');

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      // The answer is the updated table: swapping that one row in spares a
      // request over the venue's crowded signal and the "Cargando" flash.
      next: (updated) => {
        this.changing.set(false);
        this.tables.value.update((tables) =>
          tables?.map((table) => (table.id === updated.id ? updated : table)),
        );
      },
      error: (error: unknown) => {
        this.changing.set(false);
        this.changeFailure.set(failureOf(error));
      },
    });
  }
}

/**
 * The two failures are worth telling apart: one goes away when the signal
 * comes back, and the other never will, however many times they retry.
 */
function failureOf(error: unknown): Failure {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
}
