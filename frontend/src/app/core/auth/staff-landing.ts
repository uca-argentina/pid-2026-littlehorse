import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import { SessionStorage } from './session-storage';

/**
 * Where somebody goes right after signing in, by role. Decided on 2026-09-15:
 * an administrator has no home screen — they sign in to manage the menu, so
 * the menu is what they see first. US-15 adds the bar's own board the same
 * way. Every other role still lands on the screen that says their screens do
 * not exist yet (US-01, criterion 2), because somebody who signs in and sees
 * nothing cannot tell a working system from a broken one.
 */
export function staffLandingFor(role: string | undefined, venueSlug: string): string[] {
  switch (role) {
    case 'Administrator':
      return [venueSlug, 'staff', 'products'];
    case 'Kds':
      return [venueSlug, 'staff', 'kds'];
    default:
      return [venueSlug, 'staff'];
  }
}

/**
 * Keeps a role that has its own screen from stopping on the "nothing for your
 * role" one: typed by hand or left in a bookmark, the address still works, it
 * just goes on to wherever that role actually lands.
 */
export const staffLandsOnItsOwnScreenGuard: CanActivateFn = (route) => {
  const sessions = inject(SessionStorage);
  const router = inject(Router);

  const venueSlug =
    route.paramMap.get('venueSlug') ?? route.parent?.paramMap.get('venueSlug') ?? '';
  const landing = staffLandingFor(sessions.session()?.role, venueSlug);

  // This *is* the "nothing yet" screen: nobody to redirect away from it.
  if (landing.at(-1) === 'staff') return true;

  return router.createUrlTree(landing);
};
