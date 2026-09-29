import type { KdsQueueOrder } from './kds-queue';
import {
  READY_SHOWN,
  byTimeInColumn,
  matchesSearch,
  minutesInColumn,
  readyShelf,
} from './kds-board-view';

function anOrder(overrides: Partial<KdsQueueOrder> = {}): KdsQueueOrder {
  return {
    code: 'A-0066',
    customerName: 'María Quadro',
    status: 'Queued',
    paidAt: '2026-09-28T01:00:00Z',
    lastModifiedAt: null,
    isForTable: false,
    orderItems: [{ productName: 'Gin Tonic', quantity: 1, note: null }],
    ...overrides,
  };
}

describe('matchesSearch', () => {
  it('matches everything while nothing is typed', () => {
    expect(matchesSearch(anOrder(), '   ')).toBe(true);
  });

  // Typed on a wet tablet, in a hurry: no capitals, no accents.
  it('finds a name without its accents or capitals', () => {
    expect(matchesSearch(anOrder(), 'maria')).toBe(true);
    expect(matchesSearch(anOrder(), 'QUADRO')).toBe(true);
  });

  // What the customer reads out is the number, not the letter in front.
  it('finds an order by the digits of its number', () => {
    expect(matchesSearch(anOrder(), '66')).toBe(true);
    expect(matchesSearch(anOrder(), 'a-0066')).toBe(true);
  });

  it('leaves out what does not match', () => {
    expect(matchesSearch(anOrder(), 'pablo')).toBe(false);
    expect(matchesSearch(anOrder(), '77')).toBe(false);
  });
});

describe('readyShelf', () => {
  const changedAt = (minute: number) => `2026-09-28T02:${String(minute).padStart(2, '0')}:00Z`;

  function readyOrders(count: number): KdsQueueOrder[] {
    return Array.from({ length: count }, (_, index) =>
      anOrder({
        code: `A-${String(1000 + index)}`,
        status: 'Ready',
        lastModifiedAt: changedAt(index),
      }),
    );
  }

  // Oldest ready first, as in every other column.
  it('orders the ready ones from the longest waiting to the newest', () => {
    const shuffled = [readyOrders(3)[2], readyOrders(3)[0], readyOrders(3)[1]];

    expect(readyShelf(shuffled).shown.map((order) => order.code)).toEqual([
      'A-1000',
      'A-1001',
      'A-1002',
    ]);
  });

  // Decided on 2026-09-28: ten on sight, the newest ten; the ones waiting
  // longest step out of view, still Listo, still findable.
  it(`shows only the ${String(READY_SHOWN)} most recently made and counts the rest`, () => {
    const shelf = readyShelf(readyOrders(13));

    expect(shelf.shown).toHaveLength(READY_SHOWN);
    expect(shelf.shown[0].code).toBe('A-1003');
    expect(shelf.hidden).toBe(3);
  });

  it('hides nothing while there is room', () => {
    expect(readyShelf(readyOrders(4)).hidden).toBe(0);
  });
});

// Decided on 2026-09-28: each column's clock restarts when the order enters it.
describe('minutesInColumn', () => {
  const now = Date.parse('2026-09-28T02:10:00Z');

  // "Devolver a la cola" keeps the original age, so Nuevos counts from payment.
  it('counts a new order from when it was paid, even after it was moved', () => {
    const requeued = anOrder({
      status: 'Queued',
      paidAt: '2026-09-28T02:00:00Z',
      lastModifiedAt: '2026-09-28T02:08:00Z',
    });

    expect(minutesInColumn(requeued, now)).toBe(10);
  });

  it('counts an order in preparation from when it was taken', () => {
    const taken = anOrder({ status: 'InPreparation', lastModifiedAt: '2026-09-28T02:07:00Z' });

    expect(minutesInColumn(taken, now)).toBe(3);
  });

  it('counts a ready order from when it was made', () => {
    const made = anOrder({ status: 'Ready', lastModifiedAt: '2026-09-28T02:09:00Z' });

    expect(minutesInColumn(made, now)).toBe(1);
  });

  // Orders moved before the stamp existed have none: they count from payment.
  it('falls back to the payment when the order has no change recorded', () => {
    const old = anOrder({
      status: 'InPreparation',
      paidAt: '2026-09-28T02:00:00Z',
      lastModifiedAt: null,
    });

    expect(minutesInColumn(old, now)).toBe(10);
  });

  it('never reads a clock that runs a little behind as negative', () => {
    const ahead = anOrder({ status: 'Ready', lastModifiedAt: '2026-09-28T02:11:00Z' });

    expect(minutesInColumn(ahead, now)).toBe(0);
  });
});

describe('byTimeInColumn', () => {
  it('puts the order longest in its column first', () => {
    const orders = [
      anOrder({
        code: 'A-0002',
        status: 'InPreparation',
        paidAt: '2026-09-28T01:00:00Z',
        lastModifiedAt: '2026-09-28T02:05:00Z',
      }),
      anOrder({
        code: 'A-0001',
        status: 'InPreparation',
        paidAt: '2026-09-28T01:30:00Z',
        lastModifiedAt: '2026-09-28T02:01:00Z',
      }),
    ];

    expect(byTimeInColumn(orders).map((order) => order.code)).toEqual(['A-0001', 'A-0002']);
  });
});
