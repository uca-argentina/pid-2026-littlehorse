import { Injectable } from '@angular/core';
import { HubChannel, hubUrl } from '../realtime/hub-channel';

export {
  HUB_CONNECTION as KDS_HUB_CONNECTION,
  HUB_RETRY_MS as KDS_RETRY_MS,
  hubRetryDelay as kdsRetryDelay,
} from '../realtime/hub-channel';
export type {
  HubConnectionLike as KdsHubConnection,
  HubLinkState as KdsLinkState,
} from '../realtime/hub-channel';

/** Must match KdsHub.BoardChanged on the backend — the one message this hub ever sends. */
export const BOARD_CHANGED_MESSAGE = 'BoardChanged';

/** Where the bar's hub lives: see hubUrl. */
export function kdsHubUrl(apiBaseUrl: string): string {
  return hubUrl(apiBaseUrl, 'kds');
}

/**
 * US-15: the live half of the board — the queue changed, reload it.
 *
 * providedIn: 'root', not the page's own providers, so a test can override
 * this singleton in the TestBed the ordinary way instead of fighting a
 * component-level provider for it.
 */
@Injectable({ providedIn: 'root' })
export class KdsBoardChannel extends HubChannel {
  protected readonly hub = 'kds';

  protected readonly message = BOARD_CHANGED_MESSAGE;
}
