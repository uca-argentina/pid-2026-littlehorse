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

/**
 * The confirmation lives under the order's own address rather than under
 * /checkout: it is about one order and it survives a reload, which is what
 * somebody who locked their phone needs. US-12 will serve the same path with
 * the live state.
 */
export const confirmedOrderRoutes: Routes = [
  {
    path: ':code',
    loadComponent: () => import('./pages/order-confirmed.page').then((m) => m.OrderConfirmedPage),
  },
];
