import type { components } from '../../core/api/schema';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type Menu = components['schemas']['MenuResponse'];

export type MenuItem = components['schemas']['MenuItemResponse'];

/**
 * The venue is in the path because this is the customer's door and there is no
 * token to carry one. It comes from the QR they scanned, never from a screen
 * that asks them to pick a venue.
 */
export function menuUrl(venueSlug: string): string {
  return `/api/${venueSlug}/menu`;
}
