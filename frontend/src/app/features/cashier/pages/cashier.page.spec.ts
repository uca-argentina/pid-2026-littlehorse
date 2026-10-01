import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import type { HubLinkState } from '../../../core/realtime/hub-channel';
import { OPEN_CAMERA, QrCamera } from '../../../shared/qr-camera/qr-camera';
import { TillChannel } from '../till-channel';
import type { CashierOrder } from '../cashier.service';
import {
  CASHIER_ORDERS_URL,
  CASHIER_SCAN_URL,
  MY_COLLECTIONS_URL,
  cancelAtTheTillUrl,
  cashierOrderUrl,
  collectUrl,
} from '../cashier.service';
import { CashierPage } from './cashier.page';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

function anOrder(overrides: Partial<CashierOrder> = {}): CashierOrder {
  return {
    code: 'K-4821',
    customerName: 'María Quadro',
    status: 'AwaitingPayment',
    total: 9000,
    placedAt: '2026-09-28T04:12:00Z',
    paidAt: null,
    items: [{ productName: 'Gin Tonic', quantity: 2, note: 'sin hielo', unitPrice: 4500 }],
    ...overrides,
  };
}

function aCollection(code: string, total: number): CashierOrder {
  return anOrder({ code, total, status: 'Queued', paidAt: '2026-09-28T04:15:00Z' });
}

/** Stands in for the till's hub: the test says when "Por cobrar" changed. */
class FakeTillChannel {
  readonly state = signal<HubLinkState>('connected');

  changed: (() => void) | null = null;

  disconnected = false;

  connect(onChanged: () => void): void {
    this.changed = onChanged;
  }

  disconnect(): void {
    this.disconnected = true;
  }
}

let channel: FakeTillChannel;

async function openTheTill(pending: CashierOrder[] | 'fails' = [], collected: CashierOrder[] = []) {
  const rendered = await render(CashierPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      // No camera in a test: the reads are handed to the component by hand.
      { provide: OPEN_CAMERA, useValue: () => new Promise<MediaStream>(() => undefined) },
      { provide: TillChannel, useFactory: () => (channel = new FakeTillChannel()) },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  if (pending === 'fails') http.expectOne(CASHIER_ORDERS_URL).error(new ProgressEvent('error'));
  else http.expectOne(CASHIER_ORDERS_URL).flush(pending);

  http.expectOne(MY_COLLECTIONS_URL).flush(collected);
  await rendered.fixture.whenStable();

  return { rendered, http };
}

type Rendered = Awaited<ReturnType<typeof openTheTill>>['rendered'];

/** Typed by hand or by a USB reader, which types and presses Enter just the same. */
async function enter(read: string, rendered: Rendered) {
  fireEvent.input(screen.getByLabelText(/escaneá o escribí el código/i), {
    target: { value: read },
  });
  fireEvent.submit(screen.getByRole('search'));
  await rendered.fixture.whenStable();
}

async function openFromTheList(code: string, rendered: Rendered) {
  fireEvent.click(screen.getByRole('button', { name: new RegExp(`cobrar ${code}`, 'i') }));
  await rendered.fixture.whenStable();
}

function theEntry(): HTMLInputElement {
  return screen.getByLabelText<HTMLInputElement>(/escaneá o escribí el código/i);
}

/** A USB reader: a keyboard that types the whole read and Enter in one burst. */
function readerTypes(read: string, on: Element) {
  for (const key of [...read, 'Enter'])
    on.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
}

function collectButton(): HTMLButtonElement {
  return screen.getByRole<HTMLButtonElement>('button', { name: /confirmar cobro/i });
}

describe('CashierPage', () => {
  // US-26, criterion 2: what the till still has to collect.
  it('lists the orders waiting to be paid', async () => {
    await openTheTill([anOrder(), anOrder({ code: 'K-4822', customerName: 'Juan Pérez' })]);

    const pending = screen.getByRole('list', { name: /por cobrar/i });

    expect(pending.textContent).toContain('K-4821');
    expect(pending.textContent).toContain('Juan Pérez');
    expect(pending.textContent).toContain('9.000,00');
  });

  it('says so when nobody is waiting to pay', async () => {
    await openTheTill([]);

    expect(screen.getByText(/no hay pedidos esperando cobro/i)).not.toBeNull();
  });

  it('offers to try again when the list cannot be loaded', async () => {
    await openTheTill('fails');

    expect(screen.getByRole('alert').textContent).toMatch(/no pudimos traer los pedidos/i);
    expect(screen.getByRole('button', { name: /reintentar/i })).not.toBeNull();
  });

  // The wireframe's "Cobros de tu turno", with what the drawer should hold.
  it('lists what this cashier collected during the shift, and the total', async () => {
    await openTheTill([], [aCollection('K-4830', 4500), aCollection('K-4829', 9000)]);

    const collected = screen.getByRole('list', { name: /cobros de tu turno/i });

    expect(collected.textContent).toContain('K-4830');
    expect(collected.textContent).toContain('K-4829');
    expect(screen.getByTestId('collected-total').textContent).toContain('13.500,00');
  });

  it('says so when nothing was collected yet in the shift', async () => {
    await openTheTill();

    expect(screen.getByText(/todavía no cobraste nada/i)).not.toBeNull();
  });

  // US-26, criterion 1: the drinks, what each costs, and what to charge.
  it('finds an order by its code and shows the drinks and what to charge', async () => {
    const { rendered, http } = await openTheTill();

    await enter('k-4821', rendered);
    http.expectOne(cashierOrderUrl('K-4821')).flush(anOrder());
    await rendered.fixture.whenStable();

    const order = screen.getByRole('region', { name: /pedido K-4821/i });

    expect(order.textContent).toContain('María Quadro');
    expect(order.textContent).toContain('Gin Tonic');
    expect(order.textContent).toContain('sin hielo');
    expect(order.textContent).toContain('Pendiente de pago');
    expect(screen.getByTestId('charge').textContent).toContain('9.000,00');
  });

  // A USB reader types the QR's token and presses Enter: sent in a body, never
  // in an address, because the token is the customer's proof.
  it('looks up what a reader read off the customer’s qr', async () => {
    const { rendered, http } = await openTheTill();

    await enter(token, rendered);
    const scan = http.expectOne({ method: 'POST', url: CASHIER_SCAN_URL });
    scan.flush(anOrder());
    await rendered.fixture.whenStable();

    expect(scan.request.body).toEqual({ read: token });
    expect(collectButton()).not.toBeNull();
  });

  it('reads the qr with the camera when asked to', async () => {
    const { rendered, http } = await openTheTill();

    fireEvent.click(screen.getByRole('button', { name: /escanear con la cámara/i }));
    await rendered.fixture.whenStable();
    const camera = rendered.fixture.debugElement.query(By.directive(QrCamera));
    (camera.componentInstance as QrCamera).read.emit(token);
    await rendered.fixture.whenStable();

    http.expectOne({ method: 'POST', url: CASHIER_SCAN_URL }).flush(anOrder());
    await rendered.fixture.whenStable();

    expect(collectButton()).not.toBeNull();
    expect(rendered.fixture.debugElement.query(By.directive(QrCamera))).toBeNull();
  });

  it('opens an order straight from the list, without typing its code', async () => {
    const { rendered } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);

    expect(collectButton()).not.toBeNull();
  });

  it('goes back to scanning without collecting', async () => {
    const { rendered } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    fireEvent.click(screen.getByRole('button', { name: /volver a escanear/i }));
    await rendered.fixture.whenStable();

    expect(screen.queryByRole('button', { name: /confirmar cobro/i })).toBeNull();
    expect(document.activeElement).toBe(screen.getByLabelText(/escaneá o escribí el código/i));
  });

  // A USB reader types wherever the cursor is: it has to be in the field
  // from the start, and back there after every step.
  it('puts the cursor where the reader types as soon as it opens', async () => {
    await openTheTill();

    expect(document.activeElement).toBe(theEntry());
  });

  it('puts the cursor back in the field after a lookup that found nothing', async () => {
    const { rendered, http } = await openTheTill();

    await enter('K-9999', rendered);
    theEntry().blur();
    http
      .expectOne(cashierOrderUrl('K-9999'))
      .flush({ type: ProblemTypes.cashierOrderNotFound }, { status: 404, statusText: 'Not Found' });
    await rendered.fixture.whenStable();

    expect(document.activeElement).toBe(theEntry());
  });

  // The cashier clicked something else and then scanned: the read still counts,
  // and its Enter must not press whatever button had the focus.
  it('takes a reader’s burst even when the cursor is somewhere else', async () => {
    const { rendered, http } = await openTheTill([anOrder({ code: 'K-4822' })]);
    const listButton = screen.getByRole('button', { name: /cobrar K-4822/i });
    listButton.focus();

    readerTypes(token, listButton);
    await rendered.fixture.whenStable();

    const scan = http.expectOne({ method: 'POST', url: CASHIER_SCAN_URL });
    expect(scan.request.body).toEqual({ read: token });
    expect(screen.queryByRole('region', { name: /pedido K-4822/i })).toBeNull();
  });

  it('does not mistake typing in the filter for a reader', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);
    const filter = screen.getByLabelText<HTMLInputElement>(/filtrar por código o nombre/i);
    filter.focus();

    readerTypes('maria', filter);
    await rendered.fixture.whenStable();

    http.expectNone(CASHIER_SCAN_URL);
    http.expectNone(cashierOrderUrl('MARIA'));
  });

  // "Por cobrar" gets long on a busy night: filter it by code or by name.
  it('filters what is waiting by code or by name, accents or not', async () => {
    const { rendered } = await openTheTill([
      anOrder(),
      anOrder({ code: 'K-4822', customerName: 'Juan Pérez' }),
    ]);

    fireEvent.input(screen.getByLabelText(/filtrar por código o nombre/i), {
      target: { value: 'maria' },
    });
    await rendered.fixture.whenStable();

    const pending = screen.getByRole('list', { name: /por cobrar/i });
    expect(pending.textContent).toContain('K-4821');
    expect(pending.textContent).not.toContain('K-4822');

    fireEvent.input(screen.getByLabelText(/filtrar por código o nombre/i), {
      target: { value: '4822' },
    });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('list', { name: /por cobrar/i }).textContent).toContain('Juan Pérez');
  });

  it('says so when nothing waiting matches the filter', async () => {
    const { rendered } = await openTheTill([anOrder()]);

    fireEvent.input(screen.getByLabelText(/filtrar por código o nombre/i), {
      target: { value: 'nadie' },
    });
    await rendered.fixture.whenStable();

    expect(screen.getByText(/ningún pedido coincide con “nadie”/i)).not.toBeNull();
  });

  // "Por cobrar" updates on its own: a customer confirmed paying in cash, or
  // another till collected one, and nobody touched this screen.
  it('reloads what waits for cash when the till hears it changed', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    channel.changed!();
    TestBed.tick();
    http
      .expectOne(CASHIER_ORDERS_URL)
      .flush([anOrder(), anOrder({ code: 'K-4822', customerName: 'Juan Pérez' })]);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('list', { name: /por cobrar/i }).textContent).toContain('Juan Pérez');
  });

  it('says so while it cannot hear the till’s updates', async () => {
    const { rendered } = await openTheTill();

    channel.state.set('reconnecting');
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/sin conexión.*reintentando/i);
  });

  it('lets go of the connection when the screen closes', async () => {
    const { rendered } = await openTheTill();

    rendered.fixture.destroy();

    expect(channel.disconnected).toBe(true);
  });

  // US-26, criterion 2: collected, handed to the bar, off one list and onto the other.
  it('collects the order and moves it from one list to the other', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    collectButton().click();
    await rendered.fixture.whenStable();

    http
      .expectOne({ method: 'POST', url: collectUrl('K-4821') })
      .flush(null, { status: 204, statusText: 'No Content' });
    TestBed.tick();
    http.expectOne(CASHIER_ORDERS_URL).flush([]);
    http.expectOne(MY_COLLECTIONS_URL).flush([aCollection('K-4821', 9000)]);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/K-4821.*cobrado/i);
    expect(screen.queryByRole('button', { name: /confirmar cobro/i })).toBeNull();
    expect(screen.getByText(/no hay pedidos esperando cobro/i)).not.toBeNull();
    expect(screen.getByRole('list', { name: /cobros de tu turno/i }).textContent).toContain(
      'K-4821',
    );
  });

  // US-23: the customer left without paying. Asked first — it cannot be undone.
  it('asks before canceling an order, and sends nothing yet', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    fireEvent.click(screen.getByRole('button', { name: /^cancelar pedido$/i }));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('button', { name: /sí, cancelar pedido/i })).not.toBeNull();
    http.expectNone(cancelAtTheTillUrl('K-4821'));
  });

  it('keeps the order on screen when the cashier changes their mind', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    fireEvent.click(screen.getByRole('button', { name: /^cancelar pedido$/i }));
    await rendered.fixture.whenStable();
    fireEvent.click(screen.getByRole('button', { name: /^no$/i }));
    await rendered.fixture.whenStable();

    expect(collectButton()).not.toBeNull();
    expect(screen.queryByRole('button', { name: /sí, cancelar pedido/i })).toBeNull();
    http.expectNone(cancelAtTheTillUrl('K-4821'));
  });

  it('cancels the order and takes it off what waits for cash', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    fireEvent.click(screen.getByRole('button', { name: /^cancelar pedido$/i }));
    await rendered.fixture.whenStable();
    fireEvent.click(screen.getByRole('button', { name: /sí, cancelar pedido/i }));
    await rendered.fixture.whenStable();

    http
      .expectOne({ method: 'POST', url: cancelAtTheTillUrl('K-4821') })
      .flush(null, { status: 204, statusText: 'No Content' });
    TestBed.tick();
    http.expectOne(CASHIER_ORDERS_URL).flush([]);
    http.expectOne(MY_COLLECTIONS_URL).flush([]);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/K-4821.*cancelado/i);
    expect(screen.queryByRole('button', { name: /confirmar cobro/i })).toBeNull();
    expect(screen.getByText(/no hay pedidos esperando cobro/i)).not.toBeNull();
  });

  // Another till collected it a moment before: it is the bar's now.
  it('says the order was already paid when canceling it is refused', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    fireEvent.click(screen.getByRole('button', { name: /^cancelar pedido$/i }));
    await rendered.fixture.whenStable();
    fireEvent.click(screen.getByRole('button', { name: /sí, cancelar pedido/i }));
    await rendered.fixture.whenStable();

    http
      .expectOne(cancelAtTheTillUrl('K-4821'))
      .flush({ type: ProblemTypes.cashierAlreadyPaid }, { status: 409, statusText: 'Conflict' });
    TestBed.tick();
    http.expectOne(CASHIER_ORDERS_URL).flush([]);
    http.expectOne(MY_COLLECTIONS_URL).flush([]);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/ya está pago/i);
  });

  // The customer canceled it from the phone and still showed the code.
  it('says a canceled order has nothing to collect', async () => {
    const { rendered, http } = await openTheTill();

    await enter('K-4821', rendered);
    http.expectOne(cashierOrderUrl('K-4821')).flush(anOrder({ status: 'Canceled' }));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/K-4821 fue cancelado/i);
    expect(screen.queryByRole('button', { name: /confirmar cobro/i })).toBeNull();
  });

  // US-26, criterion 3.
  it('says clearly when no order has that code', async () => {
    const { rendered, http } = await openTheTill();

    await enter('K-9999', rendered);
    http
      .expectOne(cashierOrderUrl('K-9999'))
      .flush({ type: ProblemTypes.cashierOrderNotFound }, { status: 404, statusText: 'Not Found' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(
      /no hay ningún pedido con el código K-9999/i,
    );
  });

  it('says clearly when a qr belongs to no order of this venue', async () => {
    const { rendered, http } = await openTheTill();

    await enter(token, rendered);
    http
      .expectOne(CASHIER_SCAN_URL)
      .flush({ type: ProblemTypes.cashierOrderNotFound }, { status: 404, statusText: 'Not Found' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/ese qr no es de ningún pedido/i);
  });

  // US-26, criterion 3: showing the code a second time is not a second charge.
  it('says clearly when the order was already paid, and does not offer to collect it', async () => {
    const { rendered, http } = await openTheTill();

    await enter('K-4821', rendered);
    http
      .expectOne(cashierOrderUrl('K-4821'))
      .flush(anOrder({ status: 'Queued', paidAt: '2026-09-28T04:15:00Z' }));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/ya está pago desde las \d\d:\d\d\. /i);
    expect(screen.queryByRole('button', { name: /confirmar cobro/i })).toBeNull();
  });

  // The wireframe's double tap: the second one is refused by the API.
  it('says the order was already paid when collecting it again is refused', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    collectButton().click();
    await rendered.fixture.whenStable();

    http
      .expectOne(collectUrl('K-4821'))
      .flush({ type: ProblemTypes.cashierAlreadyPaid }, { status: 409, statusText: 'Conflict' });
    TestBed.tick();
    http.expectOne(CASHIER_ORDERS_URL).flush([]);
    http.expectOne(MY_COLLECTIONS_URL).flush([]);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/ya está pago/i);
  });

  it('does not collect twice on a double tap', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    collectButton().click();
    await rendered.fixture.whenStable();

    expect(screen.getByRole<HTMLButtonElement>('button', { name: /confirmando/i }).disabled).toBe(
      true,
    );
    http.expectOne(collectUrl('K-4821'));
  });

  it('says the connection failed when collecting never got an answer', async () => {
    const { rendered, http } = await openTheTill([anOrder()]);

    await openFromTheList('K-4821', rendered);
    collectButton().click();
    await rendered.fixture.whenStable();

    http.expectOne(collectUrl('K-4821')).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/no pudimos confirmar el cobro/i);
    expect(collectButton().disabled).toBe(false);
  });
});
