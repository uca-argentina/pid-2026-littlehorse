import { TestBed } from '@angular/core/testing';
import type { KdsHubConnection } from './kds-board-channel';
import { KDS_HUB_CONNECTION, KDS_RETRY_MS, KdsBoardChannel, kdsHubUrl } from './kds-board-channel';

describe('kdsHubUrl', () => {
  // Development: the Angular proxy forwards /api and strips it on the way.
  it('goes through the dev proxy when there is no API address', () => {
    expect(kdsHubUrl('')).toBe('/api/hubs/kds');
  });

  // Deployed, the PWA and the API are on different hosts, and the SignalR
  // client does not go through apiBaseUrlInterceptor like HttpClient does.
  it('goes straight to the API host when the deploy gives one', () => {
    expect(kdsHubUrl('https://api.drinkit.example')).toBe('https://api.drinkit.example/hubs/kds');
  });
});

/** Stands in for the SignalR connection: each start() answers with the next outcome queued. */
class FakeHubConnection implements KdsHubConnection {
  readonly outcomes: ('ok' | 'fail')[] = [];

  starts = 0;

  stopped = false;

  private readonly handlers = new Map<string, () => void>();

  private reconnecting: (() => void) | null = null;

  private reconnected: (() => void) | null = null;

  private closed: (() => void) | null = null;

  on(method: string, handler: () => void): void {
    this.handlers.set(method, handler);
  }

  onreconnecting(callback: () => void): void {
    this.reconnecting = callback;
  }

  onreconnected(callback: () => void): void {
    this.reconnected = callback;
  }

  onclose(callback: () => void): void {
    this.closed = callback;
  }

  start(): Promise<void> {
    this.starts += 1;

    return this.outcomes.shift() === 'fail'
      ? Promise.reject(new Error('The API is not answering.'))
      : Promise.resolve();
  }

  stop(): Promise<void> {
    this.stopped = true;
    this.closed?.();

    return Promise.resolve();
  }

  losesTheLink(): void {
    this.reconnecting?.();
  }

  getsItBack(): void {
    this.reconnected?.();
  }

  givesUp(): void {
    this.closed?.();
  }
}

describe('KdsBoardChannel', () => {
  let connection: FakeHubConnection;
  let channel: KdsBoardChannel;
  let reloads: number;

  const reload = (): void => {
    reloads += 1;
  };

  beforeEach(() => {
    vi.useFakeTimers();
    connection = new FakeHubConnection();
    reloads = 0;

    TestBed.configureTestingModule({
      providers: [{ provide: KDS_HUB_CONNECTION, useValue: () => connection }],
    });
    channel = TestBed.inject(KdsBoardChannel);
  });

  afterEach(() => {
    channel.disconnect();
    vi.useRealTimers();
  });

  it('is connected once the first start succeeds', async () => {
    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(0);

    expect(channel.state()).toBe('connected');
  });

  // SignalR's automatic reconnect only covers a link that once worked: a first
  // start against an API still waking up would otherwise never be retried.
  it('keeps retrying the first connection until it works', async () => {
    connection.outcomes.push('fail', 'fail');

    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(0);

    expect(channel.state()).toBe('reconnecting');

    await vi.advanceTimersByTimeAsync(KDS_RETRY_MS * 2);

    expect(connection.starts).toBe(3);
    expect(channel.state()).toBe('connected');
  });

  // Whatever was paid while the tablet could not hear the hub is only found
  // by asking the queue again.
  it('reloads the queue once a late first connection finally works', async () => {
    connection.outcomes.push('fail');

    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(KDS_RETRY_MS);

    expect(reloads).toBe(1);
  });

  it('says so while the link is down', async () => {
    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(0);

    connection.losesTheLink();

    expect(channel.state()).toBe('reconnecting');
  });

  it('reloads the queue when the link comes back', async () => {
    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(0);

    connection.losesTheLink();
    connection.getsItBack();

    expect(channel.state()).toBe('connected');
    expect(reloads).toBe(1);
  });

  // The night is long: a connection that closes for good starts over instead
  // of leaving the board silent until somebody reloads the tablet.
  it('starts over when the connection closes for good', async () => {
    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(0);

    connection.givesUp();
    await vi.advanceTimersByTimeAsync(KDS_RETRY_MS);

    expect(connection.starts).toBe(2);
    expect(channel.state()).toBe('connected');
    expect(reloads).toBe(1);
  });

  it('stops retrying once the screen closes', async () => {
    connection.outcomes.push('fail');

    channel.connect(reload);
    await vi.advanceTimersByTimeAsync(0);
    channel.disconnect();
    await vi.advanceTimersByTimeAsync(KDS_RETRY_MS * 3);

    expect(connection.starts).toBe(1);
    expect(connection.stopped).toBe(true);
  });
});
