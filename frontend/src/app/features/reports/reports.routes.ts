import type { Routes } from '@angular/router';
import { administratorGuard } from '../../core/auth/administrator-guard';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

/**
 * Both guards on the parent, in the same order and for the same reason as
 * nightsRoutes. Without a night, the page picks the one that matters and moves
 * on to it.
 */
export const reportsRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/reports.page').then((m) => m.ReportsPage),
      },
      {
        path: ':nightId',
        loadComponent: () => import('./pages/reports.page').then((m) => m.ReportsPage),
      },
    ],
  },
];
