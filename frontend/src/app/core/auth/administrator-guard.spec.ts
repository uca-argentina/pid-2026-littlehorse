import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { ActivatedRouteSnapshot, UrlTree } from '@angular/router';
import { administratorGuard } from './administrator-guard';
import { SessionStorage } from './session-storage';
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

function run(venueSlug = 'bar-alfa'): boolean | UrlTree {
  return TestBed.runInInjectionContext(
    () => administratorGuard(routeFor(venueSlug), {} as never) as boolean | UrlTree,
  );
}

describe('administratorGuard', () => {
  let sessions: SessionStorage;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    sessions = TestBed.inject(SessionStorage);
    router = TestBed.inject(Router);
    sessions.forget();
  });

  it('lets an administrator into the administration screens', () => {
    sessions.remember(sessionFor('Administrator'));

    expect(run()).toBe(true);
  });

  // US-03, criterion 6. Written by exclusion so a role added later is locked
  // out until somebody decides otherwise, instead of being let in by omission.
  it.each(['Kds', 'Waiter', 'Cashier'])('turns a %s away from them', (role) => {
    sessions.remember(sessionFor(role));

    expect(run()).not.toBe(true);
  });

  /**
   * Back to their own home screen, not to the login screen: their token is
   * perfectly good, and signing in again would change nothing about their role.
   * Bouncing them to login would be a loop with no way out.
   */
  it('sends whoever is turned away to their own screen, not to the login', () => {
    sessions.remember(sessionFor('Kds'));

    expect(router.serializeUrl(run() as UrlTree)).toBe('/bar-alfa/staff');
  });

  it('keeps someone turned away inside their own venue', () => {
    sessions.remember(sessionFor('Waiter'));

    expect(router.serializeUrl(run('bar-beta') as UrlTree)).toContain('/bar-beta/');
  });

  // The authenticated guard runs first and sends them to the login screen, but
  // nothing forces the order, so this one has to fail closed on its own.
  it('turns away someone with no session at all', () => {
    expect(run()).not.toBe(true);
  });
});
