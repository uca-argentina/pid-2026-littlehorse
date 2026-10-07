import type { Routes } from '@angular/router';
import { administratorGuard } from '../../core/auth/administrator-guard';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

/** Both guards on the parent, in the same order and for the same reason as staffUsersRoutes. */
export const nightsRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/nights.page').then((m) => m.NightsPage),
      },
      {
        path: 'new',
        loadComponent: () => import('./pages/new-night.page').then((m) => m.NewNightPage),
      },
    ],
  },
];
