import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { administratorLandsOnProductsGuard } from '../../core/auth/staff-landing';

/**
 * The screen for the roles that have no screens yet. An administrator never
 * stops here: the second guard sends them on to the products.
 */
export const staffHomeRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorLandsOnProductsGuard],
    loadComponent: () => import('./pages/staff-home.page').then((m) => m.StaffHomePage),
  },
];
