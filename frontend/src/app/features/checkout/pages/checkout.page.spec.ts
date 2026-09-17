import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Cart } from '../../../core/cart/cart';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { PROCESSING_PAUSE_MS } from '../checkout.store';
import { ordersUrl } from '../checkout.service';
import { CheckoutPage } from './checkout.page';

let store: StoreInMemory;

/** Nothing added: the screen as somebody reaches it with an empty order. */
const nothing = (): void => {
  // The order is whatever the store held, which in this case is nothing.
};

async function openScreenWith(fill: (cart: Cart) => void) {
  const rendered = await render(CheckoutPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
      { provide: PROCESSING_PAUSE_MS, useValue: 0 },
    ],
  });

  const cart = TestBed.inject(Cart);
  cart.open('bar-alfa');
  fill(cart);
  await rendered.fixture.whenStable();

  return { rendered, http: TestBed.inject(HttpTestingController) };
}

function anOrderOfTwoGins(cart: Cart): void {
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });
  cart.setNote('id-1', 'sin hielo');
}

function name(): HTMLInputElement {
  return screen.getByLabelText<HTMLInputElement>(/nombre/i);
}

function payButton(): HTMLButtonElement {
  return screen.getByRole<HTMLButtonElement>('button', { name: /pagar/i });
}

describe('CheckoutPage', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  it('shows what is being paid for, with its notes', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(screen.getByTestId('line-id-1').textContent).toContain('2×');
    expect(screen.getByTestId('line-id-1').textContent).toContain('Gin Tonic');
    expect(screen.getByTestId('line-id-1').textContent).toContain('sin hielo');
    expect(screen.getByTestId('total').textContent).toContain('9.000,00');
  });

  // Criterion 3: no name, no order. The button says so before it is tapped
  // rather than after a round trip.
  it('cannot be paid without a name', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(payButton().disabled).toBe(true);
  });

  it('cannot be paid with only a first name', async () => {
    await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'Euge' } });

    expect(payButton().disabled).toBe(true);
  });

  it('can be paid with a full name', async () => {
    await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'María Quadro' } });

    expect(payButton().disabled).toBe(false);
  });

  it('sends the order when it is paid', async () => {
    const { rendered, http } = await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'María Quadro' } });
    await rendered.fixture.whenStable();
    payButton().click();
    await rendered.fixture.whenStable();

    const sent = http.expectOne(ordersUrl('bar-alfa'));

    expect(sent.request.body.customerName).toBe('María Quadro');
    expect(sent.request.body.method).toBe('Digital');
  });

  // The only one that is built. The other two are drawn, so the screen is the
  // one somebody will learn, and switched off with the reason showing.
  it('offers only the way of paying that exists', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(screen.getByLabelText<HTMLInputElement>(/pago digital/i).disabled).toBe(false);
    expect(screen.getByLabelText<HTMLInputElement>(/efectivo/i).disabled).toBe(true);
    expect(screen.getByLabelText<HTMLInputElement>(/saldo de la mesa/i).disabled).toBe(true);
  });

  it('says it is working while the payment is going through', async () => {
    const { rendered } = await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'María Quadro' } });
    await rendered.fixture.whenStable();
    payButton().click();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/procesando/i);
  });

  // A drink ran out while they were deciding. The order is still on the phone
  // and the way out is back to it, not a retry that would fail the same way.
  it('says which drink ran out and sends them back to fix it', async () => {
    const { rendered, http } = await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'María Quadro' } });
    await rendered.fixture.whenStable();
    payButton().click();
    await rendered.fixture.whenStable();

    http.expectOne(ordersUrl('bar-alfa')).flush(
      {
        type: 'urn:drinkit:problem:order:sold-out',
        detail: 'Gin Tonic ran out while you were ordering.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Gin Tonic');
    expect(
      screen
        .getByText(/revisar el pedido/i)
        .closest('a')
        ?.getAttribute('href'),
    ).toBe('/bar-alfa/order');
  });

  // Nothing to pay for. Reached by typing the address, or by coming back to a
  // tab whose order was confirmed on another one.
  it('says there is nothing to pay for when the order is empty', async () => {
    await openScreenWith(nothing);

    expect(screen.getByRole('status').textContent).toMatch(/no hay nada/i);
    expect(screen.queryByRole('button', { name: /pagar/i })).toBeNull();
  });
});
