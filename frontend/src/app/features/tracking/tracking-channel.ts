import { Injectable } from '@angular/core';
import { HubChannel } from '../../core/realtime/hub-channel';
import type { HubConnectionLike } from '../../core/realtime/hub-channel';

/** Must match TrackingHub.OrderChanged on the backend — the one message that hub ever sends. */
export const ORDER_CHANGED_MESSAGE = 'OrderChanged';

/** Must match TrackingHub.Follow on the backend. */
const FOLLOW = 'Follow';

/**
 * US-22: the live half of the tracking screen — your order moved, ask again.
 *
 * Joined by the order's token rather than by a session: the customer has no
 * account, and the token is the same proof the tracking link asks for.
 */
@Injectable({ providedIn: 'root' })
export class TrackingChannel extends HubChannel {
  protected readonly hub = 'tracking';

  protected readonly message = ORDER_CHANGED_MESSAGE;

  private token = '';

  /** Following another order drops the last one: one screen, one order. */
  follow(token: string, onChanged: () => void): void {
    this.disconnect();
    this.token = token;
    this.connect(onChanged);
  }

  /** Nobody signs in to follow an order, whatever session this phone still holds. */
  protected override accessToken(): string {
    return '';
  }

  /**
   * Asks again once following, not before: a move made while the first answer
   * was on its way and the group not yet joined is heard by nobody, and is
   * never sent again.
   */
  protected override joined(connection: HubConnectionLike, onChanged: () => void): void {
    // A failed invoke means the link dropped, and the reconnect comes back here.
    connection.invoke(FOLLOW, this.token).then(onChanged, () => undefined);
  }
}
