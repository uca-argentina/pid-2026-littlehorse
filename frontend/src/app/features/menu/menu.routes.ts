import type { Routes } from '@angular/router';

/**
 * No guard at all: this is the one screen anybody reaches with nothing but the
 * address on a QR, which is criterion 2 of US-09. `rememberVenueGuard` writes
 * to the device's storage, which is exactly what a customer's anonymous visit
 * must never do — it would also overwrite a staff member's own venue.
 */
export const menuRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/menu.page').then((m) => m.MenuPage),
  },
];
