import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
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

  function typeValidCredentials(): void {
    store.username.set('euge');
    store.password.set('a-password');
  }

  it('cannot be submitted while nothing is typed', () => {
    expect(store.canSubmit()).toBe(false);
  });

  it('cannot be submitted with only the username typed', () => {
    store.username.set('euge');

    expect(store.canSubmit()).toBe(false);
  });

  it('cannot be submitted with a username of only whitespace', () => {
    store.username.set('   ');
    store.password.set('a-password');

    expect(store.canSubmit()).toBe(false);
  });

  it('can be submitted once both fields are typed', () => {
    typeValidCredentials();

    expect(store.canSubmit()).toBe(true);
  });

  it('does not send twice while a request is in flight', () => {
    logIn.mockReturnValue(new Subject<StaffSession>());
    typeValidCredentials();

    store.submit('bar-alfa');
    store.submit('bar-alfa');

    expect(store.canSubmit()).toBe(false);
    expect(logIn).toHaveBeenCalledTimes(1);
  });

  it('sends the username trimmed', () => {
    logIn.mockReturnValue(new Subject<StaffSession>());
    store.username.set('  euge  ');
    store.password.set('a-password');

    store.submit('bar-alfa');

    expect(logIn).toHaveBeenCalledWith('bar-alfa', {
      username: 'euge',
      password: 'a-password',
    });
  });

  it('remembers the session when the credentials are valid', () => {
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);
    typeValidCredentials();

    store.submit('bar-alfa');
    response.next(aSession);

    expect(sessions.session()).toEqual(aSession);
    expect(sessions.hasSession()).toBe(true);
  });

  it('leaves the login screen when the credentials are valid', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);
    typeValidCredentials();

    store.submit('bar-alfa');
    response.next(aSession);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff']);
  });

  it('reports the same failure whichever field was wrong', () => {
    logIn.mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    typeValidCredentials();

    store.submit('bar-alfa');

    expect(store.status()).toBe('invalidCredentials');
  });

  it('clears the password and keeps the username after a wrong login', () => {
    logIn.mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    typeValidCredentials();

    store.submit('bar-alfa');

    expect(store.password()).toBe('');
    expect(store.username()).toBe('euge');
  });

  it('does not blame the credentials when the API cannot be reached', () => {
    logIn.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    typeValidCredentials();

    store.submit('bar-alfa');

    expect(store.status()).toBe('unreachable');
  });

  it('tells an expired shift apart from a failed login', () => {
    store.startAfterExpiry();

    expect(store.status()).toBe('sessionExpired');
  });
});
