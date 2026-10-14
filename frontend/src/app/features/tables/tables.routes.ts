import type { Routes } from '@angular/router';
import { administratorGuard } from '../../core/auth/administrator-guard';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

/**
 * Both guards on the parent, in order: the first deals with having no session,
 * the second with having one that is not an administrator's. Neither is a
 * permission on its own — the API answers 403 to the same person — but they are
 * what keeps somebody from staring at a screen that will refuse every request.
 */
export const tablesRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/tables.page').then((m) => m.TablesPage),
      },
      {
        path: 'new',
        loadComponent: () => import('./pages/new-table.page').then((m) => m.NewTablePage),
      },
    ],
  },
];
