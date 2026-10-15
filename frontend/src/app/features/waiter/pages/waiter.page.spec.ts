import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen, within } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import type { HubLinkState } from '../../../core/realtime/hub-channel';
import { WaiterChannel } from '../waiter-channel';
import { WAITER_TABLES_URL, WaiterService } from '../waiter.service';
import type { TakenTable, WaiterTable } from '../waiter.service';
import { WaiterPage } from './waiter.page';

const now = new Date('2026-10-15T03:10:00Z');

function aTable(overrides: Partial<WaiterTable> = {}): WaiterTable {
  return {
    orderId: 'order-1',
    tableNumber: 12,
    customerName: 'Sofi',
    readyAt: '2026-10-15T03:01:00Z',
    takenByName: null,
    takenByMe: false,
    takenAt: null,
    items: [{ productName: 'Aperol Spritz', quantity: 1, note: 'con mucho hielo' }],
    ...overrides,
  };
}

const taken: TakenTable = {
  orderId: 'order-1',
  tableNumber: 12,
  waiterName: 'Martín',
  takenAt: '2026-10-15T03:10:00Z',
};

/** Stands in for the waiter's hub: the test says when the list changed. */
class FakeWaiterChannel {
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

let channel: FakeWaiterChannel;

/** Renders what the last answer changed, without waiting on any request still pending. */
async function settle(fixture: { detectChanges: () => void }): Promise<void> {
  for (let round = 0; round < 3; round++) {
    fixture.detectChanges();
    TestBed.tick();
    await Promise.resolve();
  }
}

async function openThePhone(
  tables: WaiterTable[] | 'fails' | 'notTonight' = [],
  service: Partial<Record<'take', unknown>> = {},
) {
  vi.setSystemTime(now);

  const rendered = await render(WaiterPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: WaiterChannel, useFactory: () => (channel = new FakeWaiterChannel()) },
      { provide: WaiterService, useValue: service },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  // The list is asked for in the first change detection, which render did not wait for.
  await settle(rendered.fixture);

  if (tables === 'fails')
    http.expectOne(WAITER_TABLES_URL).flush('', { status: 500, statusText: 'Server Error' });
  else if (tables === 'notTonight')
    http
      .expectOne(WAITER_TABLES_URL)
      .flush({ type: ProblemTypes.notInTonightsCrew }, { status: 403, statusText: 'Forbidden' });
  else http.expectOne(WAITER_TABLES_URL).flush(tables);

  await settle(rendered.fixture);

  return { rendered, http };
}

function section(name: RegExp): HTMLElement {
  return screen.getByRole('region', { name });
}

describe('WaiterPage', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['Date'] }));
  afterEach(() => vi.useRealTimers());

  // Criterion 1: the ones nobody has, with the table, the drinks, the notes
  // and how long they have been waiting.
  it('lists the tables nobody has, with their drinks, their notes and how long they wait', async () => {
    await openThePhone([aTable()]);

    const free = within(section(/libres/i));

    expect(free.getByText('Mesa 12')).not.toBeNull();
    expect(free.getByText(/1× Aperol Spritz/)).not.toBeNull();
    expect(free.getByText(/con mucho hielo/)).not.toBeNull();
    expect(free.getByText(/Sofi/)).not.toBeNull();
    expect(free.getByText(/esperando 9 min/i)).not.toBeNull();
  });

  // Criterion 2: "Las que tomaste" and "Las lleva otro" are different sections.
  it('puts the ones the waiter took and the ones another carries in their own sections', async () => {
    await openThePhone([
      aTable({
        orderId: 'a',
        tableNumber: 5,
        takenByMe: true,
        takenByName: 'Martín',
        takenAt: '2026-10-15T03:09:20Z',
      }),
      aTable({
        orderId: 'b',
        tableNumber: 3,
        takenByMe: false,
        takenByName: 'Sofía',
        takenAt: '2026-10-15T03:09:20Z',
      }),
    ]);

    expect(within(section(/las que tomaste/i)).getByText('Mesa 5')).not.toBeNull();
    expect(within(section(/las que tomaste/i)).getByText(/la tomaste hace 40 seg/i)).not.toBeNull();
    expect(within(section(/las lleva otro/i)).getByText('Mesa 3')).not.toBeNull();
    expect(within(section(/las lleva otro/i)).getByText(/Sofía/)).not.toBeNull();
    expect(within(section(/libres/i)).queryByText(/Mesa/)).toBeNull();
  });

  it('offers "La llevo yo" only on the ones nobody has', async () => {
    await openThePhone([
      aTable({ orderId: 'a', tableNumber: 5 }),
      aTable({ orderId: 'b', tableNumber: 3, takenByName: 'Sofía' }),
    ]);

    expect(screen.getAllByRole('button', { name: /la llevo yo/i })).toHaveLength(1);
  });

  it('takes the table and shows the list again with it among the waiter’s own', async () => {
    const take = vi.fn().mockReturnValue(of(taken));
    const { rendered, http } = await openThePhone([aTable()], { take });

    screen.getByRole('button', { name: /la llevo yo/i }).click();
    await settle(rendered.fixture);
    http
      .expectOne(WAITER_TABLES_URL)
      .flush([aTable({ takenByMe: true, takenByName: 'Martín', takenAt: '2026-10-15T03:10:00Z' })]);
    await settle(rendered.fixture);

    expect(take).toHaveBeenCalledWith('order-1');
    expect(within(section(/las que tomaste/i)).getByText('Mesa 12')).not.toBeNull();
  });

  // Criterion 3: the one who lost finds out whose it is, without a refresh.
  it('says another waiter was first and shows whose it is', async () => {
    const take = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { type: ProblemTypes.waiterAlreadyTaken },
          }),
      ),
    );
    const { rendered, http } = await openThePhone([aTable()], { take });

    screen.getByRole('button', { name: /la llevo yo/i }).click();
    await settle(rendered.fixture);
    http
      .expectOne(WAITER_TABLES_URL)
      .flush([aTable({ takenByName: 'Sofía', takenAt: '2026-10-15T03:10:00Z' })]);
    await settle(rendered.fixture);

    expect(screen.getByRole('status').textContent).toContain('otro mozo');
    expect(within(section(/las lleva otro/i)).getByText(/Sofía/)).not.toBeNull();
  });

  it('cannot be tapped twice while the first tap is in flight', async () => {
    const take = vi.fn().mockReturnValue(new Subject<TakenTable>());
    const { rendered } = await openThePhone([aTable()], { take });

    const button = screen.getByRole('button', { name: /la llevo yo/i }) as HTMLButtonElement;
    button.click();
    await settle(rendered.fixture);
    button.click();

    expect(take).toHaveBeenCalledTimes(1);
    expect(button.disabled).toBe(true);
  });

  it('says so when the tap could not be sent', async () => {
    const take = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { rendered } = await openThePhone([aTable()], { take });

    screen.getByRole('button', { name: /la llevo yo/i }).click();
    await settle(rendered.fixture);

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  // The hub is what keeps two phones looking at the same thing.
  it('loads the list again when the hub says it changed', async () => {
    const { rendered, http } = await openThePhone([aTable({ tableNumber: 12 })]);

    channel.changed?.();
    await settle(rendered.fixture);
    http
      .expectOne(WAITER_TABLES_URL)
      .flush([aTable({ tableNumber: 12 }), aTable({ orderId: 'z', tableNumber: 8 })]);
    await settle(rendered.fixture);

    expect(within(section(/libres/i)).getByText('Mesa 8')).not.toBeNull();
  });

  it('says there is nothing to carry yet', async () => {
    await openThePhone([]);

    expect(screen.getByText(/ninguna mesa lista/i)).not.toBeNull();
  });

  // US-35, criterion 4: signed in as a waiter, but not in tonight's crew.
  it('says the account is not in tonight’s crew', async () => {
    await openThePhone('notTonight');

    expect(screen.getByRole('alert').textContent).toContain('no estás en la noche de hoy');
  });

  it('says so when the list cannot be loaded', async () => {
    await openThePhone('fails');

    expect(screen.getByRole('alert').textContent).toContain('No pudimos traer las mesas');
  });

  it('says the list may be out of date while the hub is down', async () => {
    const { rendered } = await openThePhone([aTable()]);

    channel.state.set('reconnecting');
    await settle(rendered.fixture);

    expect(screen.getByRole('status').textContent).toContain('Sin conexión');
  });

  it('lets go of the hub when the screen goes away', async () => {
    const { rendered } = await openThePhone([aTable()]);

    rendered.fixture.destroy();

    expect(channel.disconnected).toBe(true);
  });
});
