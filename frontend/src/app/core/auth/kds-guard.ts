import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import { SessionStorage } from './session-storage';

/**
 * Keeps the bar's board to the bar's own account (US-15, criterion 3). Runs
 * after {@link authenticatedGuard}, which is the one that deals with having
 * no session at all, but fails closed on its own because nothing enforces
 * that order.
 */
export const kdsGuard: CanActivateFn = (route) => {
  const sessions = inject(SessionStorage);
  const router = inject(Router);

  if (sessions.session()?.role === 'Kds') return true;

  const venueSlug = route.paramMap.get('venueSlug') ?? route.parent?.paramMap.get('venueSlug');

  // Their own screen, not the login one. The token is good and signing in
  // again would change nothing about their role, so sending them there would
  // be a loop with no way out.
  return router.createUrlTree([venueSlug ?? '', 'staff']);
};
