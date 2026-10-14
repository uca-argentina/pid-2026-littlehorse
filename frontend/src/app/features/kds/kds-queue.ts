import type { components } from '../../core/api/schema';

/** No venue in the path: the station's token carries it, same as every other staff route. */
export const KDS_QUEUE_URL = '/api/kds/queue';

/** US-36: which bar this tablet is, by the name it goes by now. */
export const KDS_STATION_URL = '/api/kds/station';

export type KdsStation = components['schemas']['KdsStationResponse'];

/** Taken from the generated contract, so nothing here can drift from the API. */
export type KdsQueueOrder = components['schemas']['KdsQueueOrderResponse'];

/** Which column an order is in — from the contract, so a column name cannot drift. */
export type KdsOrderStatus = components['schemas']['KdsOrderStatus'];
