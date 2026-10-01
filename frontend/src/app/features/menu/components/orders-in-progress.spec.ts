import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { MY_ORDERS_STORAGE_PREFIX, MyOrders } from '../../../core/orders/my-orders';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { trackingUrl } from '../../tracking/tracking.service';
import type { CustomerOrderStatus, TrackedOrder } from '../../tracking/tracking.service';
import { OrdersInProgress } from './orders-in-progress';
import { OrdersInProgressChannel } from './orders-in-progress-channel';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const otherToken = '0a1b2c3d4e5f60718293a4b5c6d7e8f9';

function anOrder(code: string, status: CustomerOrderStatus): TrackedOrder {
  return {
    code,
    customerName: 'María Quadro',
    status,
    total: 9000,
    paidAt: null,
    items: [{ productName: 'Gin Tonic', quantity: 2, note: null }],
  };
}

const notFound = [
  { type: 'urn:drinkit:problem:order:not-found', detail: 'no existe' },
  { status: 404, statusText: 'Not Found' },
] as const;

/** Renders the banner with whatever this phone remembers, and answers each order's status. */
async function openMenuHaving(remembered: [code: string, token: string][]) {
  const store = new StoreInMemory();

  // Written as MyOrders keeps them, oldest first. Straight into the store:
  // render() configures the test module, so nothing can be injected before it.
  store.write(
    `${MY_ORDERS_STORAGE_PREFIX}bar-alfa`,
    JSON.stringify(
      remembered.map(([code, orderToken], index) => ({
        code,
        token: orderToken,
        savedAt: Date.now() - (remembered.length - index) * 1000,
      })),
    ),
  );

  const channel = new FakeChannel();

  const rendered = await render(OrdersInProgress, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
      { provide: OrdersInProgressChannel, useValue: channel },
    ],
  });

  return {
    rendered,
    http: TestBed.inject(HttpTestingController),
    myOrders: TestBed.inject(MyOrders),
    channel,
  };
}

/** Stands in for the live link: the test says when an order moves. */
class FakeChannel {
  following: readonly string[] | null = null;

  private changed: (() => void) | null = null;

  follow(tokens: readonly string[], onChanged: () => void): void {
    this.following = tokens;
    this.changed = onChanged;
  }

  disconnect(): void {
    this.following = null;
    this.changed = null;
  }

  anOrderMoves(): void {
    this.changed?.();
  }
}

describe('OrdersInProgress', () => {
  it('shows nothing, and asks nothing, when this phone followed no order here', async () => {
    const { http } = await openMenuHaving([]);

    http.expectNone(() => true);
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('leads back to the tracking screen of the order in progress', async () => {
    const { rendered, http } = await openMenuHaving([['K-4821', token]]);

    http
      .expectOne(trackingUrl('bar-alfa', 'K-4821', token))
      .flush(anOrder('K-4821', 'InPreparation'));
    await rendered.fixture.whenStable();

    const link = screen.getByRole('link', { name: /Tu pedido K-4821/ });
    expect(link.textContent).toContain('Lo están preparando');
    expect(link.getAttribute('href')).toBe(`/bar-alfa/orders/K-4821/${token}`);
  });

  // Criterion 6: no reloading to find out the bar made it.
  it('changes on its own when one of its orders moves', async () => {
    const { rendered, http, channel } = await openMenuHaving([['K-4821', token]]);
    http
      .expectOne(trackingUrl('bar-alfa', 'K-4821', token))
      .flush(anOrder('K-4821', 'InPreparation'));
    await rendered.fixture.whenStable();

    expect(channel.following).toEqual([token]);

    channel.anOrderMoves();
    // The resource asks again on the next round, not in the act. whenStable()
    // would wait for that request, which only this test can answer.
    TestBed.tick();
    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(anOrder('K-4821', 'Ready'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('link', { name: /Tu pedido K-4821/ }).textContent).toContain(
      '¡Está listo! Retiralo en la barra',
    );
  });

  it('follows the orders it shows, not the ones that are gone', async () => {
    const { rendered, http, channel } = await openMenuHaving([
      ['K-4821', token],
      ['K-4830', otherToken],
    ]);
    http
      .expectOne(trackingUrl('bar-alfa', 'K-4830', otherToken))
      .flush(anOrder('K-4830', 'Queued'));
    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(...notFound);
    await rendered.fixture.whenStable();

    expect(channel.following).toEqual([otherToken]);
  });

  it('lets go of the live link when the menu closes', async () => {
    const { rendered, http, channel } = await openMenuHaving([['K-4821', token]]);
    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(anOrder('K-4821', 'Queued'));
    await rendered.fixture.whenStable();

    rendered.fixture.destroy();

    expect(channel.following).toBeNull();
  });

  // Criterion 2.
  it('says so when the order is ready to pick up', async () => {
    const { rendered, http } = await openMenuHaving([['K-4821', token]]);

    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(anOrder('K-4821', 'Ready'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('link', { name: /Tu pedido K-4821/ }).textContent).toContain(
      '¡Está listo! Retiralo en la barra',
    );
  });

  // Criterion 3: closed, one line — how many, and the one that needs them most.
  it('folds several orders into one line naming the most urgent', async () => {
    const { rendered, http } = await openMenuHaving([
      ['K-4821', token],
      ['K-4830', otherToken],
    ]);

    http
      .expectOne(trackingUrl('bar-alfa', 'K-4830', otherToken))
      .flush(anOrder('K-4830', 'AwaitingPayment'));
    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(anOrder('K-4821', 'Ready'));
    await rendered.fixture.whenStable();

    const toggle = screen.getByRole('button', { name: /Tus pedidos de esta noche · 2/ });
    expect(toggle.textContent).toContain('K-4821 · ¡Está listo! Retiralo en la barra');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('opens the list of orders, each leading to its own tracking screen', async () => {
    const { rendered, http } = await openMenuHaving([
      ['K-4821', token],
      ['K-4830', otherToken],
    ]);
    http
      .expectOne(trackingUrl('bar-alfa', 'K-4830', otherToken))
      .flush(anOrder('K-4830', 'AwaitingPayment'));
    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(anOrder('K-4821', 'Ready'));
    await rendered.fixture.whenStable();

    fireEvent.click(screen.getByRole('button', { name: /Tus pedidos de esta noche/ }));
    await rendered.fixture.whenStable();

    expect(
      screen
        .getByRole('button', { name: /Tus pedidos de esta noche/ })
        .getAttribute('aria-expanded'),
    ).toBe('true');
    const links = screen.getAllByRole('link');
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      `/bar-alfa/orders/K-4821/${token}`,
      `/bar-alfa/orders/K-4830/${otherToken}`,
    ]);
    expect(links[1].textContent).toContain('Pagá en la caja con este código');
  });

  // Criterion 4: handed over, or a link that no longer leads anywhere.
  it('forgets, and does not show, an order the API no longer shows', async () => {
    const { rendered, http, myOrders } = await openMenuHaving([['K-4821', token]]);

    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(...notFound);
    await rendered.fixture.whenStable();

    expect(screen.queryByRole('link')).toBeNull();
    expect(myOrders.of('bar-alfa')).toEqual([]);
  });

  // Criterion 4: seen once, then gone.
  // Criterion 4: canceled, by the customer or at the till, is over. Nothing
  // to pick up and nothing to pay: it leaves the menu, and the phone forgets it.
  it('does not show, and forgets, a canceled order', async () => {
    const { rendered, http, myOrders } = await openMenuHaving([
      ['K-4821', token],
      ['K-4830', otherToken],
    ]);

    http
      .expectOne(trackingUrl('bar-alfa', 'K-4830', otherToken))
      .flush(anOrder('K-4830', 'Queued'));
    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).flush(anOrder('K-4821', 'Canceled'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('link', { name: /Tu pedido K-4830/ })).not.toBeNull();
    expect(screen.queryByText(/K-4821/)).toBeNull();
    expect(myOrders.of('bar-alfa')).toEqual([{ code: 'K-4830', token: otherToken }]);
  });

  // The wifi, not the order: the way back must not depend on the connection.
  it('still leads back to the order when its status could not be asked', async () => {
    const { rendered, http, myOrders } = await openMenuHaving([['K-4821', token]]);

    http.expectOne(trackingUrl('bar-alfa', 'K-4821', token)).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('link', { name: /Tu pedido K-4821/ }).textContent).toContain(
      'Tocá para ver cómo va',
    );
    expect(myOrders.of('bar-alfa')).toHaveLength(1);
  });
});
