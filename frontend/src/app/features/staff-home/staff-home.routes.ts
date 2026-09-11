import type { Routes } from '@angular/router';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

export const staffHomeRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./pages/staff-home.page').then((m) => m.StaffHomePage),
  },
];
