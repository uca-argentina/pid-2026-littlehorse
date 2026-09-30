import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen, within } from '@testing-library/angular';
import { SessionStorage } from '../../../core/auth/session-storage';
import { KDS_RETRY_MS, KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import type { KdsLinkState } from '../../../core/kds/kds-board-channel';
import {
  deliverUrl,
  markReadyUrl,
  returnToPreparationUrl,
  returnToQueueUrl,
  startPreparingUrl,
  undoDeliveryUrl,
} from '../kds-orders.service';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsQueueOrder } from '../kds-queue';
import { KdsBoardPage } from './kds-board.page';

/** Captures the callback the page hands the channel, instead of opening a real connection. */
class FakeKdsBoardChannel {
  readonly state = signal<KdsLinkState>('connected');

  onChanged: (() => void) | null = null;

  connect(onChanged: () => void): void {
    this.onChanged = onChanged;
  }

  disconnect(): void {
    this.onChanged = null;
  }
}

function anOrder(overrides: Partial<KdsQueueOrder> = {}): KdsQueueOrder {
  return {
    code: 'K-4821',
    customerName: 'María Quadro',
    status: 'Queued',
    paidAt: new Date().toISOString(),
    lastModifiedAt: null,
    isForTable: false,
    orderItems: [{ productName: 'Gin Tonic', quantity: 2, note: null }],
    ...overrides,
  };
}

/**
 * The board only ever opens with the station signed in: the route's guards see
 * to it. Stored the way SessionStorage reads it back, before the screen exists.
 */
function signedInAsTheStation(): void {
  sessionStorage.setItem(
    'drinkit.staff-session',
    JSON.stringify({
      token: 'un-token',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      username: 'barra.demo',
      role: 'Kds',
    }),
  );
}

afterEach(() => sessionStorage.clear());

async function openScreen() {
  signedInAsTheStation();
  const channel = new FakeKdsBoardChannel();

  const rendered = await render(KdsBoardPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: KdsBoardChannel, useValue: channel },
    ],
  });

  return { rendered, channel, http: TestBed.inject(HttpTestingController) };
}

async function openScreenShowing(orders: KdsQueueOrder[]) {
  const { rendered, channel, http } = await openScreen();

  http.expectOne(KDS_QUEUE_URL).flush(orders);
  await rendered.fixture.whenStable();

  return { rendered, channel, http };
}

describe('KdsBoardPage', () => {
  // Criterion 1: three columns, and a new order lands in the right one.
  it('shows a new order under Nuevos', async () => {
    await openScreenShowing([anOrder({ status: 'Queued' })]);

    expect(screen.getByText('Nuevos').closest('section')?.textContent).toContain('K-4821');
  });

  it('shows an order being made under En preparación', async () => {
    await openScreenShowing([anOrder({ status: 'InPreparation' })]);

    expect(screen.getByText('En preparación').closest('section')?.textContent).toContain('K-4821');
  });

  it('shows a finished order under Listos en la barra', async () => {
    await openScreenShowing([anOrder({ status: 'Ready' })]);

    expect(screen.getByText('Listos en la barra').closest('section')?.textContent).toContain(
      'K-4821',
    );
  });

  it('shows who ordered, the drinks with quantity and note, and the wait', async () => {
    await openScreenShowing([
      anOrder({
        customerName: 'Nico',
        orderItems: [{ productName: 'Aperol Spritz', quantity: 1, note: 'con mucho hielo' }],
      }),
    ]);

    expect(screen.getByText('Nico')).not.toBeNull();
    expect(screen.getByText('1×')).not.toBeNull();
    expect(screen.getByText('Aperol Spritz')).not.toBeNull();
    expect(screen.getByText('con mucho hielo')).not.toBeNull();
    expect(screen.getByText('0 min')).not.toBeNull();
  });

  // Criterion 1: barra vs. mesa, from the payment method.
  it('labels a bar order Barra and a table order Mesa', async () => {
    await openScreenShowing([
      anOrder({ code: 'K-0001', isForTable: false }),
      anOrder({ code: 'K-0002', isForTable: true }),
    ]);

    expect(screen.getByText('K-0001').closest('.card')?.textContent).toContain('Barra');
    expect(screen.getByText('K-0002').closest('.card')?.textContent).toContain('Mesa');
  });

  // Criterion 2: the three age bands, at the boundaries the criterion names.
  it.each([
    [4, 'ok' as const, '4 min'],
    [5, 'warn' as const, '5 min'],
    [10, 'urg' as const, '10 min'],
  ])('paints a %s-minute wait as %s', async (minutesAgo, band, label) => {
    const paidAt = new Date(Date.now() - Number(minutesAgo) * 60_000).toISOString();

    await openScreenShowing([anOrder({ paidAt })]);

    const card = screen.getByText('K-4821').closest('.card');

    expect(card?.classList.contains(band === 'ok' ? 'card' : band)).toBe(true);
    expect(card?.textContent).toContain(label);
  });

  // Criterion 4.
  it('says the queue is empty instead of showing nothing', async () => {
    await openScreenShowing([]);

    expect(screen.getByText('La cola está vacía.')).not.toBeNull();
  });

  it('says so when the queue cannot be loaded', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(KDS_QUEUE_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert')).not.toBeNull();
  });

  it('draws the outline of the three columns while the queue loads', async () => {
    await openScreen();

    const skeleton = screen.getByTestId('kds-skeleton');
    expect(skeleton.closest('[aria-busy="true"]')).not.toBeNull();
    expect(skeleton.textContent).toContain('Nuevos');
    expect(skeleton.textContent).toContain('En preparación');
    expect(skeleton.textContent).toContain('Listos en la barra');
    expect(screen.getByRole('status').textContent).toContain('Cargando');
  });

  it('drops the outline once the queue arrives', async () => {
    await openScreenShowing([anOrder()]);

    expect(screen.queryByTestId('kds-skeleton')).toBeNull();
    expect(document.querySelector('[aria-busy="true"]')).toBeNull();
  });

  // A shimmer says "on its way"; after a failure the warning says what is true.
  it('drops the outline when the first load fails', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(KDS_QUEUE_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.queryByTestId('kds-skeleton')).toBeNull();
  });

  describe('when the first load takes long', () => {
    beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));
    afterEach(() => vi.useRealTimers());

    // A cold start of the API takes most of a minute: an outline that sits
    // still that long looks like a frozen tablet.
    it('says it is still on it after a few seconds', async () => {
      const { rendered } = await openScreen();

      expect(screen.getByRole('status').textContent).not.toContain('tardando');

      vi.advanceTimersByTime(5000);
      rendered.fixture.detectChanges();

      expect(screen.getByRole('status').textContent).toContain('tardando');
    });
  });

  // Settles a failed request and lets the page react, without whenStable:
  // these tests run on fake timers, and the retry is exactly a timer.
  async function failNextLoad(http: HttpTestingController, fixture: { detectChanges(): void }) {
    http.expectOne(KDS_QUEUE_URL).flush('', { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
  }

  // Fake timers go on only once the screen is open: whenStable, which opening
  // it waits on, never settles under them.
  describe('when a reload fails', () => {
    afterEach(() => vi.useRealTimers());

    // Mid-service, a blip on one reload must not wipe what the bar is making.
    it('keeps the cards on screen and says the queue could not be refreshed', async () => {
      const { rendered, channel, http } = await openScreenShowing([anOrder()]);
      vi.useFakeTimers();

      channel.onChanged?.();
      rendered.fixture.detectChanges();
      await failNextLoad(http, rendered.fixture);

      expect(screen.getByText('K-4821')).not.toBeNull();
      expect(screen.getByRole('alert').textContent).toContain('No pudimos actualizar la cola');
    });

    // Nobody behind the bar has a free hand to tap "Reintentar".
    it('retries on its own after a failed reload', async () => {
      const { rendered, channel, http } = await openScreenShowing([anOrder()]);
      vi.useFakeTimers();

      channel.onChanged?.();
      rendered.fixture.detectChanges();
      await failNextLoad(http, rendered.fixture);

      http.expectNone(KDS_QUEUE_URL);
      await vi.advanceTimersByTimeAsync(KDS_RETRY_MS);
      rendered.fixture.detectChanges();

      http.expectOne(KDS_QUEUE_URL);
    });

    it('drops the warning once a retry succeeds', async () => {
      const { rendered, channel, http } = await openScreenShowing([anOrder()]);
      vi.useFakeTimers();

      channel.onChanged?.();
      rendered.fixture.detectChanges();
      await failNextLoad(http, rendered.fixture);
      await vi.advanceTimersByTimeAsync(KDS_RETRY_MS);
      rendered.fixture.detectChanges();

      http.expectOne(KDS_QUEUE_URL).flush([anOrder()]);
      await vi.advanceTimersByTimeAsync(0);
      rendered.fixture.detectChanges();

      expect(screen.queryByRole('alert')).toBeNull();
      expect(screen.getByText('K-4821')).not.toBeNull();
    });

    // A tablet switched on before the wifi came up must not stay dead.
    it('retries on its own when the very first load fails', async () => {
      const { rendered, http } = await openScreen();
      vi.useFakeTimers();

      await failNextLoad(http, rendered.fixture);
      await vi.advanceTimersByTimeAsync(KDS_RETRY_MS);
      rendered.fixture.detectChanges();

      http.expectOne(KDS_QUEUE_URL);
    });
  });

  // The wireframe's header: which venue, and which station's tablet this is.
  it('shows the venue and the station account in the header', async () => {
    const { rendered } = await openScreenShowing([]);

    rendered.fixture.debugElement.injector.get(SessionStorage).remember({
      token: 'un-token',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      username: 'barra-principal',
      role: 'Kds',
    });
    await rendered.fixture.whenStable();

    const header = screen.getByRole('banner');
    expect(header.textContent).toContain('bar-alfa');
    expect(header.textContent).toContain('barra-principal');
  });

  // US-15: this is the whole point of SignalR — a new order shows up without
  // anybody touching the tablet.
  it('reloads the queue when the channel says the board changed', async () => {
    const { rendered, channel, http } = await openScreenShowing([]);

    channel.onChanged?.();
    rendered.fixture.detectChanges();

    http.expectOne(KDS_QUEUE_URL).flush([anOrder()]);
    await rendered.fixture.whenStable();

    expect(screen.getByText('K-4821')).not.toBeNull();
  });

  // A new order must not blank the board: with orders arriving all night the
  // tablet would flash "Cargando" every time somebody pays.
  it('keeps the orders on screen while it reloads', async () => {
    const { rendered, channel } = await openScreenShowing([anOrder()]);

    channel.onChanged?.();
    rendered.fixture.detectChanges();

    expect(screen.getByText('K-4821')).not.toBeNull();
    expect(screen.queryByText(/cargando/i)).toBeNull();
  });

  // What is on screen may be stale while the link is down, and the bartender
  // has to know it rather than trust a board that stopped listening.
  it('warns that it lost the connection while the channel is reconnecting', async () => {
    const { rendered, channel } = await openScreenShowing([anOrder()]);

    channel.state.set('reconnecting');
    rendered.fixture.detectChanges();

    expect(screen.getByRole('status').textContent).toContain('Sin conexión');
    expect(screen.getByText('K-4821')).not.toBeNull();
  });

  it('shows no warning while connected', async () => {
    await openScreenShowing([anOrder()]);

    expect(screen.queryByText(/sin conexión/i)).toBeNull();
  });

  // US-32: nobody navigates on a tablet behind the bar, so when the session
  // ends — the API or the hub refused the token — the board itself goes to
  // sign in, saying why, instead of sitting on a queue it can no longer read.
  it('sends the station to sign in again when its session expires', async () => {
    const { rendered } = await openScreenShowing([anOrder()]);
    const sessions = rendered.fixture.debugElement.injector.get(SessionStorage);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    TestBed.tick();

    sessions.forget('expired');
    TestBed.tick();

    expect(navigate).toHaveBeenCalledWith(['/', 'bar-alfa', 'staff', 'login'], {
      queryParams: { expired: true },
    });
  });

  it('disconnects the channel when the screen closes', async () => {
    const { rendered, channel } = await openScreenShowing([]);

    rendered.fixture.destroy();

    expect(channel.onChanged).toBeNull();
  });

  // US-16: taking orders off Nuevos, one or several, and handing one back.
  describe('taking orders', () => {
    const gin = (quantity: number) => ({ productName: 'Gin Tonic', quantity, note: null });
    const fernet = (quantity: number) => ({ productName: 'Fernet con Coca', quantity, note: null });

    async function settle(fixture: { whenStable(): Promise<unknown> }) {
      await fixture.whenStable();
    }

    // Criterion 1.
    it('takes an order when its Preparar is pressed', async () => {
      const { http } = await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Preparar K-4821' }));

      expect(http.expectOne(startPreparingUrl('K-4821')).request.method).toBe('POST');
    });

    it('reloads the queue once the order is taken', async () => {
      const { http } = await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Preparar K-4821' }));
      http
        .expectOne(startPreparingUrl('K-4821'))
        .flush(null, { status: 204, statusText: 'No Content' });
      // Not whenStable: it would wait on the very reload this test is looking for.
      TestBed.tick();

      http.expectOne(KDS_QUEUE_URL);
    });

    // Criterion 3's double tap: the second tap has nothing to press.
    it('cannot be pressed again while that order is being taken', async () => {
      await openScreenShowing([anOrder()]);

      const print = screen.getByRole('button', { name: 'Preparar K-4821' }) as HTMLButtonElement;
      fireEvent.click(print);

      expect(print.disabled).toBe(true);
    });

    it('says so when an order cannot be taken', async () => {
      const { rendered, http } = await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Preparar K-4821' }));
      http
        .expectOne(startPreparingUrl('K-4821'))
        .flush('', { status: 500, statusText: 'Server Error' });
      // The failure reloads the queue; the warning has to outlive that reload.
      TestBed.tick();
      http.expectOne(KDS_QUEUE_URL).flush([anOrder()]);
      await settle(rendered.fixture);

      expect(screen.getByRole('alert').textContent).toContain('No pudimos tomar el pedido K-4821');
    });

    // Preparar is its own action: pressing it takes the order, it does not
    // also choose or unchoose the card underneath.
    it('does not choose the card when its Preparar is pressed', async () => {
      await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Preparar K-4821' }));

      expect(screen.queryByRole('region', { name: 'Pedidos elegidos' })).toBeNull();
    });

    it('chooses a new order when its card is tapped', async () => {
      await openScreenShowing([anOrder()]);

      const pick = screen.getByRole('button', { name: 'Elegir K-4821' });
      fireEvent.click(pick);

      expect(pick.getAttribute('aria-pressed')).toBe('true');
    });

    // Criterion 2, and what the bartender is about to make together.
    it('sums every drink of the chosen orders in the bar below', async () => {
      await openScreenShowing([
        anOrder({ code: 'K-0001', orderItems: [gin(2), fernet(1)] }),
        anOrder({ code: 'K-0002', orderItems: [gin(2)] }),
      ]);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0001' }));
      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0002' }));

      const bar = screen.getByRole('region', { name: 'Pedidos elegidos' });
      expect(bar.textContent).toContain('2 pedidos elegidos');
      expect(bar.textContent).toContain('4× Gin Tonic');
      expect(bar.textContent).toContain('1× Fernet con Coca');
    });

    // Criterion 2: each one taken on its own, never one combined.
    it('takes every chosen order with a request of its own', async () => {
      const { http } = await openScreenShowing([
        anOrder({ code: 'K-0001' }),
        anOrder({ code: 'K-0002' }),
      ]);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0001' }));
      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0002' }));
      fireEvent.click(screen.getByRole('button', { name: 'Preparar 2 pedidos' }));

      http.expectOne(startPreparingUrl('K-0001'));
      http.expectOne(startPreparingUrl('K-0002'));
    });

    it('lets go of the chosen orders when Cancelar is pressed', async () => {
      await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-4821' }));
      fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));

      expect(screen.queryByRole('region', { name: 'Pedidos elegidos' })).toBeNull();
    });

    // A choice belongs to the moment it was made: an order that left Nuevos
    // and came back must not be taken by an old tap nobody remembers.
    it('forgets a choice once that order leaves Nuevos', async () => {
      const { rendered, channel, http } = await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-4821' }));

      channel.onChanged?.();
      rendered.fixture.detectChanges();
      http.expectOne(KDS_QUEUE_URL).flush([anOrder({ status: 'InPreparation' })]);
      await rendered.fixture.whenStable();

      channel.onChanged?.();
      rendered.fixture.detectChanges();
      http.expectOne(KDS_QUEUE_URL).flush([anOrder({ status: 'Queued' })]);
      await rendered.fixture.whenStable();

      expect(
        screen.getByRole('button', { name: 'Elegir K-4821' }).getAttribute('aria-pressed'),
      ).toBe('false');
      expect(screen.queryByRole('region', { name: 'Pedidos elegidos' })).toBeNull();
    });

    // A failed action usually means the card is stale: reloading shows the
    // bar what the order really is now.
    it('reloads the queue when an action fails', async () => {
      const { http } = await openScreenShowing([anOrder()]);

      fireEvent.click(screen.getByRole('button', { name: 'Preparar K-4821' }));
      http
        .expectOne(startPreparingUrl('K-4821'))
        .flush('', { status: 400, statusText: 'Bad Request' });
      TestBed.tick();

      http.expectOne(KDS_QUEUE_URL);
    });

    // Decided on 2026-09-28, against §11's "next ten": a card that can be
    // prepared on its own can be chosen too, however far down Nuevos it is.
    it('lets any new order be chosen, however far down the queue', async () => {
      const twenty = Array.from({ length: 20 }, (_, index) =>
        anOrder({
          code: `K-${String(1000 + index)}`,
          paidAt: new Date(Date.now() - (30 - index) * 60_000).toISOString(),
        }),
      );
      await openScreenShowing(twenty);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-1000' }));
      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-1019' }));

      expect(screen.getByRole('region', { name: 'Pedidos elegidos' }).textContent).toContain(
        '2 pedidos elegidos',
      );
    });

    // Criterion 4.
    it('hands an order in preparation back to the queue', async () => {
      const { http } = await openScreenShowing([anOrder({ status: 'InPreparation' })]);

      fireEvent.click(screen.getByRole('button', { name: 'Devolver K-4821 a la cola' }));

      expect(http.expectOne(returnToQueueUrl('K-4821')).request.method).toBe('POST');
    });
  });

  // US-18: Listo, several at once, back to preparation, the Listos column,
  // Entregado with its Deshacer, and the search.
  describe('marking ready and delivering', () => {
    const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

    function aReadyOrder(code: string, readyMinutesAgo: number): KdsQueueOrder {
      return anOrder({
        code,
        status: 'Ready',
        paidAt: minutesAgo(30),
        lastModifiedAt: minutesAgo(readyMinutesAgo),
      });
    }

    const noContent = { status: 204, statusText: 'No Content' };

    // Decided on 2026-09-28: the clock restarts when the order enters the
    // column, and the colors keep working there too.
    it('counts an order in preparation from when it was taken, with its own colors', async () => {
      await openScreenShowing([
        anOrder({
          code: 'K-0001',
          status: 'InPreparation',
          paidAt: minutesAgo(30),
          lastModifiedAt: minutesAgo(2),
        }),
        anOrder({
          code: 'K-0002',
          status: 'InPreparation',
          paidAt: minutesAgo(30),
          lastModifiedAt: minutesAgo(6),
        }),
      ]);

      const fresh = screen.getByText('K-0001').closest('.card');
      const slow = screen.getByText('K-0002').closest('.card');
      expect(fresh?.querySelector('.age')?.textContent?.trim()).toBe('2 min');
      expect(fresh?.classList.contains('urg')).toBe(false);
      expect(slow?.classList.contains('warn')).toBe(true);
    });

    it('lists orders in preparation from the one taken longest ago', async () => {
      await openScreenShowing([
        anOrder({
          code: 'K-0001',
          status: 'InPreparation',
          paidAt: minutesAgo(40),
          lastModifiedAt: minutesAgo(1),
        }),
        anOrder({
          code: 'K-0002',
          status: 'InPreparation',
          paidAt: minutesAgo(10),
          lastModifiedAt: minutesAgo(4),
        }),
      ]);

      const cards = within(screen.getByRole('group', { name: 'En preparación' })).getAllByRole(
        'article',
      );
      expect(cards.map((card) => card.querySelector('.onum')?.textContent)).toEqual([
        'K-0002',
        'K-0001',
      ]);
    });

    // Criterion 1.
    it('marks an order in preparation ready when its Listo is pressed', async () => {
      const { http } = await openScreenShowing([anOrder({ status: 'InPreparation' })]);

      fireEvent.click(screen.getByRole('button', { name: 'Listo K-4821' }));

      expect(http.expectOne(markReadyUrl('K-4821')).request.method).toBe('POST');
    });

    it('marks every chosen order in preparation ready with a request of its own', async () => {
      const { http } = await openScreenShowing([
        anOrder({ code: 'K-0001', status: 'InPreparation' }),
        anOrder({ code: 'K-0002', status: 'InPreparation' }),
      ]);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0001' }));
      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0002' }));
      fireEvent.click(screen.getByRole('button', { name: 'Marcar listos 2 pedidos' }));

      http.expectOne(markReadyUrl('K-0001'));
      http.expectOne(markReadyUrl('K-0002'));
    });

    // One column at a time: the bar below always offers a single action.
    it('lets go of the chosen new orders when one in preparation is chosen', async () => {
      await openScreenShowing([
        anOrder({ code: 'K-0001', status: 'Queued' }),
        anOrder({ code: 'K-0002', status: 'InPreparation' }),
      ]);

      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0001' }));
      fireEvent.click(screen.getByRole('button', { name: 'Elegir K-0002' }));

      const bar = screen.getByRole('region', { name: 'Pedidos elegidos' });
      expect(bar.textContent).toContain('1 pedido elegido');
      expect(screen.getByRole('button', { name: 'Marcar listos 1 pedido' })).not.toBeNull();
      expect(
        screen.getByRole('button', { name: 'Elegir K-0001' }).getAttribute('aria-pressed'),
      ).toBe('false');
    });

    it('sends a ready order back to preparation', async () => {
      const { http } = await openScreenShowing([aReadyOrder('K-4821', 2)]);

      fireEvent.click(screen.getByRole('button', { name: 'Volver K-4821 a preparación' }));

      expect(http.expectOne(returnToPreparationUrl('K-4821')).request.method).toBe('POST');
    });

    // Decided on 2026-09-28: minutes since it was made, and no color.
    it('says how long a ready order has waited since it was made, without color', async () => {
      await openScreenShowing([aReadyOrder('K-4821', 7)]);

      const card = screen.getByText('K-4821').closest('.card');
      expect(card?.querySelector('.age')?.textContent?.trim()).toBe('7 min');
      expect(card?.classList.contains('warn') || card?.classList.contains('urg')).toBe(false);
    });

    it('shows the ten most recently made ready orders and counts every one', async () => {
      await openScreenShowing(
        Array.from({ length: 12 }, (_, index) =>
          aReadyOrder(`K-${String(1000 + index)}`, 20 - index),
        ),
      );

      const listos = screen.getByRole('group', { name: 'Listos en la barra' });
      expect(within(listos).getAllByRole('article')).toHaveLength(10);
      expect(within(listos).queryByText('K-1000')).toBeNull();
      expect(listos.textContent).toContain('+2 más viejos');
      // Out of view is not out of reach: the note says where to find them.
      expect(listos.textContent).toMatch(/escane/i);
      expect(listos.querySelector('.cnt')?.textContent?.trim()).toBe('12');
    });

    it('delivers a ready order and offers to undo it', async () => {
      const { rendered, http } = await openScreenShowing([aReadyOrder('K-4821', 2)]);

      fireEvent.click(screen.getByRole('button', { name: 'Entregado K-4821' }));
      http.expectOne(deliverUrl('K-4821')).flush(null, noContent);
      TestBed.tick();
      http.expectOne(KDS_QUEUE_URL).flush([]);
      await rendered.fixture.whenStable();

      expect(screen.getByRole('status', { name: 'Entregado' }).textContent).toContain(
        'Entregado K-4821',
      );

      fireEvent.click(screen.getByRole('button', { name: 'Deshacer' }));

      expect(http.expectOne(undoDeliveryUrl('K-4821')).request.method).toBe('POST');
    });

    it('stops offering the undo after ten seconds', async () => {
      const { rendered, http } = await openScreenShowing([aReadyOrder('K-4821', 2)]);
      vi.useFakeTimers();

      fireEvent.click(screen.getByRole('button', { name: 'Entregado K-4821' }));
      http.expectOne(deliverUrl('K-4821')).flush(null, noContent);
      await vi.advanceTimersByTimeAsync(0);
      rendered.fixture.detectChanges();
      expect(screen.queryByRole('button', { name: 'Deshacer' })).not.toBeNull();

      await vi.advanceTimersByTimeAsync(10_000);
      rendered.fixture.detectChanges();

      expect(screen.queryByRole('button', { name: 'Deshacer' })).toBeNull();
      vi.useRealTimers();
    });

    // US-18, criterion 3, moved to the scan screen on 2026-09-29: the board
    // only points to it.
    it('offers the scan screen, where orders are scanned and searched', async () => {
      await openScreenShowing([]);

      expect(screen.getByRole('link', { name: 'Escanear o buscar' }).getAttribute('href')).toBe(
        '/bar-alfa/staff/kds/scan',
      );
    });

    it('has no search of its own', async () => {
      await openScreenShowing([anOrder()]);

      expect(screen.queryByRole('searchbox')).toBeNull();
    });
  });
});
