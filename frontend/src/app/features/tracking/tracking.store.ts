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
 * Three of these come from the same 404 and are worth telling apart. 'nowhere'
 * is a link that leads to no order and never did, so asking again is pointless.
 * 'over' is the same answer arriving about an order that was on screen a moment
 * ago: the API stops showing an order once it is handed over or cancelled, so
 * this is the journey ending, not a broken link. 'unreachable' is the venue's
 * wifi dropping, where the order is fine and asking again is the whole answer.
 */
export type TrackingStatus = 'starting' | 'following' | 'unreachable' | 'nowhere' | 'over';

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

  /** Whether there is anything left to ask about. Nobody outside needs this:
   * the screen draws the status, and this is only what stops the timer. */
  private readonly isOver = computed(() => this.state() === 'nowhere' || this.state() === 'over');

  constructor() {
    // Registered once, here, rather than on every follow: there is one store per
    // screen and one timer at a time, and hanging a callback off each call left
    // the older ones pointing at a handle that stopping had already replaced.
    this.destroyRef.onDestroy(() => this.stop());
  }

  /** Starts watching one order, and keeps watching until there is no point. */
  follow(venueSlug: string, code: string, token: string): void {
    this.where = { venueSlug, code, token };

    // The screen is left asking on a timer rather than on a stream of its own,
    // so nothing accumulates: one interval, replaced if it is asked to follow
    // again and cleared when the component dies. Two of them would drift apart
    // and turn one round of asking into two.
    this.stop();
    this.timer = setInterval(() => this.askAgain(), this.every);

    // Last, so that an order already over stops the timer that was just set
    // instead of leaving it running for a round.
    this.askAgain();
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
    if (this.isOver()) return this.stop();
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
        },
        error: (error: unknown) => {
          this.asking = false;

          if (!theLinkLeadsNowhere(error)) return this.state.set('unreachable');

          // Having shown the order once is what tells the two apart: the same
          // 404 means the journey ended if it was on screen, and that the link
          // never led anywhere if it was not.
          this.state.set(this.known() === null ? 'nowhere' : 'over');
          this.stop();
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
 * same on purpose — telling them apart would confirm to somebody working
 * through codes that one of them exists.
 */
function theLinkLeadsNowhere(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}
