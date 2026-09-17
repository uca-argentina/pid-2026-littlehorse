import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { LastVenue } from './last-venue';

/** Records the venue of any route that carries one. Never blocks. */
export const rememberVenueGuard: CanActivateFn = (route) => {
  const slug = route.paramMap.get('venueSlug');

  if (slug !== null) inject(LastVenue).remember(slug);

  return true;
};
