import type { Routes } from '@angular/router';

/**
 * No guard, like the menu: this is the same anonymous visit, one tap further
 * in. The order lives on the device, so there is nothing here to protect.
 */
export const orderRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/order.page').then((m) => m.OrderPage),
  },
];
