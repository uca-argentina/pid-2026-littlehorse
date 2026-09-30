import { Injectable } from '@angular/core';
import { HubChannel } from '../../core/realtime/hub-channel';

/** Must match TillHub.TillChanged on the backend — the one message that hub ever sends. */
export const TILL_CHANGED_MESSAGE = 'TillChanged';

/**
 * US-26: the live half of the till — what waits for cash changed, reload
 * "Por cobrar". A customer confirmed paying in cash, or another till just
 * collected one.
 */
@Injectable({ providedIn: 'root' })
export class TillChannel extends HubChannel {
  protected readonly hub = 'till';

  protected readonly message = TILL_CHANGED_MESSAGE;
}
