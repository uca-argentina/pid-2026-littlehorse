import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { waiterGuard } from '../../core/auth/waiter-guard';

export const waiterRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, waiterGuard],
    loadComponent: () => import('./pages/waiter.page').then((m) => m.WaiterPage),
  },
];
