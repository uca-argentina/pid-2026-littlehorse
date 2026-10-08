import { TestBed } from '@angular/core/testing';
import { BrowserStore } from '../storage/browser-store';
import { StoreInMemory } from '../storage/store-in-memory';
import { MY_ORDERS_STORAGE_PREFIX, MyOrders } from './my-orders';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const otherToken = '0a1b2c3d4e5f60718293a4b5c6d7e8f9';

const HOUR_MS = 60 * 60 * 1000;

describe('MyOrders', () => {
  let store: StoreInMemory;
  let orders: MyOrders;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-30T23:00:00Z'));
    store = new StoreInMemory();

    TestBed.configureTestingModule({ providers: [{ provide: BrowserStore, useValue: store }] });
    orders = TestBed.inject(MyOrders);
  });

  afterEach(() => vi.useRealTimers());

  it('remembers an order that was followed, for its venue', () => {
    orders.remember('bar-alfa', 'K-4821', token);

    expect(orders.of('bar-alfa')).toEqual([{ code: 'K-4821', token }]);
  });

  it('keeps it across visits', () => {
    orders.remember('bar-alfa', 'K-4821', token);

    const nextVisit = TestBed.runInInjectionContext(() => new MyOrders());

    expect(nextVisit.of('bar-alfa')).toEqual([{ code: 'K-4821', token }]);
  });

  // Criterion 5: another bar's order is not this bar's business.
  it('never shows one venue the orders of another', () => {
    orders.remember('bar-alfa', 'K-4821', token);

    expect(orders.of('bar-beta')).toEqual([]);
  });

  it('lists the newest order first', () => {
    orders.remember('bar-alfa', 'K-4821', token);
    vi.advanceTimersByTime(60_000);
    orders.remember('bar-alfa', 'K-4830', otherToken);

    expect(orders.of('bar-alfa').map((order) => order.code)).toEqual(['K-4830', 'K-4821']);
  });

  // The tracking screen remembers it every time it answers: that is one order, not a pile.
  it('remembers the same order once however often it is followed', () => {
    orders.remember('bar-alfa', 'K-4821', token);
    orders.remember('bar-alfa', 'K-4821', token);

    expect(orders.of('bar-alfa')).toHaveLength(1);
  });

  it('forgets an order', () => {
    orders.remember('bar-alfa', 'K-4821', token);
    orders.remember('bar-alfa', 'K-4830', otherToken);

    orders.forget('bar-alfa', 'K-4821');

    expect(orders.of('bar-alfa')).toEqual([{ code: 'K-4830', token: otherToken }]);
  });

  // A night out, not forever: yesterday's order is nobody's business tonight.
  it('forgets an order twelve hours after it was last followed', () => {
    orders.remember('bar-alfa', 'K-4821', token);

    vi.advanceTimersByTime(12 * HOUR_MS + 1);

    expect(orders.of('bar-alfa')).toEqual([]);
  });

  it('keeps an order followed again for another twelve hours', () => {
    orders.remember('bar-alfa', 'K-4821', token);
    vi.advanceTimersByTime(11 * HOUR_MS);
    orders.remember('bar-alfa', 'K-4821', token);

    vi.advanceTimersByTime(11 * HOUR_MS);

    expect(orders.of('bar-alfa')).toHaveLength(1);
  });

  // Whatever the phone kept is not trusted: an old version, or something else wrote there.
  it('remembers nothing from a stored value it cannot read', () => {
    store.write(`${MY_ORDERS_STORAGE_PREFIX}bar-alfa`, '{not json');

    expect(orders.of('bar-alfa')).toEqual([]);
  });

  it('skips stored entries that are not orders', () => {
    store.write(
      `${MY_ORDERS_STORAGE_PREFIX}bar-alfa`,
      JSON.stringify([{ code: 'K-4821' }, 42, { code: 'K-4830', token, savedAt: Date.now() }]),
    );

    expect(orders.of('bar-alfa')).toEqual([{ code: 'K-4830', token }]);
  });
});
