import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Subject, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { SessionStorage } from '../../core/auth/session-storage';
import type { StaffSession } from '../../core/auth/staff-session';
import { StaffLoginService } from './staff-login.service';
import { StaffLoginStore } from './staff-login.store';

function rejectedWith(type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status: 401, error: { type } });
}

const aSession: StaffSession = {
  token: 'un-token',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  username: 'euge',
  role: 'Administrator',
};

describe('StaffLoginStore', () => {
  let store: StaffLoginStore;
  let logIn: ReturnType<typeof vi.fn>;
  let sessions: SessionStorage;
  let router: Router;

  beforeEach(() => {
    logIn = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        StaffLoginStore,
        { provide: StaffLoginService, useValue: { logIn } },
      ],
    });

    store = TestBed.inject(StaffLoginStore);
    sessions = TestBed.inject(SessionStorage);
    router = TestBed.inject(Router);
  });

  const credentials = { username: 'euge', password: 'a-password' };

  // Two taps on a slow connection must not place two logins.
  it('does not send twice while a request is in flight', () => {
    logIn.mockReturnValue(new Subject<StaffSession>());

    store.submit('bar-alfa', credentials);
    store.submit('bar-alfa', credentials);

    expect(store.isSending()).toBe(true);
    expect(logIn).toHaveBeenCalledTimes(1);
  });

  it('remembers the session when the credentials are valid', () => {
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);

    store.submit('bar-alfa', credentials);
    response.next(aSession);

    expect(sessions.session()).toEqual(aSession);
    expect(sessions.hasSession()).toBe(true);
  });

  // Straight to what they manage: there is no home screen for an administrator.
  it('takes an administrator to the products when the credentials are valid', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);

    store.submit('bar-alfa', credentials);
    response.next(aSession);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff', 'products']);
  });

  // US-01, criterion 2: a role without screens is told so, not left on the login.
  it('takes any other role to the screen that says there is nothing for them yet', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);

    store.submit('bar-alfa', credentials);
    response.next({ ...aSession, role: 'Kds' });

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff']);
  });

  it('reports the same failure whichever field was wrong', () => {
    logIn.mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));

    store.submit('bar-alfa', credentials);

    expect(store.status()).toBe('invalidCredentials');
  });

  it('does not blame the credentials when the API cannot be reached', () => {
    logIn.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    store.submit('bar-alfa', credentials);

    expect(store.status()).toBe('unreachable');
  });

  it('tells an expired shift apart from a failed login', () => {
    store.startAfterExpiry();

    expect(store.status()).toBe('sessionExpired');
  });
});
