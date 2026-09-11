import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import type { ActivatedRouteSnapshot, UrlTree } from '@angular/router';
import { authenticatedGuard } from './authenticated-guard';
import { SessionStorage } from './session-storage';
import type { StaffSession } from './staff-session';

const aSession: StaffSession = {
  token: 'un-token',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  username: 'euge',
  role: 'Administrator',
};

/** Only the venue slug is read from the route, so only that is stood up. */
function routeFor(venueSlug: string): ActivatedRouteSnapshot {
  return {
    paramMap: new Map([['venueSlug', venueSlug]]),
    parent: null,
  } as unknown as ActivatedRouteSnapshot;
}

function run(venueSlug = 'bar-alfa'): boolean | UrlTree {
  return TestBed.runInInjectionContext(
    () => authenticatedGuard(routeFor(venueSlug), {} as never) as boolean | UrlTree,
  );
}

describe('authenticatedGuard', () => {
  let sessions: SessionStorage;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    sessions = TestBed.inject(SessionStorage);
    router = TestBed.inject(Router);
    sessions.forget();
  });

  it('Activate_WhenThereIsASession_LetsThemThrough', () => {
    sessions.remember(aSession);

    expect(run()).toBe(true);
  });

  it('Activate_WhenNobodySignedInOnThisTab_DoesNotClaimTheShiftRanOut', () => {
    const destination = router.serializeUrl(run() as UrlTree);

    expect(destination).toBe('/bar-alfa/personal/ingresar');
  });

  it('Activate_WhenTheApiRejectedTheToken_SaysTheShiftRanOut', () => {
    sessions.remember(aSession);
    sessions.forget('expired');

    const destination = router.serializeUrl(run() as UrlTree);

    expect(destination).toBe('/bar-alfa/personal/ingresar?vencida=true');
  });

  it('Activate_WhenTurnedAway_SendsThemBackToTheirOwnVenue', () => {
    const destination = router.serializeUrl(run('bar-beta') as UrlTree);

    expect(destination).toContain('/bar-beta/');
  });
});
