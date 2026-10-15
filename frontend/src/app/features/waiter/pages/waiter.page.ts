import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { StaffHeader } from '../../../shared/staff-header/staff-header';
import { WaiterChannel } from '../waiter-channel';
import { WAITER_TABLES_URL, WaiterService } from '../waiter.service';
import type { WaiterTable } from '../waiter.service';

/** What the last tap on "La llevo yo" came to, when it did not simply work. */
type TapOutcome = 'none' | 'beatenToIt' | 'failed';

/** How often the "hace cuánto" figures are recomputed, in milliseconds. */
const CLOCK_MS = 15_000;

/**
 * The waiter's phone (US-43), from design/wireframes/MozoEntregas: the tables
 * whose order is ready, split in the ones this waiter took, the ones nobody
 * has, and the ones another carries. Nobody assigns tables (§8.3): whoever
 * taps first has it.
 *
 * What keeps two phones looking at the same thing is the hub, not polling: it
 * says the list changed and this loads it again.
 */
@Component({
  selector: 'drinkit-waiter-page',
  imports: [StaffHeader],
  styleUrl: './waiter.page.scss',
  templateUrl: './waiter.page.html',
})
export class WaiterPage {
  private readonly service = inject(WaiterService);

  private readonly destroyRef = inject(DestroyRef);

  /** Hears when what is ready to carry changed, so the list reloads on its own. */
  protected readonly channel = inject(WaiterChannel);

  readonly venueSlug = input.required<string>();

  protected readonly tables = httpResource<WaiterTable[]>(() => WAITER_TABLES_URL);

  /** US-35, criterion 4: signed in as a waiter, but not in tonight's crew. */
  protected readonly notTonight = computed(
    () => problemTypeOf(this.tables.error()) === ProblemTypes.notInTonightsCrew,
  );

  protected readonly unreachable = computed(
    () => this.tables.error() !== undefined && !this.notTonight(),
  );

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  private readonly everything = computed<WaiterTable[]>(() =>
    this.tables.hasValue() ? this.tables.value() : [],
  );

  /** The ones this waiter took, the oldest take first. */
  protected readonly mine = computed(() => this.everything().filter((table) => table.takenByMe));

  /** Nobody has them: the one that has waited longest comes first, as the API sends them. */
  protected readonly free = computed(() =>
    this.everything().filter((table) => table.takenByName === null),
  );

  protected readonly others = computed(() =>
    this.everything().filter((table) => table.takenByName !== null && !table.takenByMe),
  );

  protected readonly isEmpty = computed(
    () => this.tables.hasValue() && this.everything().length === 0,
  );

  /** The order being taken, so a double tap cannot send it twice. */
  protected readonly taking = signal<string | null>(null);

  protected readonly outcome = signal<TapOutcome>('none');

  /** What "hace cuánto" is measured against. A signal, so the figures keep moving. */
  private readonly now = signal(Date.now());

  constructor() {
    this.channel.connect(() => this.tables.reload());

    const clock = setInterval(() => this.now.set(Date.now()), CLOCK_MS);

    this.destroyRef.onDestroy(() => {
      clearInterval(clock);
      this.channel.disconnect();
    });
  }

  protected take(table: WaiterTable): void {
    if (this.taking() !== null) return;

    this.taking.set(table.orderId);
    this.outcome.set('none');

    this.service
      .take(table.orderId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.taking.set(null);
          this.tables.reload();
        },
        error: (error: unknown) => {
          this.taking.set(null);
          this.outcome.set(isBeatenToIt(error) ? 'beatenToIt' : 'failed');

          // Somebody else got there first: the list says whose it is.
          if (isBeatenToIt(error)) this.tables.reload();
        },
      });
  }

  /** "9 min", or "40 seg" under a minute: how long ago something happened. */
  protected since(moment: string | null): string {
    if (moment === null) return '';

    const seconds = Math.max(0, Math.floor((this.now() - Date.parse(moment)) / 1000));

    return seconds < 60 ? `${seconds} seg` : `${Math.floor(seconds / 60)} min`;
  }
}

/** The order is there and somebody has it: another waiter, or another save in the same instant. */
function isBeatenToIt(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return false;

  const type = problemTypeOf(error);

  return type === ProblemTypes.waiterAlreadyTaken || type === ProblemTypes.orderChangedMeanwhile;
}
