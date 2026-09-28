import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { render, screen } from '@testing-library/angular';
import { SessionStorage } from '../../../core/auth/session-storage';
import { KDS_RETRY_MS, KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import type { KdsLinkState } from '../../../core/kds/kds-board-channel';
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
    isForTable: false,
    lines: [{ productName: 'Gin Tonic', quantity: 2, note: null }],
    ...overrides,
  };
}

async function openScreen() {
  const channel = new FakeKdsBoardChannel();

  const rendered = await render(KdsBoardPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
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
        lines: [{ productName: 'Aperol Spritz', quantity: 1, note: 'con mucho hielo' }],
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
    [10, 'urg' as const, '10 min · urgente'],
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

  it('disconnects the channel when the screen closes', async () => {
    const { rendered, channel } = await openScreenShowing([]);

    rendered.fixture.destroy();

    expect(channel.onChanged).toBeNull();
  });
});
