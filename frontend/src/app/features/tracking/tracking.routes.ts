import type { Routes } from '@angular/router';

/**
 * The address of one order, code and token.
 *
 * No guard: there is no account to check. The token in the path is the proof —
 * the customer's only one — and the API is what checks it.
 */
export const trackingRoutes: Routes = [
  {
    path: ':code/:token',
    loadComponent: () => import('./pages/tracking.page').then((m) => m.TrackingPage),
  },
];
