import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { ActivatedRouteSnapshot, UrlTree } from '@angular/router';
import { waiterGuard } from './waiter-guard';
import { SessionStorage } from './session-storage';
import type { StaffSession } from './staff-session';
import type { StaffRole } from '../staff/staff-roles';

function sessionFor(role: StaffRole): StaffSession {
  return {
    token: 'un-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    username: 'euge',
    displayName: null,
    role,
  };
}

function routeFor(venueSlug: string): ActivatedRouteSnapshot {
  return {
    paramMap: new Map([['venueSlug', venueSlug]]),
    parent: null,
  } as unknown as ActivatedRouteSnapshot;
}

function run(venueSlug = 'bar-alfa'): boolean | UrlTree {
  return TestBed.runInInjectionContext(
    () => waiterGuard(routeFor(venueSlug), {} as never) as boolean | UrlTree,
  );
}

describe('waiterGuard', () => {
  let sessions: SessionStorage;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    sessions = TestBed.inject(SessionStorage);
    router = TestBed.inject(Router);
    sessions.forget();
  });

  it('lets a waiter in', () => {
    sessions.remember(sessionFor('Waiter'));

    expect(run()).toBe(true);
  });

  // US-43. Written by exclusion so a role added later is locked out until
  // somebody decides otherwise, instead of being let in by omission.
  it.each<StaffRole>(['Administrator', 'Kds', 'Cashier'])(
    'turns a %s away from the waiter screen',
    (role) => {
      sessions.remember(sessionFor(role));

      expect(run()).not.toBe(true);
    },
  );

  /**
   * Back to their own home screen, not to the login screen: their token is
   * perfectly good, and signing in again would change nothing about their role.
   */
  it('sends whoever is turned away to their own screen, not to the login', () => {
    sessions.remember(sessionFor('Administrator'));

    expect(router.serializeUrl(run() as UrlTree)).toBe('/bar-alfa/staff');
  });

  it('keeps someone turned away inside their own venue', () => {
    sessions.remember(sessionFor('Cashier'));

    expect(router.serializeUrl(run('bar-beta') as UrlTree)).toContain('/bar-beta/');
  });

  // The authenticated guard runs first and sends them to the login screen, but
  // nothing forces the order, so this one has to fail closed on its own.
  it('turns away someone with no session at all', () => {
    expect(run()).not.toBe(true);
  });
});
