import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import { SessionStorage } from './session-storage';

/**
 * Keeps a screen closed until there is a session. The venue comes from the
 * route, so someone who bookmarked a screen lands back on their own venue's
 * login rather than on a guess.
 */
export const authenticatedGuard: CanActivateFn = (route) => {
  const sessions = inject(SessionStorage);
  const router = inject(Router);

  if (sessions.hasSession()) return true;

  const venueSlug = route.paramMap.get('venueSlug') ?? route.parent?.paramMap.get('venueSlug');

  // Only say the shift ran out when it actually did. Someone who simply never
  // signed in on this tab would otherwise be told they had been working for
  // eight hours.
  return router.createUrlTree([venueSlug ?? '', 'staff', 'login'], {
    queryParams: sessions.expired() ? { expired: true } : {},
  });
};
