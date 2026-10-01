import type { components } from '../../core/api/schema';

/** No venue in the path: the station's token carries it, same as every other staff route. */
export const KDS_QUEUE_URL = '/api/kds/queue';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type KdsQueueOrder = components['schemas']['KdsQueueOrderResponse'];

/** Which column an order is in — from the contract, so a column name cannot drift. */
export type KdsOrderStatus = components['schemas']['KdsOrderStatus'];
