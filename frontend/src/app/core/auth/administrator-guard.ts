import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import { SessionStorage } from './session-storage';

/**
 * Keeps the administration screens to administrators. Runs after
 * {@link authenticatedGuard}, which is the one that deals with having no
 * session at all, but fails closed on its own because nothing enforces that
 * order.
 *
 * This is half of US-03's sixth criterion, and the half that only hides a
 * screen. The other half is the API, which answers 403 to the same person: a
 * guard that anyone can edit out of the bundle is not a permission.
 */
export const administratorGuard: CanActivateFn = (route) => {
  const sessions = inject(SessionStorage);
  const router = inject(Router);

  if (sessions.session()?.role === 'Administrator') return true;

  const venueSlug = route.paramMap.get('venueSlug') ?? route.parent?.paramMap.get('venueSlug');

  // Their own screen, not the login one. The token is good and signing in
  // again would change nothing about their role, so sending them there would
  // be a loop with no way out.
  return router.createUrlTree([venueSlug ?? '', 'staff']);
};
