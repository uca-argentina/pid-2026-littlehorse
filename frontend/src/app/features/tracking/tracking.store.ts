import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, InjectionToken, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProblemTypes } from '../../core/api/problem-types';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { TrackingChannel } from './tracking-channel';
import { TrackingService } from './tracking.service';
import type { TrackedOrder } from './tracking.service';

/**
 * How long to wait before asking again when an answer did not arrive.
 *
 * Only then: the screen does not ask on a timer any more (US-22), it asks when
 * the live link says the order moved. But a move heard whose answer never came
 * is announced by nobody again, so that one ask is retried until it gets
 * through.
 */
export const TRACKING_RETRY_MS = new InjectionToken<number>('TRACKING_RETRY_MS', {
  providedIn: 'root',
  factory: () => 5_000,
});

/**
 * What the screen is doing.
 *
 * Three of these come from the same 404 and are worth telling apart. 'nowhere'
 * is a link that leads to no order and never did, so asking again is pointless.
 * 'over' is the same answer arriving about an order that was on screen a moment
 * ago: the API stops showing an order once it is handed over, so this is the
 * journey ending, not a broken link. 'unreachable' is the venue's wifi
 * dropping — an answer that did not arrive, or the live link being down —
 * where the order is fine and what is on screen may just be old.
 */
export type TrackingStatus = 'starting' | 'following' | 'unreachable' | 'nowhere' | 'over';

/**
 * Why a cancellation did not go through (US-23): paid at the till a moment
 * before, or no answer at all.
 */
export type CancelProblem = 'alreadyPaid' | 'failed';

@Injectable()
export class TrackingStore {
  private readonly tracking = inject(TrackingService);

  private readonly channel = inject(TrackingChannel);

  private readonly destroyRef = inject(DestroyRef);

  private readonly retryIn = inject(TRACKING_RETRY_MS);

  private readonly state = signal<TrackingStatus>('starting');

  private readonly known = signal<TrackedOrder | null>(null);

  private asking = false;

  /** A move was heard while an answer was on its way, which may predate it. */
  private askedMeanwhile = false;

  private retry: ReturnType<typeof setTimeout> | null = null;

  private where: { venueSlug: string; code: string; token: string } | null = null;

  /**
   * Out of touch while the live link is down (US-22, criterion 2): nothing is
   * being heard, so what is on screen may already be old. The link says when
   * it is back, and asks again then.
   */
  readonly status = computed<TrackingStatus>(() => {
    const state = this.state();

    return state === 'following' && this.channel.state() === 'reconnecting' ? 'unreachable' : state;
  });

  /** The last thing the server said. Kept through a dropped connection. */
  readonly order = this.known.asReadonly();

  private readonly cancelingNow = signal(false);

  /** While a cancellation is on its way: the button says so and takes no second tap. */
  readonly canceling = this.cancelingNow.asReadonly();

  private readonly cancelRefusal = signal<CancelProblem | null>(null);

  readonly cancelProblem = this.cancelRefusal.asReadonly();

  /**
   * Whether there is anything left to hear about (US-22, criterion 3). A
   * canceled order still answers, so the screen can say so, but it is not
   * going anywhere any more.
   */
  private readonly isOver = computed(
    () =>
      this.state() === 'nowhere' || this.state() === 'over' || this.known()?.status === 'Canceled',
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.stop());

    // A phone in a pocket can lose the live link without noticing for a while.
    // The moment somebody looks again is the moment the screen has to be right.
    const onLookedAtAgain = (): void => {
      if (document.visibilityState === 'visible') this.askAgain();
    };
    document.addEventListener('visibilitychange', onLookedAtAgain);
    this.destroyRef.onDestroy(() =>
      document.removeEventListener('visibilitychange', onLookedAtAgain),
    );
  }

  /** Starts watching one order, and keeps watching until there is no point. */
  follow(venueSlug: string, code: string, token: string): void {
    this.where = { venueSlug, code, token };

    this.stop();
    this.channel.follow(token, () => this.askAgain());
    this.askAgain();
  }

  /**
   * Asks once, if there is any point in asking — never two at once, so a slow
   * connection does not pile them up. Asked while an answer is on its way, it
   * asks once more when that answer arrives: the answer may have been read
   * before the move this is about.
   */
  askAgain(): void {
    if (this.where === null) return;
    if (this.isOver()) return this.stop();
    if (this.asking) {
      this.askedMeanwhile = true;
      return;
    }

    this.asking = true;
    this.askedMeanwhile = false;
    this.clearRetry();

    this.tracking
      .follow(this.where.venueSlug, this.where.code, this.where.token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (order) => {
          this.asking = false;
          this.known.set(order);
          this.state.set('following');

          if (this.isOver()) return this.stop();
          if (this.askedMeanwhile) this.askAgain();
        },
        error: (error: unknown) => {
          this.asking = false;

          if (!theLinkLeadsNowhere(error)) {
            this.state.set('unreachable');
            this.retry = setTimeout(() => this.askAgain(), this.retryIn);
            return;
          }

          // Having shown the order once is what tells the two apart: the same
          // 404 means the journey ended if it was on screen, and that the link
          // never led anywhere if it was not.
          this.state.set(this.known() === null ? 'nowhere' : 'over');
          this.stop();
        },
      });
  }

  /**
   * US-23: changed their mind before paying at the till. Either way it asks
   * again right after, rather than waiting for the live link: what the screen
   * shows next is the order as it is now — canceled, or paid a moment ago.
   */
  cancel(): void {
    if (this.where === null || this.cancelingNow()) return;

    this.cancelingNow.set(true);
    this.cancelRefusal.set(null);

    this.tracking
      .cancel(this.where.venueSlug, this.where.code, this.where.token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.cancelingNow.set(false);
          this.askAgain();
        },
        error: (error: unknown) => {
          this.cancelingNow.set(false);
          this.cancelRefusal.set(
            problemTypeOf(error) === ProblemTypes.orderNotCancelable ? 'alreadyPaid' : 'failed',
          );
          this.askAgain();
        },
      });
  }

  private stop(): void {
    this.clearRetry();
    this.channel.disconnect();
  }

  private clearRetry(): void {
    if (this.retry !== null) clearTimeout(this.retry);

    this.retry = null;
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
