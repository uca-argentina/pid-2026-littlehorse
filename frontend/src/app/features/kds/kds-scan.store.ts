import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { KdsOrdersService } from './kds-orders.service';
import type { ScanOutcome } from './kds-orders.service';

/**
 * How long the same code read again is taken as the same scan. The camera sees
 * a QR in every frame for as long as it is held up; a second person holding
 * up the same one comes later than this.
 */
export const SCAN_REPEAT_MS = new InjectionToken<number>('SCAN_REPEAT_MS', {
  providedIn: 'root',
  factory: () => 3_000,
});

/** How many scans "Últimos escaneos" shows. */
export const RECENT_SCANS_KEPT = 5;

/**
 * One scan, as the screen lists it. Never the token that was read: that is the
 * customer's secret, sent once and not kept.
 */
export type ScanResult = { readonly id: number } & (
  | {
      readonly kind: 'found';
      readonly code: string;
      readonly customerName: string;
      readonly outcome: ScanOutcome;
      readonly at: number;
    }
  /** Nothing of this venue's: another venue's QR, a stranger's, not ours at all. */
  | { readonly kind: 'unknown'; readonly at: number }
  /** It never reached the API, so nothing was handed over. */
  | { readonly kind: 'failed'; readonly at: number }
);

/**
 * Whether a text has the shape of what a customer's QR carries: 32 lowercase
 * hex digits, the TrackingToken. A reader that types into the wrong field is
 * recognised by this.
 */
export function isTrackingToken(text: string): boolean {
  return /^[0-9a-f]{32}$/.test(text.trim());
}

/**
 * US-20: what the scan screen has read and what came of it. Kept on this
 * tablet only, for as long as the screen is open: it is there so the bartender
 * sees what just happened, not as a record.
 */
@Injectable()
export class KdsScanStore {
  private readonly kdsOrders = inject(KdsOrdersService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly repeatMs = inject(SCAN_REPEAT_MS);

  private readonly results = signal<readonly ScanResult[]>([]);

  /** Newest first. */
  readonly recent = this.results.asReadonly();

  /** The last code sent, and when: only to tell a repeat frame from a new scan. */
  private last: { read: string; at: number } | null = null;

  private nextId = 0;

  /**
   * From the camera or the reader alike: both end up here. Answers whether it
   * went out — an empty read or a repeat frame does not — so the screen only
   * acknowledges a scan that is really on its way.
   */
  submit(raw: string): boolean {
    const read = raw.trim();

    if (read === '') return false;
    if (this.isRepeat(read)) return false;

    this.last = { read, at: Date.now() };

    this.kdsOrders
      .scan(read)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (order) =>
          this.record((id) => ({
            id,
            kind: 'found',
            code: order.code,
            customerName: order.customerName,
            outcome: order.outcome,
            at: Date.now(),
          })),
        error: (error: unknown) => {
          const unknown = error instanceof HttpErrorResponse && error.status === 404;

          // Nothing happened on the API's side, so the same QR may go again now.
          if (!unknown) this.last = null;

          this.record((id) => ({ id, kind: unknown ? 'unknown' : 'failed', at: Date.now() }));
        },
      });

    return true;
  }

  private isRepeat(read: string): boolean {
    return (
      this.last !== null && this.last.read === read && Date.now() - this.last.at <= this.repeatMs
    );
  }

  /** Numbered as it is recorded: two answers in the same millisecond stay apart. */
  private record(result: (id: number) => ScanResult): void {
    const recorded = result(this.nextId++);

    this.results.update((results) => [recorded, ...results].slice(0, RECENT_SCANS_KEPT));
  }
}
