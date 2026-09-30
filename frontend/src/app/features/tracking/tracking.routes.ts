import type { ActivatedRouteSnapshot, Routes } from '@angular/router';

/**
 * The address of one order, code and token.
 *
 * No guard: there is no account to check. The token in the path is the proof —
 * the customer's only one — and the API is what checks it.
 */
export const trackingRoutes: Routes = [
  {
    // US-24: where Mercado Pago's page sends the customer back — the order's
    // own address, one segment longer.
    path: ':code/:token/payment',
    // Mercado Pago names it payment_id in the query string; the screen only
    // knows its own input, so nothing in it depends on the gateway's naming.
    resolve: {
      paymentId: (route: ActivatedRouteSnapshot) => route.queryParamMap.get('payment_id'),
    },
    loadComponent: () => import('./pages/payment-return.page').then((m) => m.PaymentReturnPage),
  },
  {
    path: ':code/:token',
    loadComponent: () => import('./pages/tracking.page').then((m) => m.TrackingPage),
  },
];
