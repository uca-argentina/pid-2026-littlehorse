import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { ActivatedRouteSnapshot, UrlTree } from '@angular/router';
import { SessionStorage } from './session-storage';
import { administratorLandsOnProductsGuard, staffLandingFor } from './staff-landing';
import type { StaffSession } from './staff-session';

function sessionFor(role: string): StaffSession {
  return {
    token: 'un-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    username: 'euge',
    role,
  };
}

function routeFor(venueSlug: string): ActivatedRouteSnapshot {
  return {
    paramMap: new Map([['venueSlug', venueSlug]]),
    parent: null,
  } as unknown as ActivatedRouteSnapshot;
}

describe('staffLandingFor', () => {
  // Decided on 2026-09-15: there is no home screen for an administrator. They
  // sign in to manage the menu, so that is what they see first.
  it('sends an administrator straight to the products', () => {
    expect(staffLandingFor('Administrator', 'bar-alfa')).toEqual(['bar-alfa', 'staff', 'products']);
  });

  // US-01, criterion 2: a role with no screens yet is told so, instead of
  // being left on an empty page.
  it.each(['Kds', 'Waiter', undefined])(
    'sends %s to the screen that says there is nothing yet',
    (role) => {
      expect(staffLandingFor(role, 'bar-alfa')).toEqual(['bar-alfa', 'staff']);
    },
  );
});

describe('administratorLandsOnProductsGuard', () => {
  let sessions: SessionStorage;
  let router: Router;

  function run(): boolean | UrlTree {
    return TestBed.runInInjectionContext(
      () =>
        administratorLandsOnProductsGuard(routeFor('bar-alfa'), {} as never) as boolean | UrlTree,
    );
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    sessions = TestBed.inject(SessionStorage);
    router = TestBed.inject(Router);
    sessions.forget();
  });

  // Typed by hand, or left in a bookmark from before: the address still works,
  // it just does not stop on a screen that no longer exists for them.
  it('redirects an administrator to the products', () => {
    sessions.remember(sessionFor('Administrator'));

    expect(router.serializeUrl(run() as UrlTree)).toBe('/bar-alfa/staff/products');
  });

  it.each(['Kds', 'Waiter'])('lets a %s stay', (role) => {
    sessions.remember(sessionFor(role));

    expect(run()).toBe(true);
  });
});
