import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import { KdsOrdersService } from './kds-orders.service';

/** How long "Entregado A-0066 · Deshacer" stays on screen after a delivery (US-18). */
export const UNDO_OFFER_MS = new InjectionToken<number>('UNDO_OFFER_MS', {
  providedIn: 'root',
  factory: () => 10_000,
});

/**
 * Which action on which order did not go through. Data and not a sentence: the
 * screen says it, in Spanish, like every other text the bar reads.
 */
export interface ActionFailure {
  readonly action: 'take' | 'return' | 'ready' | 'backToPreparation' | 'deliver' | 'undo';
  readonly code: string;
}

/**
 * What the bar's buttons do to an order, the same on every screen that has
 * them — the board's cards and the scan screen's search (US-16, US-18, US-20).
 * One per screen: which orders are in flight, what just failed and what can
 * still be undone belong to the screen they were done on.
 */
@Injectable()
export class KdsOrderActions {
  private readonly kdsOrders = inject(KdsOrdersService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly undoMs = inject(UNDO_OFFER_MS);

  /** Orders with a request in flight: their buttons stay pressed until it lands. */
  private readonly inFlight = signal<ReadonlySet<string>>(new Set());

  private readonly lastFailure = signal<ActionFailure | null>(null);

  private readonly delivered = signal<string | null>(null);

  /** What the last action on an order could not do. */
  readonly failure = this.lastFailure.asReadonly();

  /** The order just handed over, while its "Deshacer" is on offer. */
  readonly justDelivered = this.delivered.asReadonly();

  private settledCallbacks: ((code: string) => void)[] = [];

  private undoOffer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.withdrawUndo());
  }

  /**
   * What the screen does once an action lands, whether it went through or not
   * — reading the queue again, above all: a failed action usually means the
   * card was stale and the order moved on elsewhere.
   */
  whenSettled(callback: (code: string) => void): void {
    this.settledCallbacks.push(callback);
  }

  isBusy(code: string): boolean {
    return this.inFlight().has(code);
  }

  /** "Preparar" (US-16). */
  startPreparing(code: string): void {
    this.act(code, this.kdsOrders.startPreparing(code), 'take');
  }

  returnToQueue(code: string): void {
    this.act(code, this.kdsOrders.returnToQueue(code), 'return');
  }

  /** "Listo" (US-18). */
  markReady(code: string): void {
    this.act(code, this.kdsOrders.markReady(code), 'ready');
  }

  returnToPreparation(code: string): void {
    this.act(code, this.kdsOrders.returnToPreparation(code), 'backToPreparation');
  }

  /** "Entregado" by hand, with a few seconds to undo a mistaken tap. */
  deliver(code: string): void {
    this.act(code, this.kdsOrders.deliver(code), 'deliver', () => this.offerUndo(code));
  }

  undoDelivery(code: string): void {
    this.withdrawUndo();
    this.act(code, this.kdsOrders.undoDelivery(code), 'undo');
  }

  /**
   * "Deshacer" for a delivery made some other way than this service's own
   * button — a scan (US-20).
   */
  offerUndo(code: string): void {
    this.withdrawUndo();
    this.delivered.set(code);
    this.undoOffer = setTimeout(() => this.withdrawUndo(), this.undoMs);
  }

  private withdrawUndo(): void {
    if (this.undoOffer !== null) clearTimeout(this.undoOffer);
    this.undoOffer = null;
    this.delivered.set(null);
  }

  private act(
    code: string,
    request: Observable<void>,
    action: ActionFailure['action'],
    done?: () => void,
  ): void {
    this.lastFailure.set(null);
    this.inFlight.update((busy) => new Set(busy).add(code));

    const settle = (): void => {
      this.inFlight.update((busy) => {
        const next = new Set(busy);
        next.delete(code);

        return next;
      });

      for (const callback of this.settledCallbacks) callback(code);
    };

    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        done?.();
        settle();
      },
      error: () => {
        this.lastFailure.set({ action, code });
        settle();
      },
    });
  }
}
