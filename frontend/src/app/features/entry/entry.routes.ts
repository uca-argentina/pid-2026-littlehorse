import type { Routes } from '@angular/router';

export const entryRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pages/entry.page').then((m) => m.EntryPage),
  },
];
