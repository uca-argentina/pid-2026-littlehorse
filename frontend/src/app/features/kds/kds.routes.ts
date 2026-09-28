import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { kdsGuard } from '../../core/auth/kds-guard';

export const kdsRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, kdsGuard],
    loadComponent: () => import('./pages/kds-board.page').then((m) => m.KdsBoardPage),
  },
];
