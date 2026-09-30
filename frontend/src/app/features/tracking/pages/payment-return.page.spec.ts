import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { Cart } from '../../../core/cart/cart';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { paymentReturnUrl } from '../payment-return.service';
import { PaymentReturnPage } from './payment-return.page';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const url = paymentReturnUrl('bar-alfa', 'K-4821', token);

/** Mercado Pago appends what happened to the return address; "null" when nothing did. */
async function comeBackWith(paymentId: string | undefined) {
  const store = new StoreInMemory();
  const rendered = await render(PaymentReturnPage, {
    inputs: { venueSlug: 'bar-alfa', code: 'K-4821', token, paymentId: paymentId ?? null },
    providers: [
      provideRouter([{ path: '**', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
    ],
  });

  const cart = TestBed.inject(Cart);
  cart.open('bar-alfa');
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });

  return {
    rendered,
    cart,
    http: TestBed.inject(HttpTestingController),
    navigate: vi.spyOn(TestBed.inject(Router), 'navigate'),
  };
}

describe('PaymentReturnPage', () => {
  it('tells the api which payment it came back with', async () => {
    const { http } = await comeBackWith('123456');

    expect(http.expectOne(url).request.body).toEqual({ paymentId: '123456' });
  });

  // Back without paying: Mercado Pago writes "null" for a payment that never was.
  it('tells the api it came back with no payment when mercado pago says null', async () => {
    const { http } = await comeBackWith('null');

    expect(http.expectOne(url).request.body).toEqual({ paymentId: null });
  });

  // Paid: the order is at the bar now, and the phone has nothing left to pay.
  it('goes on to the order and empties the cart once it is paid', async () => {
    const { rendered, http, cart, navigate } = await comeBackWith('123456');

    http.expectOne(url).flush({ status: 'Queued' });
    await rendered.fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/', 'bar-alfa', 'orders', 'K-4821', token], {
      replaceUrl: true,
    });
    expect(cart.lines()).toHaveLength(0);
  });

  // Under review, or cash to pay at a Rapipago: the order screen says it waits.
  it('goes on to the order while the payment is still pending', async () => {
    const { rendered, http, navigate } = await comeBackWith('123456');

    http.expectOne(url).flush({ status: 'AwaitingPayment' });
    await rendered.fixture.whenStable();

    expect(navigate).toHaveBeenCalled();
  });

  // Nothing was charged, and the drinks they chose are still here to try again.
  it('says the payment did not go through and offers to try again with the same order', async () => {
    const { rendered, http, cart } = await comeBackWith('null');

    http.expectOne(url).flush({ status: 'Canceled' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/no se complet[óo]/i);
    expect(screen.getByRole('link', { name: /volver a intentar/i }).getAttribute('href')).toBe(
      '/bar-alfa/checkout',
    );
    expect(cart.lines()).toHaveLength(1);
  });

  it('says the money arrived too late and will be given back', async () => {
    const { rendered, http } = await comeBackWith('123456');

    http
      .expectOne(url)
      .flush(
        { type: 'urn:drinkit:problem:payment:paid-after-cancel' },
        { status: 409, statusText: 'Conflict' },
      );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/devol/i);
  });

  it('says the link leads nowhere when the api does not know it', async () => {
    const { rendered, http } = await comeBackWith('123456');

    http
      .expectOne(url)
      .flush(
        { type: 'urn:drinkit:problem:payment:order-not-found' },
        { status: 404, statusText: 'Not Found' },
      );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/no lleva a ning[úu]n pedido/i);
  });

  // The signal dropped on the way back: the payment is fine, asking again is the answer.
  it('offers to ask again when the answer never arrives', async () => {
    const { rendered, http } = await comeBackWith('123456');

    http.expectOne(url).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();
    screen.getByRole('button', { name: /reintentar/i }).click();

    http.expectOne(url);
  });
});
