import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ProblemTypes } from '../../../../core/api/problem-types';
import { problemTypeOf } from '../../../../core/api/problem-type-of';
import { NightsService, nightStockUrl } from '../../nights.service';
import type { NightStockLine } from '../../nights.service';

/** Why the stock is not on screen. */
type LoadFailure = 'none' | 'waiting' | 'unreachable';

/**
 * What a night has of each product (US-37): what it loaded, what it sold and
 * what is left. Reading it is what opens it, so the first time the
 * administrator looks at a night's stock it starts from what the night before
 * left.
 *
 * Units are added or taken away, never typed over a total: the bar keeps
 * selling while this is open, and a total would put back drinks already sold.
 * What is shown is what the API answers after every change.
 */
@Component({
  selector: 'drinkit-night-stock',
  styleUrl: './night-stock.scss',
  templateUrl: './night-stock.html',
})
export class NightStock {
  private readonly nights = inject(NightsService);

  private readonly destroyRef = inject(DestroyRef);

  readonly nightId = input.required<string>();

  /** False for a night that ended: it is read, and what it sold stays as it was. */
  readonly editable = input(true);

  protected readonly stock = httpResource<NightStockLine[]>(() => nightStockUrl(this.nightId()));

  /**
   * Waiting is not a failure to retry: the stock of this night does not exist
   * until the one before it is over, however many times it is asked for.
   */
  protected readonly failure = computed<LoadFailure>(() => {
    const error = this.stock.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.nightStockPreviousNightNotOver
      ? 'waiting'
      : 'unreachable';
  });

  /** What was typed in each row, by product. */
  private readonly typed = signal<Readonly<Record<string, string>>>({});

  /** What each row has to say about its last change, by product. */
  private readonly problems = signal<Readonly<Record<string, string>>>({});

  /** The rows waiting on the API, so a second tap is not a second change. */
  private readonly moving = signal<ReadonlySet<string>>(new Set());

  protected unitsOf(productId: string): string {
    return this.typed()[productId] ?? '';
  }

  protected problemOf(productId: string): string | undefined {
    return this.problems()[productId];
  }

  protected isMoving(productId: string): boolean {
    return this.moving().has(productId);
  }

  protected type(productId: string, event: Event): void {
    const units = (event.target as HTMLInputElement).value;

    this.typed.update((all) => ({ ...all, [productId]: units }));
  }

  /** Units that arrived, or taken away when it was loaded wrong. `direction` is +1 or −1. */
  protected move(line: NightStockLine, direction: 1 | -1): void {
    const productId = line.productId;

    if (this.isMoving(productId)) return;

    const units = Number(this.unitsOf(productId));

    if (!Number.isInteger(units) || units <= 0) {
      this.say(productId, 'Poné un número entero mayor a cero.');

      return;
    }

    this.say(productId, null);
    this.mark(productId, true);

    this.nights
      .adjustStock(this.nightId(), productId, direction * units)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.mark(productId, false);
          this.typed.update((all) => ({ ...all, [productId]: '' }));
          this.stock.reload();
        },
        error: (error: unknown) => {
          this.mark(productId, false);

          if (problemTypeOf(error) === ProblemTypes.nightStockMoved) {
            // What is left is read again, so the row says how many are left now.
            this.say(
              productId,
              'Se vendió mientras tanto y no alcanza para restar eso. Mirá cuántas quedan y probá de nuevo.',
            );
            this.stock.reload();

            return;
          }

          this.say(productId, 'No pudimos guardarlo. Fijate la señal y probá de nuevo.');
        },
      });
  }

  private say(productId: string, message: string | null): void {
    this.problems.update((all) => {
      const next = { ...all };

      if (message === null) delete next[productId];
      else next[productId] = message;

      return next;
    });
  }

  /** A new set every time, so the template sees the change. */
  private mark(productId: string, member: boolean): void {
    this.moving.update((ids) => {
      const next = new Set(ids);

      if (member) next.add(productId);
      else next.delete(productId);

      return next;
    });
  }
}
