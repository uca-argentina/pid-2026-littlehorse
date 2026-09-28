import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { API_BASE_URL } from '../api/api-base-url-interceptor';
import { SessionStorage } from '../auth/session-storage';

/** Must match KdsHub.BoardChanged on the backend — the one message this hub ever sends. */
export const BOARD_CHANGED_MESSAGE = 'BoardChanged';

/**
 * How long to wait between attempts, forever. SignalR's default gives up after
 * four tries, about 45 seconds: a wifi cut longer than that in a boliche would
 * leave the board silent for the rest of the night.
 */
export const KDS_RETRY_MS = 5_000;

/** Whether the board is hearing the hub, as the screen needs to tell the bartender. */
export type KdsLinkState = 'connecting' | 'connected' | 'reconnecting';

/** The part of a SignalR HubConnection this channel uses — what a test stands in for. */
export interface KdsHubConnection {
  on(method: string, handler: () => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

/**
 * Where the hub lives. The same rule apiBaseUrlInterceptor applies to
 * HttpClient, done by hand: the SignalR client makes its own requests, so the
 * interceptor never sees them, and deployed a relative /api would land on the
 * Static Web App instead of the API.
 */
export function kdsHubUrl(apiBaseUrl: string): string {
  return apiBaseUrl === '' ? '/api/hubs/kds' : `${apiBaseUrl}/hubs/kds`;
}

/** Builds the real connection. A token so a test can hand over a double instead. */
export const KDS_HUB_CONNECTION = new InjectionToken<
  (url: string, accessTokenFactory: () => string) => KdsHubConnection
>('KDS_HUB_CONNECTION', {
  factory: () => (url, accessTokenFactory) =>
    new HubConnectionBuilder()
      .withUrl(url, {
        accessTokenFactory,
        // The client's default is true, and FrontendCors deliberately allows
        // no credentials: the token travels in accessTokenFactory, not in a
        // cookie. Left on, the cross-origin negotiate is refused in Azure.
        withCredentials: false,
      })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => KDS_RETRY_MS })
      .build(),
});

/**
 * US-15: the live half of the board. A thin wrapper so the page never touches
 * @microsoft/signalr directly — everything the page needs from it is "tell me
 * when the queue changed", and whether it can still hear that.
 *
 * providedIn: 'root', not the page's own providers, so a test can override
 * this singleton in the TestBed the ordinary way instead of fighting a
 * component-level provider for it.
 */
@Injectable({ providedIn: 'root' })
export class KdsBoardChannel {
  private readonly sessions = inject(SessionStorage);

  private readonly hubUrl = kdsHubUrl(inject(API_BASE_URL));

  private readonly createConnection = inject(KDS_HUB_CONNECTION);

  private readonly current = signal<KdsLinkState>('connecting');

  readonly state = this.current.asReadonly();

  private connection: KdsHubConnection | null = null;

  private retry: ReturnType<typeof setTimeout> | null = null;

  /**
   * Idempotent: a screen reopened while the last one's teardown is still in
   * flight does not end up with two connections racing each other.
   *
   * onChanged also runs whenever the link comes back after being down: the
   * messages sent meanwhile are gone, so asking for the queue again is the
   * only way to see what was paid during the gap.
   */
  connect(onChanged: () => void): void {
    if (this.connection) return;

    const connection = this.createConnection(
      this.hubUrl,
      () => this.sessions.session()?.token ?? '',
    );

    connection.on(BOARD_CHANGED_MESSAGE, onChanged);
    connection.onreconnecting(() => this.current.set('reconnecting'));
    connection.onreconnected(() => {
      this.current.set('connected');
      onChanged();
    });
    // Only still ours when the automatic reconnect gave up or the server shut
    // it: disconnect() lets go of it before stopping.
    connection.onclose(() => {
      if (this.connection !== connection) return;

      this.current.set('reconnecting');
      this.startLater(connection, onChanged);
    });

    this.connection = connection;
    this.current.set('connecting');

    void this.start(connection, onChanged, false);
  }

  /** Called from the page's own teardown — a root-provided service outlives any one screen. */
  disconnect(): void {
    const connection = this.connection;

    this.connection = null;
    if (this.retry !== null) clearTimeout(this.retry);
    this.retry = null;

    void connection?.stop();
  }

  private async start(
    connection: KdsHubConnection,
    onChanged: () => void,
    afterAGap: boolean,
  ): Promise<void> {
    try {
      await connection.start();
    } catch {
      if (this.connection !== connection) return;

      this.current.set('reconnecting');
      this.startLater(connection, onChanged);

      return;
    }

    if (this.connection !== connection) return;

    this.current.set('connected');
    if (afterAGap) onChanged();
  }

  private startLater(connection: KdsHubConnection, onChanged: () => void): void {
    this.retry = setTimeout(() => {
      this.retry = null;
      void this.start(connection, onChanged, true);
    }, KDS_RETRY_MS);
  }
}
