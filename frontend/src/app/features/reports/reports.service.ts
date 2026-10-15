import type { components } from '../../core/api/schema';
import { NIGHTS_URL } from '../nights/nights.service';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type NightMetrics = components['schemas']['NightMetricsResponse'];

export type PaymentMethodName = NightMetrics['byMethod'][number]['method'];

/** A night's report (US-47 to US-49). The token carries the venue, as with every staff endpoint. */
export function metricsUrl(nightId: string): string {
  return `${NIGHTS_URL}/${nightId}/metrics`;
}
