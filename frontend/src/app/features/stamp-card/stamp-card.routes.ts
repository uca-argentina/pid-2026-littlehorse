import type { Routes } from '@angular/router';
import { administratorGuard } from '../../core/auth/administrator-guard';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

/** Both guards, in order, like the promotions' routes. */
export const stampCardRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorGuard],
    loadComponent: () => import('./pages/stamp-card.page').then((m) => m.StampCardPage),
  },
];
