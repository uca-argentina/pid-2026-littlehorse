import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TRACKING_INTERVAL_MS, TrackingStore } from './tracking.store';
import { trackingUrl } from './tracking.service';
import type { TrackedOrder } from './tracking.service';

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

/** Whether the page is being looked at. The store asks this, the browser answers it. */
let looking: boolean;

function aStore(): { tracking: TrackingStore; http: HttpTestingController } {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      // Asked for by hand in these tests rather than on a timer: what is under
      // test is what each answer does, not that setInterval counts.
      { provide: TRACKING_INTERVAL_MS, useValue: 0 },
      TrackingStore,
    ],
  });

  return { tracking: TestBed.inject(TrackingStore), http: TestBed.inject(HttpTestingController) };
}

describe('TrackingStore', () => {
  beforeEach(() => {
    looking = true;
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() =>
      looking ? 'visible' : 'hidden',
    );
  });

  it('asks where the order is', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);

    expect(http.expectOne(url).request.method).toBe('GET');
  });

  // Criterion 1 and 2: the code, and which of the four steps it is on.
  it('keeps what it was told', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    expect(tracking.order()?.code).toBe('K-4821');
    expect(tracking.order()?.status).toBe('Queued');
    expect(tracking.status()).toBe('following');
  });

  // Criterion 3: the state changed, and the screen catches up on its own.
  it('notices when the order moves on', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    tracking.askAgain();
    http.expectOne(url).flush(anOrder('InPreparation'));

    expect(tracking.order()?.status).toBe('InPreparation');
  });

  // The link leads nowhere: a wrong token, somebody else's code, an order
  // already handed over. One answer for all of them, and no retrying.
  it('stops asking when the link leads nowhere', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http
      .expectOne(url)
      .flush(
        { type: 'urn:drinkit:problem:order:not-found', detail: 'no existe' },
        { status: 404, statusText: 'Not Found' },
      );

    expect(tracking.status()).toBe('nowhere');

    tracking.askAgain();
    http.expectNone(url);
  });

  /**
   * Criterion 6: the signal dropped. It is not the same as the link leading
   * nowhere — the order is fine and so is the link — so the screen says so and
   * keeps asking, and comes back on its own when the connection does.
   */
  it('keeps asking when the connection drops, and catches up', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    tracking.askAgain();
    http.expectOne(url).error(new ProgressEvent('error'));

    expect(tracking.status()).toBe('unreachable');
    // And what it last knew is still on screen rather than blanked out.
    expect(tracking.order()?.status).toBe('Queued');

    tracking.askAgain();
    http.expectOne(url).flush(anOrder('Ready'));

    expect(tracking.status()).toBe('following');
    expect(tracking.order()?.status).toBe('Ready');
  });

  // Nobody is waiting on it any more, so there is nothing left to ask about.
  it('stops asking once the order is over', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Delivered'));

    expect(tracking.isOver()).toBe(true);

    tracking.askAgain();
    http.expectNone(url);
  });

  // The phone is in a pocket with the screen off. Asking twenty times a minute
  // for something nobody is reading is battery somebody needs for the night.
  it('does not ask while nobody is looking', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    looking = false;
    tracking.askAgain();
    http.expectNone(url);

    looking = true;
    tracking.askAgain();

    expect(http.expectOne(url).request.method).toBe('GET');
  });

  // Two answers racing on a bad connection would otherwise pile requests up.
  it('does not ask again while one is still in flight', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    tracking.askAgain();

    http.expectOne(url).flush(anOrder('Queued'));
  });
});
