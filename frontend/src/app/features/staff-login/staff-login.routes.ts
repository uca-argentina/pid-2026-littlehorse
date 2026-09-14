import type { Routes } from '@angular/router';

export const staffLoginRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/staff-login.page').then((m) => m.StaffLoginPage),
  },
];
