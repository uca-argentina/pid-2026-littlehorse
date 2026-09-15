import type { Routes } from '@angular/router';
import { rememberVenueGuard } from '../../core/venue/remember-venue-guard';

/**
 * No guard about sessions: this is the one screen anybody reaches with nothing
 * but the address on a QR, which is criterion 2 of US-09.
 */
export const menuRoutes: Routes = [
  {
    path: '',
    canActivate: [rememberVenueGuard],
    loadComponent: () => import('./pages/menu.page').then((m) => m.MenuPage),
  },
];
