import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { staffLandsOnItsOwnScreenGuard } from '../../core/auth/staff-landing';

/**
 * The screen for the roles that have no screens yet. A role with its own
 * screen — the administrator, the bar's board — never stops here: the second
 * guard sends them on.
 */
export const staffHomeRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, staffLandsOnItsOwnScreenGuard],
    loadComponent: () => import('./pages/staff-home.page').then((m) => m.StaffHomePage),
  },
];
