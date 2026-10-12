import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { problemTypeOf } from '../../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../../core/api/problem-types';
import type { NightPhase } from '../../night-calendar';
import { NIGHTS_URL, NightsService } from '../../nights.service';
import type { NightStock as NightStockSheet, NightStockLine } from '../../nights.service';

/** A message that belongs to one product's row. */
interface RowMessage {
  readonly productId: string;
  readonly text: string;
}

const NOT_A_COUNT = 'Escribí cuántas unidades: un número entero mayor que cero.';

const STOCK_MOVED =
  'Se vendió mientras tanto y no alcanza para restar eso. Ya actualizamos cuánto queda.';

const UNREACHABLE = 'No pudimos guardarlo. Fijate la señal y probá de nuevo.';

/** Refusals about the whole night rather than about one product. */
const NIGHT_REFUSALS = new Map<string, string>([
  [ProblemTypes.nightStockClosed, 'La noche terminó mientras cargabas: su stock ya sólo se mira.'],
  [
    ProblemTypes.nightEarlierNightOpen,
    'Todavía no terminó la noche anterior. Cargá el stock de esta cuando termine: arranca con lo que esa deje.',
  ],
]);

/**
 * The night's stock (US-37), inside its own screen: one row per product with
 * what it starts with, what was loaded, sold and is left, and the units to add
 * or take away. A finished night's is only read. The API holds the rules; this
 * only avoids offering what it would refuse.
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

  readonly phase = input.required<NightPhase>();

  protected readonly sheet = httpResource<NightStockSheet>(
    () => `${NIGHTS_URL}/${this.nightId()}/stock`,
  );

  protected readonly isOver = computed(() => this.phase() === 'over');

  /** "Arranca" while the night before may still sell; "arrancó" once this one is on. */
  protected readonly startsWith = computed(() =>
    this.phase() === 'upcoming' ? 'Arranca con' : 'Arrancó con',
  );

  /** The product whose change is on its way. One at a time: each answer is the whole sheet. */
  protected readonly pending = signal<string | null>(null);

  protected readonly rowMessage = signal<RowMessage | null>(null);

  protected readonly refusal = signal<string | null>(null);

  /** "+12", "−5" or "0", as the row shows what was loaded. */
  protected signed(units: number): string {
    if (units > 0) return `+${units}`;

    return units < 0 ? `−${-units}` : '0';
  }

  protected messageFor(line: NightStockLine): string | null {
    const message = this.rowMessage();

    return message?.productId === line.productId ? message.text : null;
  }

  protected adjust(line: NightStockLine, field: HTMLInputElement, direction: 1 | -1): void {
    if (this.pending() !== null) return;

    const units = Number(field.value);

    if (field.value.trim() === '' || !Number.isInteger(units) || units <= 0) {
      this.rowMessage.set({ productId: line.productId, text: NOT_A_COUNT });

      return;
    }

    this.rowMessage.set(null);
    this.refusal.set(null);
    this.pending.set(line.productId);

    this.nights
      .adjustStock(this.nightId(), line.productId, direction * units)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.pending.set(null);
          this.sheet.set(updated);
          field.value = '';
        },
        error: (error: unknown) => {
          this.pending.set(null);
          this.answer(error, line.productId);
        },
      });
  }

  protected retry(): void {
    this.sheet.reload();
  }

  private answer(error: unknown, productId: string): void {
    const type = error instanceof HttpErrorResponse ? (problemTypeOf(error) ?? '') : '';
    const nightRefusal = NIGHT_REFUSALS.get(type);

    if (nightRefusal !== undefined) {
      this.refusal.set(nightRefusal);

      return;
    }

    if (type === ProblemTypes.productStockMoved) {
      this.rowMessage.set({ productId, text: STOCK_MOVED });
      this.sheet.reload();

      return;
    }

    this.rowMessage.set({ productId, text: UNREACHABLE });
  }
}
