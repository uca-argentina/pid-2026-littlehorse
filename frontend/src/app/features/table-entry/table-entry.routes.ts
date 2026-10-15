import type { Routes } from '@angular/router';

/**
 * No guard, like the menu: the customer arrives with nothing but the address
 * on the QR the waiter handed over.
 */
export const tableEntryRoutes: Routes = [
  {
    path: ':code',
    loadComponent: () => import('./pages/table-entry.page').then((m) => m.TableEntryPage),
  },
];
