import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { TRACKING_INTERVAL_MS } from '../tracking.store';
import { trackingUrl } from '../tracking.service';
import type { TrackedOrder } from '../tracking.service';
import { TrackingPage } from './tracking.page';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const url = trackingUrl('bar-alfa', 'K-4821', token);

function anOrder(status: string): TrackedOrder {
  return {
    code: 'K-4821',
    customerName: 'María Quadro',
    status,
    total: 9000,
    paidAt: '2026-09-17T02:30:00Z',
    items: [{ productName: 'Gin Tonic', quantity: 2, note: 'sin hielo' }],
  };
}

async function openScreenShowing(status: string) {
  const rendered = await render(TrackingPage, {
    inputs: { venueSlug: 'bar-alfa', code: 'K-4821', token },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      // Never fires: what these tests are about is what each answer draws.
      { provide: TRACKING_INTERVAL_MS, useValue: 600_000 },
    ],
  });

  const http = TestBed.inject(HttpTestingController);
  http.expectOne(url).flush(anOrder(status));
  await rendered.fixture.whenStable();

  return { rendered, http };
}

/** The four steps, in order, each with whether it is reached. */
function steps(): { name: string; reached: boolean }[] {
  return screen.getAllByRole('listitem').map((step) => ({
    name: step.textContent?.trim() ?? '',
    reached: step.getAttribute('data-reached') === 'true',
  }));
}

describe('TrackingPage', () => {
  // Criterion 1: the number, which is what the bar will call out.
  it('shows the code of the order', async () => {
    await openScreenShowing('Queued');

    expect(screen.getByTestId('order-code').textContent).toContain('K-4821');
  });

  // Criterion 2: the whole journey, and which step this one is on.
  it('shows the four steps, with the one it is on', async () => {
    await openScreenShowing('Queued');

    expect(steps().map((step) => step.name)).toEqual([
      'Esperando en la barra',
      'En preparación',
      'Listo',
      'Entregado',
    ]);
    expect(steps().map((step) => step.reached)).toEqual([true, false, false, false]);
  });

  it('marks everything up to where the order got to', async () => {
    await openScreenShowing('Ready');

    expect(steps().map((step) => step.reached)).toEqual([true, true, true, false]);
  });

  // The one moment the screen exists for: it is their turn at the bar.
  it('says it plainly when the order is ready', async () => {
    await openScreenShowing('Ready');

    expect(screen.getByRole('status').textContent).toMatch(/listo/i);
  });

  it('says it is waiting while nobody has taken it', async () => {
    await openScreenShowing('Queued');

    expect(screen.getByRole('status').textContent).toMatch(/pago/i);
  });

  // A cancelled order is not a journey that stalled: the steps would be a lie,
  // so they are not drawn at all.
  it('says so instead of drawing the steps when the order was cancelled', async () => {
    await openScreenShowing('Canceled');

    expect(screen.queryAllByRole('listitem')).toEqual([]);
    expect(screen.getByRole('status').textContent).toMatch(/cancelado/i);
  });

  it('offers the way back to the menu of this venue', async () => {
    await openScreenShowing('Queued');

    expect(
      screen
        .getByText(/pedir algo más/i)
        .closest('a')
        ?.getAttribute('href'),
    ).toBe('/bar-alfa/menu');
  });

  // A wrong token, somebody else's code, an order already handed over: one
  // answer, because from where the customer stands they are the same.
  it('says the link leads nowhere when the API says so', async () => {
    const rendered = await render(TrackingPage, {
      inputs: { venueSlug: 'bar-alfa', code: 'K-4821', token },
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TRACKING_INTERVAL_MS, useValue: 600_000 },
      ],
    });

    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush(
        { type: 'urn:drinkit:problem:order:not-found', detail: 'no existe' },
        { status: 404, statusText: 'Not Found' },
      );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/no lleva a ning[úu]n pedido/i);
  });

  /*
   * Criterion 6 — what a dropped connection does — is proved in
   * tracking.store.spec.ts, which owns it: the screen only draws what the store
   * says, and the store is what decides to keep the last answer and go on
   * asking. Reaching in here to fake a dropped request would test the mock.
   */
});
