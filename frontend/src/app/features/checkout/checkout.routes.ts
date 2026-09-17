import type { Routes } from '@angular/router';

/**
 * No guard, like the menu and the order: the same anonymous visit, two taps
 * further in. Nothing here belongs to an account, because there are none.
 */
export const checkoutRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/checkout.page').then((m) => m.CheckoutPage),
  },
];
