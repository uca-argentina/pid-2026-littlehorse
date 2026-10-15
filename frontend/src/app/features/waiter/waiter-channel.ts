import { Injectable } from '@angular/core';
import { HubChannel } from '../../core/realtime/hub-channel';

/** Must match WaiterHub.TablesChanged on the backend — the one message that hub ever sends. */
export const TABLES_CHANGED_MESSAGE = 'TablesChanged';

/**
 * US-43: the live half of the waiter's phone — what is ready to carry changed,
 * reload the list. A table's order became ready, or another waiter took one.
 */
@Injectable({ providedIn: 'root' })
export class WaiterChannel extends HubChannel {
  protected readonly hub = 'waiter';

  protected readonly message = TABLES_CHANGED_MESSAGE;
}
