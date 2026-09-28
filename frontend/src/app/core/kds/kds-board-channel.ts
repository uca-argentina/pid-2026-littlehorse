import { Injectable, inject } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { SessionStorage } from '../auth/session-storage';

export const KDS_HUB_URL = '/api/hubs/kds';

/** Must match KdsHub.BoardChanged on the backend — the one message this hub ever sends. */
export const BOARD_CHANGED_MESSAGE = 'BoardChanged';

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

  private connection: HubConnection | null = null;

  /**
   * Idempotent: a screen reopened while the last one's teardown is still in
   * flight does not end up with two connections racing each other.
   */
  connect(onChanged: () => void): void {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl(KDS_HUB_URL, { accessTokenFactory: () => this.sessions.session()?.token ?? '' })
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
