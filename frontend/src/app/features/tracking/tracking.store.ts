import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TrackingService } from './tracking.service';
import type { TrackedOrder } from './tracking.service';

/**
 * How often the screen asks where the order is.
 *
 * Criterion 3 gives it five seconds to notice a change, and three leaves room
 * for a request to be slow on the wifi of a packed venue. It is asking rather
 * than being told because SignalR is a later sprint — the criterion is met
 * either way, and this costs an afternoon instead of a day.
 */
export const TRACKING_INTERVAL_MS = new InjectionToken<number>('TRACKING_INTERVAL_MS', {
  providedIn: 'root',
  factory: () => 3000,
});

/**
 * What the screen is doing.
 *
 * 'nowhere' and 'unreachable' are worth telling apart: the first means the link
 * leads to no order and never will, so asking again is pointless; the second
 * means the venue's wifi dropped, the order is fine, and asking again is the
 * whole answer.
 */
export type TrackingStatus = 'starting' | 'following' | 'unreachable' | 'nowhere';

/** The statuses nobody is waiting on any more. Mirrors OrderStatuses.IsFinished. */
const FINISHED = ['Delivered', 'Canceled'];

@Injectable()
export class TrackingStore {
  private readonly tracking = inject(TrackingService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly every = inject(TRACKING_INTERVAL_MS);

  private readonly state = signal<TrackingStatus>('starting');

  private readonly known = signal<TrackedOrder | null>(null);

  private asking = false;

  private timer: ReturnType<typeof setInterval> | null = null;

  private where: { venueSlug: string; code: string; token: string } | null = null;

  readonly status = this.state.asReadonly();

  /** The last thing the server said. Kept through a dropped connection. */
  readonly order = this.known.asReadonly();

  readonly isOver = computed(() => {
    const status = this.known()?.status;

    return status !== undefined && FINISHED.includes(status);
  });

  /** Starts watching one order, and keeps watching until there is no point. */
  follow(venueSlug: string, code: string, token: string): void {
    this.where = { venueSlug, code, token };

    this.askAgain();

    // The screen is left asking on a timer rather than on a stream of its own,
    // so nothing accumulates: one interval, cleared when the component dies.
    this.timer = setInterval(() => this.askAgain(), this.every);

    this.destroyRef.onDestroy(() => this.stop());
  }

  /**
   * Asks once, if there is any point in asking.
   *
   * Three reasons there might not be: the order is over, the link leads
   * nowhere, or the phone is in a pocket with the screen off — twenty requests
   * a minute for something nobody is reading is battery somebody needs for the
   * rest of the night. And never two at once, so a slow connection does not
   * pile them up.
   */
  askAgain(): void {
    if (this.where === null || this.asking) return;
    if (this.isOver() || this.state() === 'nowhere') return this.stop();
    if (document.visibilityState === 'hidden') return;

    this.asking = true;

    this.tracking
      .follow(this.where.venueSlug, this.where.code, this.where.token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (order) => {
          this.asking = false;
          this.known.set(order);
          this.state.set('following');

          if (this.isOver()) this.stop();
        },
        error: (error: unknown) => {
          this.asking = false;
          this.state.set(theLinkLeadsNowhere(error) ? 'nowhere' : 'unreachable');

          if (this.state() === 'nowhere') this.stop();
        },
      });
  }

  private stop(): void {
    if (this.timer !== null) clearInterval(this.timer);

    this.timer = null;
  }
}

/**
 * A 404 is every way of not getting in: a wrong token, a code nobody has,
 * another venue's order, one already handed over. The API answers them all the
 * same on purpose, and so does the screen.
 */
function theLinkLeadsNowhere(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}
