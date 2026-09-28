import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { render, screen } from '@testing-library/angular';
import { KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsQueueOrder } from '../kds-queue';
import { KdsBoardPage } from './kds-board.page';

/** Captures the callback the page hands the channel, instead of opening a real connection. */
class FakeKdsBoardChannel {
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

  it('shows a finished order under Listos', async () => {
    await openScreenShowing([anOrder({ status: 'Ready' })]);

    expect(screen.getByText('Listos').closest('section')?.textContent).toContain('K-4821');
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

  it('disconnects the channel when the screen closes', async () => {
    const { rendered, channel } = await openScreenShowing([]);

    rendered.fixture.destroy();

    expect(channel.onChanged).toBeNull();
  });
});
