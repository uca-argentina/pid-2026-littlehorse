import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen, within } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import type { HubLinkState } from '../../../core/realtime/hub-channel';
import { CASHIER_VIP_DELIVERIES_URL } from '../cashier.service';
import type { VipDelivery } from '../cashier.service';
import { TillChannel } from '../till-channel';
import { CashierVipPage } from './cashier-vip.page';

const now = new Date('2026-10-15T03:10:00Z');

function aTable(overrides: Partial<VipDelivery> = {}): VipDelivery {
  return {
    orderId: 'order-1',
    tableNumber: 12,
    customerName: 'Sofi',
    readyAt: '2026-10-15T03:01:00Z',
    takenByName: null,
    takenAt: null,
    items: [{ productName: 'Aperol Spritz', quantity: 1, note: null }],
    ...overrides,
  };
}

/** Stands in for the till's hub: the test says when something changed. */
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

/** Renders what the last answer changed, without waiting on any request still pending. */
async function settle(fixture: { detectChanges: () => void }): Promise<void> {
  for (let round = 0; round < 3; round++) {
    fixture.detectChanges();
    TestBed.tick();
    await Promise.resolve();
  }
}

async function openTheScreen(tables: VipDelivery[] | 'fails' | 'notTonight' = []) {
  vi.setSystemTime(now);

  const rendered = await render(CashierVipPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: TillChannel, useFactory: () => (channel = new FakeTillChannel()) },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  await settle(rendered.fixture);

  if (tables === 'fails')
    http
      .expectOne(CASHIER_VIP_DELIVERIES_URL)
      .flush('', { status: 500, statusText: 'Server Error' });
  else if (tables === 'notTonight')
    http
      .expectOne(CASHIER_VIP_DELIVERIES_URL)
      .flush({ type: ProblemTypes.notInTonightsCrew }, { status: 403, statusText: 'Forbidden' });
  else http.expectOne(CASHIER_VIP_DELIVERIES_URL).flush(tables);

  await settle(rendered.fixture);

  return { rendered, http };
}

function section(name: RegExp): HTMLElement {
  return screen.getByRole('region', { name });
}

describe('CashierVipPage', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['Date'] }));
  afterEach(() => vi.useRealTimers());

  // Criterion 1: the ones nobody took, with how long they have waited.
  it('lists the tables nobody has taken, with their drinks and how long they wait', async () => {
    await openTheScreen([aTable()]);

    const waiting = within(section(/todavía no tomó ningún mozo/i));

    expect(waiting.getByText('Mesa 12')).not.toBeNull();
    expect(waiting.getByText(/1× Aperol Spritz/)).not.toBeNull();
    expect(waiting.getByText(/Sofi/)).not.toBeNull();
    expect(waiting.getByText(/9 min esperando/i)).not.toBeNull();
    expect(waiting.getByText(/nadie la tomó/i)).not.toBeNull();
  });

  it('keeps the order the API sends them in: the one that waited longest is first', async () => {
    await openTheScreen([
      aTable({ orderId: 'a', tableNumber: 12 }),
      aTable({ orderId: 'b', tableNumber: 5, readyAt: '2026-10-15T03:05:00Z' }),
    ]);

    const names = within(section(/todavía no tomó ningún mozo/i))
      .getAllByText(/^Mesa \d+$/)
      .map((element) => element.textContent);

    expect(names).toEqual(['Mesa 12', 'Mesa 5']);
  });

  // Criterion 1, the other half: who has it and since when.
  it('lists the ones a waiter took apart, with who and since when', async () => {
    await openTheScreen([
      aTable({
        orderId: 'a',
        tableNumber: 3,
        takenByName: 'Martín',
        takenAt: '2026-10-15T03:09:20Z',
      }),
    ]);

    const taken = within(section(/ya las tomó un mozo/i));

    expect(taken.getByText('Mesa 3')).not.toBeNull();
    expect(taken.getByText(/Martín · la tomó hace 40 seg/i)).not.toBeNull();
    expect(within(section(/todavía no tomó ningún mozo/i)).queryByText('Mesa 3')).toBeNull();
  });

  it('says there are no tables waiting', async () => {
    await openTheScreen([]);

    expect(screen.getByText(/no hay más mesas esperando/i)).not.toBeNull();
  });

  // The till only looks: nothing on this screen changes an order.
  it('has no way to take or hand over a table', async () => {
    await openTheScreen([
      aTable(),
      aTable({ orderId: 'b', tableNumber: 3, takenByName: 'Martín' }),
    ]);

    expect(screen.queryByRole('button', { name: /llevo|entregado/i })).toBeNull();
  });

  // Criterion 2: a waiter takes or delivers, and the list moves on its own.
  it('loads the list again when the till hub says something changed', async () => {
    const { rendered, http } = await openTheScreen([aTable()]);

    channel.changed?.();
    await settle(rendered.fixture);
    http.expectOne(CASHIER_VIP_DELIVERIES_URL).flush([
      aTable(),
      aTable({
        orderId: 'z',
        tableNumber: 8,
        takenByName: 'Sofía',
        takenAt: '2026-10-15T03:10:00Z',
      }),
    ]);
    await settle(rendered.fixture);

    expect(within(section(/ya las tomó un mozo/i)).getByText('Mesa 8')).not.toBeNull();
  });

  it('says the account is not in tonight’s crew', async () => {
    await openTheScreen('notTonight');

    expect(screen.getByRole('alert').textContent).toContain('no estás en la noche de hoy');
  });

  it('says so when the list cannot be loaded', async () => {
    await openTheScreen('fails');

    expect(screen.getByRole('alert').textContent).toContain('No pudimos traer las entregas');
  });

  it('says the list may be out of date while the hub is down', async () => {
    const { rendered } = await openTheScreen([aTable()]);

    channel.state.set('reconnecting');
    await settle(rendered.fixture);

    expect(screen.getByRole('status').textContent).toContain('Sin conexión');
  });

  it('lets go of the hub when the screen goes away', async () => {
    const { rendered } = await openTheScreen([aTable()]);

    rendered.fixture.destroy();

    expect(channel.disconnected).toBe(true);
  });

  // The two tabs of the till: cash and the VIP deliveries.
  it('is reachable from the till’s tabs', async () => {
    await openTheScreen([]);

    expect(screen.getByRole('link', { name: /entregas vip/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/cashier/vip',
    );
    expect(screen.getByRole('link', { name: /cobro en efectivo/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/cashier',
    );
  });
});
