import { InjectionToken, inject, signal } from '@angular/core';
import { HttpError, HubConnectionBuilder } from '@microsoft/signalr';
import { API_BASE_URL } from '../api/api-base-url-interceptor';
import { SessionStorage } from '../auth/session-storage';

/**
 * How long to wait between attempts, forever. SignalR's default gives up after
 * four tries, about 45 seconds: a wifi cut longer than that in a boliche would
 * leave the board silent for the rest of the night.
 */
export const HUB_RETRY_MS = 5_000;

/**
 * The hub said the token is no good any more — the shift outlived it. Asking
 * again with the same token can only get the same answer (US-32).
 */
function isRefusedToken(error: unknown): boolean {
  return error instanceof HttpError && error.statusCode === 401;
}

/**
 * SignalR's own reconnect loop, which runs before the channel hears of any
 * close: forever for a dropped link, never for a refused token. Returning null
 * is how that loop is told to stop, and then the connection closes.
 */
export function hubRetryDelay(retryReason: Error | undefined): number | null {
  return isRefusedToken(retryReason) ? null : HUB_RETRY_MS;
}

/** Whether the screen is hearing its hub, as it needs to tell whoever is using it. */
export type HubLinkState = 'connecting' | 'connected' | 'reconnecting';

/** The part of a SignalR HubConnection this channel uses — what a test stands in for. */
export interface HubConnectionLike {
  on(method: string, handler: () => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: (error?: Error) => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(method: string, ...args: unknown[]): Promise<unknown>;
}

/**
 * Where the hub lives. The same rule apiBaseUrlInterceptor applies to
 * HttpClient, done by hand: the SignalR client makes its own requests, so the
 * interceptor never sees them, and deployed a relative /api would land on the
 * Static Web App instead of the API.
 */
export function hubUrl(apiBaseUrl: string, hub: string): string {
  return apiBaseUrl === '' ? `/api/hubs/${hub}` : `${apiBaseUrl}/hubs/${hub}`;
}

/** Builds the real connection. A token so a test can hand over a double instead. */
export const HUB_CONNECTION = new InjectionToken<
  (url: string, accessTokenFactory: () => string) => HubConnectionLike
>('HUB_CONNECTION', {
  factory: () => (url, accessTokenFactory) =>
    new HubConnectionBuilder()
      .withUrl(url, {
        accessTokenFactory,
        // The client's default is true, and FrontendCors deliberately allows
        // no credentials: the token travels in accessTokenFactory, not in a
        // cookie. Left on, the cross-origin negotiate is refused in Azure.
        withCredentials: false,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => hubRetryDelay(context.retryReason),
      })
      .build(),
});

/**
 * The live half of a screen (US-15 for the bar, US-26 for the till, US-22 for
 * the customer following an order). A thin wrapper so a page never touches
 * @microsoft/signalr directly — everything it needs is "tell me when my list
 * changed", and whether it can still hear that. Each hub is a subclass that
 * names its route and message.
 */
export abstract class HubChannel {
  /** The hub's route under /hubs, as the backend maps it. */
  protected abstract readonly hub: string;

  /** The one message that hub sends, as the backend names it. */
  protected abstract readonly message: string;

  private readonly sessions = inject(SessionStorage);

  private readonly apiBaseUrl = inject(API_BASE_URL);

  private readonly createConnection = inject(HUB_CONNECTION);

  private readonly current = signal<HubLinkState>('connecting');

  readonly state = this.current.asReadonly();

  private connection: HubConnectionLike | null = null;

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

    const connection = this.createConnection(hubUrl(this.apiBaseUrl, this.hub), () =>
      this.accessToken(),
    );

    connection.on(this.message, onChanged);
    connection.onreconnecting(() => this.current.set('reconnecting'));
    connection.onreconnected(() => {
      this.current.set('connected');
      onChanged();
      this.joined?.(connection, onChanged);
    });
    // Only still ours when the automatic reconnect gave up or the server shut
    // it: disconnect() lets go of it before stopping.
    connection.onclose((error) => {
      if (this.connection !== connection) return;
      if (isRefusedToken(error)) return this.sessionIsOver();

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
    connection: HubConnectionLike,
    onChanged: () => void,
    afterAGap: boolean,
  ): Promise<void> {
    try {
      await connection.start();
    } catch (error: unknown) {
      if (this.connection !== connection) return;
      if (isRefusedToken(error)) return this.sessionIsOver();

      this.current.set('reconnecting');
      this.startLater(connection, onChanged);

      return;
    }

    if (this.connection !== connection) return;

    this.current.set('connected');
    if (afterAGap) onChanged();
    this.joined?.(connection, onChanged);
  }

  /** What the hub is asked to prove who is listening: the staff session, by default. */
  protected accessToken(): string {
    return this.sessions.session()?.token ?? '';
  }

  /**
   * Runs every time the connection is (again) up. A staff hub needs nothing
   * here — the token already told it which venue's group to join — but one
   * that is joined by asking has to ask on every connection: the server
   * forgets a dropped one's groups.
   */
  protected joined?(connection: HubConnectionLike, onChanged: () => void): void;

  /**
   * Drops the session the same way authenticationInterceptor does for an
   * ordinary request: the SignalR client makes its own requests, so the
   * interceptor never sees this 401. The screen takes it from there.
   */
  private sessionIsOver(): void {
    this.sessions.forget('expired');
  }

  private startLater(connection: HubConnectionLike, onChanged: () => void): void {
    this.retry = setTimeout(() => {
      this.retry = null;
      void this.start(connection, onChanged, true);
    }, HUB_RETRY_MS);
  }
}
