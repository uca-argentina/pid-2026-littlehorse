import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { HubLinkState } from '../../core/realtime/hub-channel';
import { TrackingChannel } from './tracking-channel';
import { TRACKING_RETRY_MS, TrackingStore } from './tracking.store';
import { trackingUrl } from './tracking.service';
import type { CustomerOrderStatus, TrackedOrder } from './tracking.service';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const url = trackingUrl('bar-alfa', 'K-4821', token);

const retryMs = 5_000;

function anOrder(status: CustomerOrderStatus): TrackedOrder {
  return {
    code: 'K-4821',
    customerName: 'María Quadro',
    status,
    total: 9000,
    paidAt: '2026-09-17T02:30:00Z',
    items: [{ productName: 'Gin Tonic', quantity: 2, note: 'sin hielo' }],
  };
}

const notFound = [
  { type: 'urn:drinkit:problem:order:not-found', detail: 'no existe' },
  { status: 404, statusText: 'Not Found' },
] as const;

/** Stands in for the live link: the test says when the order moves and when the link drops. */
class FakeTrackingChannel {
  readonly link = signal<HubLinkState>('connected');

  readonly state = this.link.asReadonly();

  following: string | null = null;

  private changed: (() => void) | null = null;

  follow(token: string, onChanged: () => void): void {
    this.following = token;
    this.changed = onChanged;
  }

  disconnect(): void {
    this.following = null;
    this.changed = null;
  }

  orderMoves(): void {
    this.changed?.();
  }
}

function aStore(): {
  tracking: TrackingStore;
  http: HttpTestingController;
  channel: FakeTrackingChannel;
} {
  const channel = new FakeTrackingChannel();

  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: TrackingChannel, useValue: channel },
      { provide: TRACKING_RETRY_MS, useValue: retryMs },
      TrackingStore,
    ],
  });

  return {
    tracking: TestBed.inject(TrackingStore),
    http: TestBed.inject(HttpTestingController),
    channel,
  };
}

describe('TrackingStore', () => {
  it('asks where the order is', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);

    expect(http.expectOne(url).request.method).toBe('GET');
  });

  it('keeps what it was told', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    expect(tracking.order()?.code).toBe('K-4821');
    expect(tracking.order()?.status).toBe('Queued');
    expect(tracking.status()).toBe('following');
  });

  // US-22: told, not asking. The token is what the live link follows it by.
  it('follows the order live by its token', () => {
    const { tracking, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);

    expect(channel.following).toBe(token);
  });

  // US-22, criterion 1: the bar moved it, and the screen catches up on its own.
  it('asks again when it hears the order moved', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    channel.orderMoves();
    http.expectOne(url).flush(anOrder('InPreparation'));

    expect(tracking.order()?.status).toBe('InPreparation');
  });

  // A hundred phones in a packed venue asking every few seconds is what the
  // live link replaces.
  it('does not ask on a timer', () => {
    vi.useFakeTimers();

    try {
      const { tracking, http } = aStore();

      tracking.follow('bar-alfa', 'K-4821', token);
      http.expectOne(url).flush(anOrder('Queued'));

      vi.advanceTimersByTime(60_000);

      http.expectNone(url);
    } finally {
      vi.useRealTimers();
    }
  });

  // The answer on its way may have been read before the move it was told
  // about. Nothing will say so again, so dropping the second ask would leave
  // the screen stuck on the old status for good.
  it('does not lose a move it heard while it was still asking', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    channel.orderMoves();
    http.expectOne(url).flush(anOrder('Queued'));

    http.expectOne(url).flush(anOrder('InPreparation'));

    expect(tracking.order()?.status).toBe('InPreparation');
  });

  // Nor does it pile requests up: however many moves arrive meanwhile, one
  // more round is enough to catch up with all of them.
  it('asks once more, not once per move, after an answer in flight', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    channel.orderMoves();
    channel.orderMoves();
    http.expectOne(url).flush(anOrder('Queued'));

    http.expectOne(url).flush(anOrder('InPreparation'));
    http.expectNone(url);
  });

  // US-22, criterion 2: while the live link is down, what is on screen may
  // already be old, and the customer is told so rather than trusting it.
  it('says it is out of touch while the live link is down', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    channel.link.set('reconnecting');

    expect(tracking.status()).toBe('unreachable');
    // And what it last knew is still on screen rather than blanked out.
    expect(tracking.order()?.status).toBe('Queued');

    channel.link.set('connected');

    expect(tracking.status()).toBe('following');
  });

  // The move was heard but the answer did not arrive: nothing will announce
  // that move again, so it keeps asking until it gets through.
  it('keeps asking when a request fails, and catches up', () => {
    vi.useFakeTimers();

    try {
      const { tracking, http, channel } = aStore();

      tracking.follow('bar-alfa', 'K-4821', token);
      http.expectOne(url).flush(anOrder('Queued'));

      channel.orderMoves();
      http.expectOne(url).error(new ProgressEvent('error'));

      expect(tracking.status()).toBe('unreachable');
      expect(tracking.order()?.status).toBe('Queued');

      vi.advanceTimersByTime(retryMs);
      http.expectOne(url).flush(anOrder('Ready'));

      expect(tracking.status()).toBe('following');
      expect(tracking.order()?.status).toBe('Ready');
    } finally {
      vi.useRealTimers();
    }
  });

  // The link leads nowhere: a wrong token, somebody else's code, a code nobody
  // has. One answer for all of them, and no retrying. Told apart from an order
  // that finished by this being the first thing the server ever said.
  it('stops following when the link leads nowhere', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(...notFound);

    expect(tracking.status()).toBe('nowhere');
    expect(channel.following).toBeNull();

    tracking.askAgain();
    http.expectNone(url);
  });

  /**
   * The order was handed over.
   *
   * There is no answer that says so: the API stops showing an order the moment
   * it is handed over, so what arrives is the same 404 as a bad link. It was on
   * screen, so the journey is over rather than the link being wrong.
   */
  it('calls it over, and stops following, when the link stops working after showing the order', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Ready'));

    channel.orderMoves();
    http.expectOne(url).flush(...notFound);

    expect(tracking.status()).toBe('over');
    expect(tracking.order()?.code).toBe('K-4821');
    expect(channel.following).toBeNull();

    tracking.askAgain();
    http.expectNone(url);
  });

  // US-22, criterion 3: a canceled order still answers, so the screen can say
  // so, but it is not going anywhere any more.
  it('stops following a canceled order, and keeps showing it', () => {
    const { tracking, http, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Canceled'));

    expect(tracking.order()?.status).toBe('Canceled');
    expect(channel.following).toBeNull();

    tracking.askAgain();
    http.expectNone(url);
  });

  // A phone in a pocket may have lost the live link without noticing yet. The
  // moment somebody looks again is the moment it has to be right.
  it('asks again when the screen is looked at again', () => {
    const { tracking, http } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    http.expectOne(url).flush(anOrder('Queued'));

    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));

    expect(http.expectOne(url).request.method).toBe('GET');
  });

  it('lets go of the live link when the screen closes', () => {
    const { tracking, channel } = aStore();

    tracking.follow('bar-alfa', 'K-4821', token);
    TestBed.resetTestingModule();

    expect(channel.following).toBeNull();
  });
});
