import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { problemTypeOf } from '../../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../../core/api/problem-types';
import type { NightPhase } from '../../night-calendar';
import { NIGHTS_URL, NightsService } from '../../nights.service';
import type {
  NightStockBar,
  NightStock as NightStockSheet,
  NightStockLine,
} from '../../nights.service';

/** A message that belongs to one product's row in one bar. */
interface RowMessage {
  readonly kdsId: string;
  readonly productId: string;
  readonly text: string;
}

const NOT_A_COUNT = 'Escribí cuántas unidades: un número entero mayor que cero.';

const STOREROOM_SHORT =
  'En el depósito no hay tantas: se cargaron en otra barra o se ajustó el stock. Ya actualizamos cuántas hay.';

const STOCK_MOVED =
  'La barra vendió mientras tanto y no alcanza para devolver eso. Ya actualizamos cuánto queda.';

const UNREACHABLE = 'No pudimos guardarlo. Fijate la señal y probá de nuevo.';

/** Refusals about the whole night rather than about one row. */
const NIGHT_REFUSALS = new Map<string, string>([
  [ProblemTypes.nightStockClosed, 'La noche terminó mientras cargabas: su stock ya sólo se mira.'],
  [
    ProblemTypes.nightBarNotInNight,
    'Esa barra ya no trabaja esta noche. Revisá el personal de la noche.',
  ],
]);

/** Refusals that the sheet as it is now explains, so it is asked for again. */
const ROW_REFUSALS = new Map<string, string>([
  [ProblemTypes.nightStoreroomShort, STOREROOM_SHORT],
  [ProblemTypes.productStockMoved, STOCK_MOVED],
]);

/**
 * The night's stock (US-37), inside its own screen: one section per bar (a
 * KDS of the night), one row per product with what was loaded, sold and is
 * left, beside what the storeroom still has, and the units to load into the
 * bar or give back. A new night's bars start with the storeroom split evenly
 * among them. A finished night's is only read. The API holds the rules; this only avoids offering what it would
 * refuse.
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

  /** Products are the same in every bar, so the first one says whether the menu is empty. */
  protected readonly hasProducts = computed(
    () => this.sheet.hasValue() && (this.sheet.value().bars[0]?.lines.length ?? 0) > 0,
  );

  /** Whether a change is on its way. One at a time: each answer is the whole sheet. */
  protected readonly pending = signal(false);

  protected readonly rowMessage = signal<RowMessage | null>(null);

  protected readonly refusal = signal<string | null>(null);

  protected messageFor(bar: NightStockBar, line: NightStockLine): string | null {
    const message = this.rowMessage();

    if (message === null) return null;

    return message.kdsId === bar.kdsId && message.productId === line.productId
      ? message.text
      : null;
  }

  protected adjust(
    bar: NightStockBar,
    line: NightStockLine,
    field: HTMLInputElement,
    direction: 1 | -1,
  ): void {
    if (this.pending()) return;

    const units = Number(field.value);
    const row = { kdsId: bar.kdsId, productId: line.productId };

    if (field.value.trim() === '' || !Number.isInteger(units) || units <= 0) {
      this.rowMessage.set({ ...row, text: NOT_A_COUNT });

      return;
    }

    this.rowMessage.set(null);
    this.refusal.set(null);
    this.pending.set(true);

    this.nights
      .adjustStock(this.nightId(), bar.kdsId, line.productId, direction * units)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.pending.set(false);
          this.sheet.set(updated);
          field.value = '';
        },
        error: (error: unknown) => {
          this.pending.set(false);
          this.answer(error, row);
        },
      });
  }

  protected retry(): void {
    this.sheet.reload();
  }

  private answer(error: unknown, row: Omit<RowMessage, 'text'>): void {
    const type = error instanceof HttpErrorResponse ? (problemTypeOf(error) ?? '') : '';
    const nightRefusal = NIGHT_REFUSALS.get(type);

    if (nightRefusal !== undefined) {
      this.refusal.set(nightRefusal);

      return;
    }

    const rowRefusal = ROW_REFUSALS.get(type);

    if (rowRefusal !== undefined) {
      this.rowMessage.set({ ...row, text: rowRefusal });
      this.sheet.reload();

      return;
    }

    this.rowMessage.set({ ...row, text: UNREACHABLE });
  }
}
