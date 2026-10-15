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
import { VIP_TABLE_KEY_PREFIX, vipTableUrl } from '../../../core/vip-table/vip-tables';
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

  // US-24, criterion 1. The table's balance only opens with a VIP table's QR (US-41).
  it('offers paying from the phone or at the till, and not the table balance without a table', async () => {
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

    // US-35, criterion 3: a cart put together before closing and paid after.
    it('says in Spanish that the venue stopped taking orders', async () => {
      const said = await refusedWith({ type: 'urn:drinkit:problem:order:not-taking-orders' }, 409);

      expect(said).toContain('El boliche no está tomando pedidos en este momento.');
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

  describe('from a VIP table', () => {
    const CODE = 'a'.repeat(32);

    async function openForTheTable(balance: number) {
      store.write(VIP_TABLE_KEY_PREFIX + 'bar-alfa', CODE);
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
      anOrderOfTwoGins(cart);
      const http = TestBed.inject(HttpTestingController);

      // Answered before waiting: the screen is not stable while it asks.
      http.expectOne(vipTableUrl('bar-alfa', CODE)).flush({ tableNumber: 5, balance });
      await rendered.fixture.whenStable();

      return { rendered, http };
    }

    async function payAs(rendered: Awaited<ReturnType<typeof openForTheTable>>['rendered']) {
      fireEvent.input(name(), { target: { value: 'María Quadro' } });
      await rendered.fixture.whenStable();
      payButton().click();
      await rendered.fixture.whenStable();
    }

    // US-41, criterion 2: the balance pays, and nobody chooses a method.
    it("pays from the table's balance, with no method to choose", async () => {
      const { rendered, http } = await openForTheTable(20000);

      expect(screen.queryByLabelText(/pago digital/i)).toBeNull();
      expect(screen.queryByLabelText(/efectivo/i)).toBeNull();
      expect(screen.getByText(/disponible \$ 20\.000,00/i)).not.toBeNull();

      await payAs(rendered);

      const sent = http.expectOne(ordersUrl('bar-alfa'));
      expect(sent.request.body.method).toBe('VipBalance');
      expect(sent.request.body.tableCode).toBe(CODE);
    });

    // US-42 covers the difference; until then, the order has to fit.
    it('says what is missing when the balance falls short, and cannot be paid', async () => {
      await openForTheTable(5000);

      fireEvent.input(name(), { target: { value: 'María Quadro' } });

      expect(screen.getByRole('alert').textContent).toContain('faltan $ 4.000,00');
      expect(payButton().disabled).toBe(true);
    });

    // Criterion 3: another phone of the table spent first. The balance shown is refreshed.
    it('says the balance changed when the API refuses it, and shows it again', async () => {
      const { rendered, http } = await openForTheTable(20000);

      await payAs(rendered);
      http
        .expectOne(ordersUrl('bar-alfa'))
        .flush(
          { type: 'urn:drinkit:problem:vip-account:balance-short' },
          { status: 409, statusText: '' },
        );
      // Not whenStable: the balance is being asked for again, on purpose.
      rendered.fixture.detectChanges();
      TestBed.tick();

      expect(screen.getByRole('alert').textContent).toContain('Alguien de tu mesa pidió');
      http.expectOne(vipTableUrl('bar-alfa', CODE)).flush({ tableNumber: 5, balance: 3000 });
      await rendered.fixture.whenStable();
      expect(screen.getByText(/disponible \$ 3\.000,00/i)).not.toBeNull();
    });

    // Criterion 4: the night ended, or the waiter replaced the code, while paying.
    it("says the table's code no longer works when the API refuses it", async () => {
      const { rendered, http } = await openForTheTable(20000);

      await payAs(rendered);
      http
        .expectOne(ordersUrl('bar-alfa'))
        .flush(
          { type: 'urn:drinkit:problem:vip-account:not-found' },
          { status: 404, statusText: '' },
        );
      await rendered.fixture.whenStable();

      expect(screen.getByRole('alert').textContent).toContain('El código de tu mesa ya no sirve');
    });
  });
});
