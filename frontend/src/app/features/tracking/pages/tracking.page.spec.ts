import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { TRACKING_INTERVAL_MS, TrackingStore } from '../tracking.store';
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
      // Never fires on its own: what these tests are about is what each answer
      // draws, so the answers are handed over by hand.
      { provide: TRACKING_INTERVAL_MS, useValue: 600_000 },
    ],
  });

  const http = TestBed.inject(HttpTestingController);
  http.expectOne(url).flush(anOrder(status));
  await rendered.fixture.whenStable();

  // The store belongs to the component, not to the TestBed: a test that needs a
  // second round asks for it here rather than waiting on the timer.
  const store = rendered.fixture.debugElement.injector.get(TrackingStore);

  return { rendered, http, store };
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

  /**
   * The drinks were handed over, which is the end of the journey and has to
   * look like one.
   *
   * The API stops showing an order the moment it is finished, so what arrives
   * is a 404 — the same one a bad link gets. Showing the red alert to somebody
   * who has been watching their order the whole time would tell them something
   * went wrong at the exact moment nothing did.
   */
  it('closes the journey when the order is handed over', async () => {
    const { rendered, http, store } = await openScreenShowing('Ready');

    store.askAgain();
    http
      .expectOne(url)
      .flush(
        { type: 'urn:drinkit:problem:order:not-found', detail: 'no existe' },
        { status: 404, statusText: 'Not Found' },
      );
    await rendered.fixture.whenStable();

    expect(screen.queryByRole('alert')).toBeNull();
    expect(screen.getByRole('status').textContent).toMatch(/entregado/i);
    expect(steps().every((step) => step.reached)).toBe(true);
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

  /**
   * The screen asks on its timer, and only on its timer. Reading what the
   * server said must not be what makes it ask again: that turns one round of
   * polling into a loop that feeds itself, with a timer left over each time.
   */
  it('does not ask again just because an answer arrived', async () => {
    const { http } = await openScreenShowing('Queued');

    http.expectNone(url);
  });

  /**
   * The signal dropped before the very first answer arrived.
   *
   * This is the screen somebody lands on the instant they pay, so it is exactly
   * where bad wifi finds them. The order is fine and the screen keeps asking on
   * its own, but saying nothing leaves them staring at "Buscando tu pedido…"
   * with no idea whether it is working — the one state the four-state rule
   * exists to prevent.
   */
  it('says the connection is down when the first answer never arrives', async () => {
    const rendered = await render(TrackingPage, {
      inputs: { venueSlug: 'bar-alfa', code: 'K-4821', token },
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TRACKING_INTERVAL_MS, useValue: 600_000 },
      ],
    });

    TestBed.inject(HttpTestingController).expectOne(url).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toMatch(/sin se[ñn]al|conexi[óo]n/i);
    expect(screen.queryByText(/buscando tu pedido/i)).toBeNull();
  });
});
