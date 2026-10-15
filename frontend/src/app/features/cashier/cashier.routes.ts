import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { cashierGuard } from '../../core/auth/cashier-guard';

/**
 * The till is two screens under one guard (US-45): the cash to collect, and
 * the VIP tables waiting for a waiter. Only the cashier's own account gets in.
 */
export const cashierRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, cashierGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/cashier.page').then((m) => m.CashierPage),
      },
      {
        path: 'vip',
        loadComponent: () => import('./pages/cashier-vip.page').then((m) => m.CashierVipPage),
      },
    ],
  },
];
