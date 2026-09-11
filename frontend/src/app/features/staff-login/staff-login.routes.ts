import type { Routes } from '@angular/router';
import { StaffLoginStore } from './staff-login.store';

export const staffLoginRoutes: Routes = [
  {
    path: '',
    // Scoped to the route so the screen starts clean every time it opens.
    providers: [StaffLoginStore],
    loadComponent: () => import('./pages/staff-login.page').then((m) => m.StaffLoginPage),
  },
];
