import type { Routes } from '@angular/router';
import { administratorGuard } from '../../core/auth/administrator-guard';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

/**
 * Both guards on the parent, in order: the first deals with having no session,
 * the second with having one that is not an administrator's. Neither is a
 * permission on its own — the API answers 403 to the same person.
 *
 * There is no listing of its own: the categories show up as the choices of a
 * product's form and as the tabs of the customer's menu, so the only screen
 * they need is the one that adds one.
 */
export const categoriesRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorGuard],
    children: [
      {
        path: 'new',
        loadComponent: () => import('./pages/new-category.page').then((m) => m.NewCategoryPage),
      },
    ],
  },
];
