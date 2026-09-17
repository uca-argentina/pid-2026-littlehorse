import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Cart } from '../../core/cart/cart';
import { BrowserStore } from '../../core/storage/browser-store';
import { StoreInMemory } from '../../core/storage/store-in-memory';
import { ordersUrl } from './checkout.service';
import { PROCESSING_PAUSE_MS, CheckoutStore } from './checkout.store';
import type { ConfirmedOrder } from './checkout.service';

const confirmed: ConfirmedOrder = {
  id: 'ba5eba11-0000-4000-8000-000000000001',
  code: 'A-0000',
  customerName: 'María Quadro',
  total: 9000,
  status: 'Queued',
  paidAt: '2026-09-17T02:30:00Z',
};

let store: StoreInMemory;

/**
 * The store waits for the response and for the "procesando" pause, whichever
 * takes longer. The pause is zero here but a timer still fires on its own turn,
 * so the outcome lands one tick after the response does.
 */
function theTimerFires(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/** The timer, the response, and the navigation that follows them. */
async function itAllSettles(): Promise<void> {
  await theTimerFires();
  await theTimerFires();
}

function aStore(): { checkout: CheckoutStore; http: HttpTestingController; cart: Cart } {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      // A route that matches anything: the store empties the cart once the
      // confirmation is actually on screen, and a navigation to a path the
      // test router does not know would never get there.
      provideRouter([{ path: '**', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
      // No pause at all: what the two seconds are for is somebody watching the
      // screen, and nobody is watching this.
      { provide: PROCESSING_PAUSE_MS, useValue: 0 },
      CheckoutStore,
    ],
  });

  const cart = TestBed.inject(Cart);
  cart.open('bar-alfa');
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });

  return {
    checkout: TestBed.inject(CheckoutStore),
    http: TestBed.inject(HttpTestingController),
    cart,
  };
}

describe('CheckoutStore', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  it('sends what is in the order, and no prices', async () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    const sent = http.expectOne(ordersUrl('bar-alfa'));

    expect(sent.request.method).toBe('POST');
    expect(sent.request.body.customerName).toBe('María Quadro');
    expect(sent.request.body.lines).toEqual([{ productId: 'id-1', quantity: 2, note: null }]);
    expect(JSON.stringify(sent.request.body)).not.toContain('4500');
  });

  // Criterion 6: the browser retries on its own, and the same key has to mean
  // the same order rather than a second one.
  it('sends the same key on every attempt', async () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    const first = http.expectOne(ordersUrl('bar-alfa'));
    first.flush({ detail: 'sin red' }, { status: 0, statusText: 'Unknown Error' });
    await TestBed.inject(Router).navigate([]);

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    const second = http.expectOne(ordersUrl('bar-alfa'));

    expect(second.request.body.idempotencyKey).toBe(first.request.body.idempotencyKey);
    expect(second.request.body.idempotencyKey).toBeTruthy();
  });

  /**
   * The one the review caught: somebody pays, the answer is lost on the way
   * back, and they do what anybody does on a bad signal — reload and pay again.
   * A key that lived only in the screen would be a new key, and the second
   * attempt a second paid order, which is exactly what the screen promises
   * will not happen.
   */
  it('keeps the same key across a reload of the screen', async () => {
    const first = aStore();
    first.checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    const before = first.http.expectOne(ordersUrl('bar-alfa')).request.body.idempotencyKey;

    // A new store over the same browser storage is what a reload produces.
    const after = aStore();
    after.checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    expect(after.http.expectOne(ordersUrl('bar-alfa')).request.body.idempotencyKey).toBe(before);
  });

  // And it is let go of once the order exists, so the next round of the night
  // is a new order and not an answer about the last one.
  it('lets the key go once the order is confirmed', async () => {
    const paid = aStore();
    paid.checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    const used = paid.http.expectOne(ordersUrl('bar-alfa'));
    used.flush(confirmed);
    await itAllSettles();

    const next = aStore();
    next.checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    expect(next.http.expectOne(ordersUrl('bar-alfa')).request.body.idempotencyKey).not.toBe(
      used.request.body.idempotencyKey,
    );
  });

  it('shows the confirmation of the order it was given', async () => {
    const { checkout, http } = aStore();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    http.expectOne(ordersUrl('bar-alfa')).flush(confirmed);
    await theTimerFires();

    expect(navigate).toHaveBeenCalledWith(['/', 'bar-alfa', 'orders', 'A-0000']);
  });

  // The order belongs to the server now. What is left on the phone is a copy
  // nobody should be able to pay for a second time.
  it("empties the phone once the order is the server's", async () => {
    const { checkout, http, cart } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    http.expectOne(ordersUrl('bar-alfa')).flush(confirmed);
    await itAllSettles();

    expect(cart.isEmpty()).toBe(true);
  });

  it('is paying while it waits', () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    expect(checkout.isPaying()).toBe(true);

    http.expectOne(ordersUrl('bar-alfa')).flush(confirmed);
  });

  // Two taps on a slow phone are one order, before the key even matters.
  it('does not send a second time while one is in flight', () => {
    const { checkout, http } = aStore();

    checkout.pay('bar-alfa', 'María Quadro', 'Digital');
    checkout.pay('bar-alfa', 'María Quadro', 'Digital');

    http.expectOne(ordersUrl('bar-alfa')).flush(confirmed);
  });

  describe('when it cannot go through', () => {
    // The menu moved underneath: a drink ran out, or somebody took the last
    // one. Kept apart from a bad name because the way out is different — this
    // one sends them back to the order to change it.
    it.each([
      'urn:drinkit:problem:order:sold-out',
      'urn:drinkit:problem:order:not-on-the-menu',
      'urn:drinkit:problem:order:stock-moved',
    ])('says which drink when the menu moved (%s)', (type) => {
      const { checkout, http } = aStore();

      checkout.pay('bar-alfa', 'María Quadro', 'Digital');
      http
        .expectOne(ordersUrl('bar-alfa'))
        .flush(
          { type, detail: 'Gin Tonic ran out while you were ordering.' },
          { status: 409, statusText: 'Conflict' },
        );

      expect(checkout.status()).toBe('soldOut');
      expect(checkout.whatWentWrong()).toContain('Gin Tonic');
    });

    // The order stays on the phone: they are going to fix it and try again.
    it('keeps the order on the phone when the menu moved', () => {
      const { checkout, http, cart } = aStore();

      checkout.pay('bar-alfa', 'María Quadro', 'Digital');
      http
        .expectOne(ordersUrl('bar-alfa'))
        .flush(
          { type: 'urn:drinkit:problem:order:sold-out', detail: 'Se agotó.' },
          { status: 409, statusText: 'Conflict' },
        );

      expect(cart.isEmpty()).toBe(false);
    });

    it('repeats what the API said about the name', () => {
      const { checkout, http } = aStore();

      checkout.pay('bar-alfa', 'Euge', 'Digital');
      http.expectOne(ordersUrl('bar-alfa')).flush(
        {
          type: 'urn:drinkit:problem:order:name-needs-surname',
          detail: 'Give us your first name and your surname.',
        },
        { status: 400, statusText: 'Bad Request' },
      );

      expect(checkout.status()).toBe('rejected');
      expect(checkout.whatWentWrong()).toContain('surname');
    });

    // No problem document at all: the venue's wifi, not our rules.
    it('tells a dropped connection apart from a refusal', () => {
      const { checkout, http } = aStore();

      checkout.pay('bar-alfa', 'María Quadro', 'Digital');
      http.expectOne(ordersUrl('bar-alfa')).error(new ProgressEvent('error'));

      expect(checkout.status()).toBe('unreachable');
    });

    it('lets them try again after it failed', () => {
      const { checkout, http } = aStore();

      checkout.pay('bar-alfa', 'María Quadro', 'Digital');
      http.expectOne(ordersUrl('bar-alfa')).error(new ProgressEvent('error'));

      expect(checkout.isPaying()).toBe(false);

      checkout.pay('bar-alfa', 'María Quadro', 'Digital');

      expect(http.expectOne(ordersUrl('bar-alfa')).request.method).toBe('POST');
    });
  });
});
