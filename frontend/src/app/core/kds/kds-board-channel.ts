import { Injectable, inject } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { API_BASE_URL } from '../api/api-base-url-interceptor';
import { SessionStorage } from '../auth/session-storage';

/** Must match KdsHub.BoardChanged on the backend — the one message this hub ever sends. */
export const BOARD_CHANGED_MESSAGE = 'BoardChanged';

/**
 * Where the hub lives. The same rule apiBaseUrlInterceptor applies to
 * HttpClient, done by hand: the SignalR client makes its own requests, so the
 * interceptor never sees them, and deployed a relative /api would land on the
 * Static Web App instead of the API.
 */
export function kdsHubUrl(apiBaseUrl: string): string {
  return apiBaseUrl === '' ? '/api/hubs/kds' : `${apiBaseUrl}/hubs/kds`;
}

/**
 * US-15: the live half of the board. A thin wrapper so the page never touches
 * @microsoft/signalr directly — everything the page needs from it is "tell me
 * when the queue changed."
 *
 * providedIn: 'root', not the page's own providers, so a test can override
 * this singleton in the TestBed the ordinary way instead of fighting a
 * component-level provider for it.
 */
@Injectable({ providedIn: 'root' })
export class KdsBoardChannel {
  private readonly sessions = inject(SessionStorage);

  private readonly hubUrl = kdsHubUrl(inject(API_BASE_URL));

  private connection: HubConnection | null = null;

  /**
   * Idempotent: a screen reopened while the last one's teardown is still in
   * flight does not end up with two connections racing each other.
   */
  connect(onChanged: () => void): void {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl(this.hubUrl, {
        accessTokenFactory: () => this.sessions.session()?.token ?? '',
        // The client's default is true, and FrontendCors deliberately allows
        // no credentials: the token travels in accessTokenFactory, not in a
        // cookie. Left on, the cross-origin negotiate is refused in Azure.
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .build();

    connection.on(BOARD_CHANGED_MESSAGE, onChanged);
    this.connection = connection;

    void connection.start();
  }

  /** Called from the page's own teardown — a root-provided service outlives any one screen. */
  disconnect(): void {
    const connection = this.connection;

    this.connection = null;
    void connection?.stop();
  }
}
