import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import { SessionStorage } from './session-storage';

/**
 * Where somebody goes right after signing in, by role. Decided on 2026-09-15:
 * an administrator has no home screen — they sign in to manage the menu, so
 * the menu is what they see first. Every other role still lands on the screen
 * that says their screens do not exist yet (US-01, criterion 2), because
 * somebody who signs in and sees nothing cannot tell a working system from a
 * broken one.
 */
export function staffLandingFor(role: string | undefined, venueSlug: string): string[] {
  return role === 'Administrator' ? [venueSlug, 'staff', 'products'] : [venueSlug, 'staff'];
}

/**
 * Keeps an administrator from stopping on the "nothing for your role" screen:
 * typed by hand or left in a bookmark, the address still works, it just goes
 * on to the products.
 */
export const administratorLandsOnProductsGuard: CanActivateFn = (route) => {
  const sessions = inject(SessionStorage);
  const router = inject(Router);

  if (sessions.session()?.role !== 'Administrator') return true;

  const venueSlug = route.paramMap.get('venueSlug') ?? route.parent?.paramMap.get('venueSlug');

  return router.createUrlTree(staffLandingFor('Administrator', venueSlug ?? ''));
};
