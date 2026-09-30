import { TestBed } from '@angular/core/testing';
import { SessionStorage } from '../../core/auth/session-storage';
import { HUB_CONNECTION } from '../../core/realtime/hub-channel';
import type { HubConnectionLike } from '../../core/realtime/hub-channel';
import { TrackingChannel } from './tracking-channel';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

/** Stands in for the SignalR connection, and remembers what was asked of it. */
class FakeHubConnection implements HubConnectionLike {
  readonly listening: string[] = [];

  readonly invoked: unknown[][] = [];

  private reconnected: (() => void) | null = null;

  on(method: string): void {
    this.listening.push(method);
  }

  onreconnecting(): void {
    return undefined;
  }

  onreconnected(callback: () => void): void {
    this.reconnected = callback;
  }

  onclose(): void {
    return undefined;
  }

  start(): Promise<void> {
    return Promise.resolve();
  }

  stop(): Promise<void> {
    return Promise.resolve();
  }

  invoke(method: string, ...args: unknown[]): Promise<unknown> {
    this.invoked.push([method, ...args]);

    return Promise.resolve();
  }

  getsItBack(): void {
    this.reconnected?.();
  }
}

/**
 * US-22: the customer's half of the live link. What the rest of HubChannel
 * does — retrying forever, saying when it is down — is covered by
 * KdsBoardChannel's spec.
 */
describe('TrackingChannel', () => {
  let connection: FakeHubConnection;
  let url: string;
  let accessToken: () => string;
  let channel: TrackingChannel;
  let asks: number;

  const askAgain = (): void => {
    asks += 1;
  };

  beforeEach(() => {
    vi.useFakeTimers();
    connection = new FakeHubConnection();
    asks = 0;

    TestBed.configureTestingModule({
      providers: [
        {
          provide: HUB_CONNECTION,
          useValue: (hubUrl: string, tokenFactory: () => string) => {
            url = hubUrl;
            accessToken = tokenFactory;
            return connection;
          },
        },
      ],
    });
    channel = TestBed.inject(TrackingChannel);
  });

  afterEach(() => {
    channel.disconnect();
    vi.useRealTimers();
  });

  it('listens to the tracking hub for its one message', () => {
    channel.follow(token, askAgain);

    expect(url).toBe('/api/hubs/tracking');
    expect(connection.listening).toEqual(['OrderChanged']);
  });

  // The link is the proof, not a staff session somebody left open on the
  // same phone: this is the customer's screen.
  it('sends no staff token, even with a staff session open', () => {
    TestBed.inject(SessionStorage).remember({
      token: 'un-token',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      username: 'barra.demo',
      role: 'Kds',
    });

    channel.follow(token, askAgain);

    expect(accessToken()).toBe('');
  });

  it('follows the order by its token once connected', async () => {
    channel.follow(token, askAgain);
    await vi.advanceTimersByTimeAsync(0);

    expect(connection.invoked).toEqual([['Follow', token]]);
  });

  // Whatever moved between the first answer and joining would otherwise
  // never be heard: nothing is sent for it again.
  it('asks again once it follows the order', async () => {
    channel.follow(token, askAgain);
    await vi.advanceTimersByTimeAsync(0);

    expect(asks).toBe(1);
  });

  // A connection that comes back is a new one on the server, and belongs to
  // no order until it follows it again.
  it('follows the order again when the link comes back', async () => {
    channel.follow(token, askAgain);
    await vi.advanceTimersByTimeAsync(0);

    connection.getsItBack();
    await vi.advanceTimersByTimeAsync(0);

    expect(connection.invoked).toEqual([
      ['Follow', token],
      ['Follow', token],
    ]);
  });
});
