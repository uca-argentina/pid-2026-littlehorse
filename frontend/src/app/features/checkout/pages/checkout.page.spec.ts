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

  // US-24, criterion 1. The VIP tables are out of this sprint: drawn, so the
  // screen is the one somebody will learn, and switched off.
  it('offers paying from the phone or at the till, and not the table balance yet', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(screen.getByLabelText<HTMLInputElement>(/pago digital/i).disabled).toBe(false);
    expect(screen.getByLabelText<HTMLInputElement>(/efectivo/i).disabled).toBe(false);
    expect(screen.getByLabelText<HTMLInputElement>(/saldo de la mesa/i).disabled).toBe(true);
  });

  it('starts on paying from the phone', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(screen.getByLabelText<HTMLInputElement>(/pago digital/i).checked).toBe(true);
  });

  // US-24, criterion 3: nothing is paid on the phone, so the button does not
  // say "pagar" — it confirms the order and the money changes hands at the till.
  it('confirms the order instead of paying when cash is chosen', async () => {
    await openScreenWith(anOrderOfTwoGins);

    fireEvent.click(screen.getByLabelText(/efectivo/i));

    expect(screen.queryByRole('button', { name: /pagar/i })).toBeNull();
    expect(screen.getByRole('button', { name: /confirmar pedido/i })).not.toBeNull();
  });

  it('sends cash as the way of paying when it is chosen', async () => {
    const { rendered, http } = await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'María Quadro' } });
    fireEvent.click(screen.getByLabelText(/efectivo/i));
    await rendered.fixture.whenStable();
    screen.getByRole<HTMLButtonElement>('button', { name: /confirmar pedido/i }).click();
    await rendered.fixture.whenStable();

    expect(http.expectOne(ordersUrl('bar-alfa')).request.body.method).toBe('Cash');
  });

  // US-24: the payment happens on Mercado Pago's page, and the button says so.
  it('offers to pay with Mercado Pago', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(payButton().textContent).toMatch(/pagar con mercado pago/i);
    expect(payButton().getAttribute('data-mp-checkout-cta')).toBe('checkout-pro');
  });

  // It is not simulated any more: saying so would be a lie.
  it('no longer says the payment is simulated', async () => {
    await openScreenWith(anOrderOfTwoGins);

    expect(screen.queryByText(/simulado/i)).toBeNull();
  });

  it('says the payment could not be started when Mercado Pago does not answer', async () => {
    const { rendered, http } = await openScreenWith(anOrderOfTwoGins);

    fireEvent.input(name(), { target: { value: 'María Quadro' } });
    await rendered.fixture.whenStable();
    payButton().click();
    await rendered.fixture.whenStable();
    http
      .expectOne(ordersUrl('bar-alfa'))
      .flush(
        { type: 'urn:drinkit:problem:payment:gateway-unavailable' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/mercado pago no respondi[óo]/i);
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
        productName: 'Gin Tonic',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Se acabó el Gin Tonic');
    expect(
      screen
        .getByText(/revisar el pedido/i)
        .closest('a')
        ?.getAttribute('href'),
    ).toBe('/bar-alfa/order');
  });

  // The API's detail is English, for developers: a customer in a boliche reads
  // Spanish, whatever went wrong, and never the sentence meant for the logs.
  describe('when the order is refused', () => {
    async function refusedWith(problem: Record<string, string>, status = 400) {
      const { rendered, http } = await openScreenWith(anOrderOfTwoGins);

      fireEvent.input(name(), { target: { value: 'María Quadro' } });
      await rendered.fixture.whenStable();
      payButton().click();
      await rendered.fixture.whenStable();

      http
        .expectOne(ordersUrl('bar-alfa'))
        .flush(
          { detail: 'An English sentence for the logs.', ...problem },
          { status, statusText: '' },
        );
      await rendered.fixture.whenStable();

      return screen.getByRole('alert').textContent ?? '';
    }

    it.each([
      ['urn:drinkit:problem:order:sold-out', 'Se acabó el Gin Tonic mientras pedías.'],
      ['urn:drinkit:problem:order:not-on-the-menu', 'El Gin Tonic ya no está en la carta.'],
    ])('names the drink in Spanish (%s)', async (type, sentence) => {
      const said = await refusedWith({ type, productName: 'Gin Tonic' }, 409);

      expect(said).toContain(sentence);
      expect(said).not.toContain('English');
    });

    // An old API, or a drink it could not name: still Spanish, just vaguer.
    it('still says it in Spanish when the drink is not named', async () => {
      const said = await refusedWith({ type: 'urn:drinkit:problem:order:sold-out' }, 409);

      expect(said).toContain('Se acabó uno de los tragos mientras pedías.');
    });

    it('says in Spanish that somebody took the last one', async () => {
      const said = await refusedWith({ type: 'urn:drinkit:problem:order:stock-moved' }, 409);

      expect(said).toContain('Alguien pidió al mismo tiempo');
    });

    it.each([
      ['urn:drinkit:problem:order:name-required', 'Necesitamos un nombre'],
      ['urn:drinkit:problem:order:name-needs-surname', 'Poné tu nombre y tu apellido.'],
      ['urn:drinkit:problem:order:name-only-letters', 'El nombre sólo puede tener letras.'],
      ['urn:drinkit:problem:order:name-too-long', 'El nombre es demasiado largo.'],
    ])('says what is wrong with the name in Spanish (%s)', async (type, sentence) => {
      const said = await refusedWith({ type });

      expect(said).toContain(sentence);
      expect(said).not.toContain('English');
    });

    it('says something in Spanish for a refusal it has no sentence for', async () => {
      const said = await refusedWith({ type: 'urn:drinkit:problem:order:something-new' });

      expect(said).toContain('No pudimos confirmar tu pedido.');
      expect(said).not.toContain('English');
    });
  });

  // Nothing to pay for. Reached by typing the address, or by coming back to a
  // tab whose order was confirmed on another one.
  it('says there is nothing to pay for when the order is empty', async () => {
    await openScreenWith(nothing);

    expect(screen.getByRole('status').textContent).toMatch(/no hay nada/i);
    expect(screen.queryByRole('button', { name: /pagar/i })).toBeNull();
  });
});
