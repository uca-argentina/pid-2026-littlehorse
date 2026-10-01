import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { cashierGuard } from '../../core/auth/cashier-guard';

export const cashierRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, cashierGuard],
    loadComponent: () => import('./pages/cashier.page').then((m) => m.CashierPage),
  },
];
