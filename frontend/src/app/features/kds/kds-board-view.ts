import type { KdsQueueOrder } from './kds-queue';

/**
 * How many ready orders the Listos column draws (US-18, decided on 2026-09-28):
 * the most recently made ones. The rest stay Listo and the scan screen's search
 * finds them.
 */
export const READY_SHOWN = 10;

/** Lowercase, and without the accents a hurried hand leaves out. */
function plain(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase();
}

function digitsOf(text: string): string {
  return text.replace(/\D/g, '');
}

/**
 * Whether an order answers to what was typed in the scan screen's search: by its
 * customer's name or by its number, the digits alone included — "66" is what
 * somebody reads out for A-0066.
 */
export function matchesSearch(order: KdsQueueOrder, query: string): boolean {
  const wanted = plain(query.trim());

  if (wanted === '') return true;
  if (plain(order.customerName).includes(wanted)) return true;
  if (plain(order.code).includes(wanted)) return true;

  const digits = digitsOf(wanted);

  return digits !== '' && digits === wanted && digitsOf(order.code).includes(digits);
}

/** What the Listos column draws, and how many more are waiting out of view. */
export interface ReadyShelf {
  readonly shown: KdsQueueOrder[];
  readonly hidden: number;
}

/**
 * The ready orders from the longest waiting to the newest, keeping only the
 * last {@link READY_SHOWN} on sight.
 */
export function readyShelf(ready: readonly KdsQueueOrder[]): ReadyShelf {
  const byWait = byTimeInColumn(ready);
  const hidden = Math.max(0, byWait.length - READY_SHOWN);

  return { shown: byWait.slice(hidden), hidden };
}

/**
 * Since when the order is in the column it is in (decided on 2026-09-28: each
 * column's clock restarts when the order enters it). Nuevos counts from the
 * payment, because "Devolver a la cola" keeps the original age. The others
 * count from the order's last change — its audit stamp, which only a move
 * writes — and from the payment when an order moved before that stamp existed.
 */
function inColumnSince(order: KdsQueueOrder): number {
  if (order.status === 'Queued') return Date.parse(order.paidAt);

  return Date.parse(order.lastModifiedAt ?? order.paidAt);
}

/** Whole minutes the order has spent in its current column. */
export function minutesInColumn(order: KdsQueueOrder, now: number): number {
  return Math.max(0, Math.floor((now - inColumnSince(order)) / 60_000));
}

/** From the order longest in its column to the newest arrival. */
export function byTimeInColumn(orders: readonly KdsQueueOrder[]): KdsQueueOrder[] {
  return [...orders].sort((first, second) => inColumnSince(first) - inColumnSince(second));
}
