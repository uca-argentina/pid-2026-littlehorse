import type { Routes } from '@angular/router';
import { rememberVenueGuard } from './core/venue/remember-venue-guard';

// Every feature is lazy. The customer opens this app once, from a QR, on the
// mobile data of a packed venue: a kilobyte in the initial bundle is paid for
// in seconds of waiting.
//
// Every venue path starts with the venue's slug, staff paths included: the
// venue is never chosen from a screen, it comes from the address that was
// handed out.
export const routes: Routes = [
  {
    path: ':venueSlug/staff/login',
    canActivate: [rememberVenueGuard],
    loadChildren: () =>
      import('./features/staff-login/staff-login.routes').then((m) => m.staffLoginRoutes),
  },
  // Before the bare ':venueSlug/staff' below, which would otherwise match this
  // prefix first and then find no child to show.
  {
    path: ':venueSlug/staff/users',
    canActivate: [rememberVenueGuard],
    loadChildren: () =>
      import('./features/staff-users/staff-users.routes').then((m) => m.staffUsersRoutes),
  },
  {
    path: ':venueSlug/staff/products',
    canActivate: [rememberVenueGuard],
    loadChildren: () => import('./features/products/products.routes').then((m) => m.productsRoutes),
  },
  // The customer's door: what the venue's QR points at.
  {
    path: ':venueSlug/menu',
    loadChildren: () => import('./features/menu/menu.routes').then((m) => m.menuRoutes),
  },
  {
    path: ':venueSlug/staff',
    canActivate: [rememberVenueGuard],
    loadChildren: () =>
      import('./features/staff-home/staff-home.routes').then((m) => m.staffHomeRoutes),
  },
  // The installed PWA always launches here: a web manifest cannot carry a
  // venue, so start_url resolves to the root. Without this the app opens on a
  // blank page.
  {
    path: '',
    pathMatch: 'full',
    loadChildren: () => import('./features/entry/entry.routes').then((m) => m.entryRoutes),
  },
  // A mistyped address is not an error worth its own screen; it is the same
  // situation as arriving with no venue at all.
  { path: '**', redirectTo: '' },
];
