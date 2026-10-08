import { Injectable } from '@angular/core';
import { TrackingChannel } from '../../tracking/tracking-channel';

/**
 * US-34: the menu's own live link to the tracking hub. The same channel as
 * the tracking screen's, as a separate instance: the two screens replace each
 * other, and sharing one would let the screen being left drop the connection
 * the screen being opened just made.
 */
@Injectable({ providedIn: 'root' })
export class OrdersInProgressChannel extends TrackingChannel {}
