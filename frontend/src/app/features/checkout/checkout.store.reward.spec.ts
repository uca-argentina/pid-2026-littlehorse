import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ProblemTypes } from '../../core/api/problem-types';
import { Cart } from '../../core/cart/cart';
import { STAMP_CARD_KEY_PREFIX, StampCards, stampCardUrl } from '../../core/stamp-card/stamp-cards';
import { BrowserStore } from '../../core/storage/browser-store';
import { StoreInMemory } from '../../core/storage/store-in-memory';
import { ordersUrl } from './checkout.service';
import type { ConfirmedOrder } from './checkout.service';
import { PROCESSING_PAUSE_MS, CheckoutStore } from './checkout.store';

const TOKEN = 'b'.repeat(64);

const confirmed: ConfirmedOrder = {
  id: 'ba5eba11-0000-4000-8000-000000000001',
  code: 'A-0000',
  trackingToken: '9f3c2ba7d81e4c06a1b2c3d4e5f60718',
  customerName: 'María Quadro',
  total: 9000,
  status: 'Queued',
  paidAt: '2026-09-17T02:30:00Z',
  stampCardToken: TOKEN,
};

let store: StoreInMemory;

async function itAllSettles(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await new Promise((resolve) => setTimeout(resolve, 0));
}

function aStore() {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: '**', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
      { provide: PROCESSING_PAUSE_MS, useValue: 0 },
      CheckoutStore,
    ],
  });

  const cart = TestBed.inject(Cart);
  cart.open('bar-alfa');
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });

  return {
    checkout: TestBed.inject(CheckoutStore),
    http: TestBed.inject(HttpTestingController),
    cards: TestBed.inject(StampCards),
    router: TestBed.inject(Router),
  };
}

describe('CheckoutStore with a stamp card', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  it('sends the card the phone holds, and no reward unless asked for', () => {
    store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    const body = http.expectOne(ordersUrl('bar-alfa')).request.body;

    expect(body.stampCardToken).toBe(TOKEN);
    expect(body.redeemReward).toBe(false);
  });

  it('sends no token when the phone has no card yet', () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    expect(http.expectOne(ordersUrl('bar-alfa')).request.body.stampCardToken).toBeNull();
  });

  // US-59: the phone says it wants the reward and never what it costs.
  it('asks for the reward with a flag and no price', () => {
    store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital', null, null, true);

    const body = http.expectOne(ordersUrl('bar-alfa')).request.body;

    expect(body.redeemReward).toBe(true);
    expect(body.lines).toEqual([{ productId: 'id-1', quantity: 1, note: null }]);
  });

  // US-58, criterion 1: "queda guardada en el celular".
  it('keeps the card the first order hands over', async () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    http.expectOne(ordersUrl('bar-alfa')).flush(confirmed);
    await itAllSettles();

    expect(store.read(STAMP_CARD_KEY_PREFIX + 'bar-alfa')).toBe(TOKEN);
  });

  // Found by the end-to-end spec: the card on the tracking screen showed the
  // stamps from before the reward was spent, because it was read at checkout.
  it('reads the card again once the order is confirmed, so the next screen shows what was spent', async () => {
    store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);
    const { checkout, http, cards } = aStore();

    cards.open('bar-alfa');
    TestBed.tick();
    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({
        isActive: true,
        stampsForReward: 2,
        rewardProductId: 'id-2',
        rewardName: 'Fernet',
        stamps: 2,
        canRedeem: true,
      });
    await itAllSettles();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital', null, null, true);
    http.expectOne(ordersUrl('bar-alfa')).flush(confirmed);
    await itAllSettles();
    TestBed.tick();

    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({
        isActive: true,
        stampsForReward: 2,
        rewardProductId: 'id-2',
        rewardName: 'Fernet',
        stamps: 0,
        canRedeem: false,
      });
    await Promise.resolve();

    expect(cards.view()?.stamps).toBe(0);
  });

  it('keeps nothing when the order came without a card', async () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    http.expectOne(ordersUrl('bar-alfa')).flush({ ...confirmed, stampCardToken: null });
    await itAllSettles();

    expect(store.read(STAMP_CARD_KEY_PREFIX + 'bar-alfa')).toBeNull();
  });

  // US-59, criterion 4 and 2: told in Spanish by the screen, nothing confirmed, card read again.
  it.each([
    ProblemTypes.stampCardNotEnoughStamps,
    ProblemTypes.stampCardCardMoved,
    ProblemTypes.stampCardRewardUnavailable,
    ProblemTypes.stampCardNotActive,
  ])('says the reward was refused and reads the card again on %s', async (type) => {
    store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);
    const { checkout, http, cards } = aStore();

    cards.open('bar-alfa');
    TestBed.tick();
    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({
        isActive: true,
        stampsForReward: 2,
        rewardProductId: 'id-2',
        rewardName: 'Fernet',
        stamps: 2,
        canRedeem: true,
      });
    await Promise.resolve();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital', null, null, true);
    http.expectOne(ordersUrl('bar-alfa')).flush({ type }, { status: 409, statusText: 'Conflict' });
    await itAllSettles();
    TestBed.tick();

    expect(checkout.status()).toBe('rewardRefused');
    expect(checkout.whatWentWrong()?.type).toBe(type);
    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({
        isActive: true,
        stampsForReward: 2,
        rewardProductId: 'id-2',
        rewardName: 'Fernet',
        stamps: 0,
        canRedeem: false,
      });
  });

  it('does not empty the cart when the reward was refused', async () => {
    const { checkout, http, cart } = (() => {
      const made = aStore();

      return { ...made, cart: TestBed.inject(Cart) };
    })();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital', null, null, true);
    http
      .expectOne(ordersUrl('bar-alfa'))
      .flush(
        { type: ProblemTypes.stampCardNotEnoughStamps },
        { status: 409, statusText: 'Conflict' },
      );
    await itAllSettles();

    expect(cart.count()).toBe(1);
  });
});
