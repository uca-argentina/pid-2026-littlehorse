import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';
import { Router } from '@angular/router';
import type { StaffRole } from '../staff/staff-roles';
import { SessionStorage } from './session-storage';

/**
 * Keeps a screen to the one role it was built for. Runs after
 * {@link authenticatedGuard}, which is the one that deals with having no
 * session at all, but fails closed on its own because nothing enforces that
 * order.
 */
export function roleGuard(role: StaffRole): CanActivateFn {
  return (route) => {
    const sessions = inject(SessionStorage);
    const router = inject(Router);

    if (sessions.session()?.role === role) return true;

    const venueSlug = route.paramMap.get('venueSlug') ?? route.parent?.paramMap.get('venueSlug');

    // Their own screen, not the login one. The token is good and signing in
    // again would change nothing about their role, so sending them there
    // would be a loop with no way out.
    return router.createUrlTree([venueSlug ?? '', 'staff']);
  };
}
