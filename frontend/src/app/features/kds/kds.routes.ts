import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { kdsGuard } from '../../core/auth/kds-guard';

export const kdsRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, kdsGuard],
    loadComponent: () => import('./pages/kds-board.page').then((m) => m.KdsBoardPage),
  },
  {
    // US-20: scans the customer's QR, and searches by hand when it will not read.
    path: 'scan',
    canActivate: [authenticatedGuard, kdsGuard],
    loadComponent: () => import('./pages/kds-scan.page').then((m) => m.KdsScanPage),
  },
];
