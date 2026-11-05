import type { Routes } from '@angular/router';
import { administratorGuard } from '../../core/auth/administrator-guard';
import { authenticatedGuard } from '../../core/auth/authenticated-guard';

/** Both guards on the parent, in order, like the products' routes. */
export const promotionsRoutes: Routes = [
  {
    path: '',
    canActivate: [authenticatedGuard, administratorGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/promotions.page').then((m) => m.PromotionsPage),
      },
      {
        path: 'new',
        loadComponent: () => import('./pages/promotion.page').then((m) => m.PromotionPage),
      },
      // After 'new', which would otherwise be read as somebody's id.
      {
        path: ':id',
        loadComponent: () => import('./pages/promotion.page').then((m) => m.PromotionPage),
      },
    ],
  },
];
