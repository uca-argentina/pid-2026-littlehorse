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
    store.password.set('una-contrasena');
  }

  it('CanSubmit_WhenNothingIsTyped_IsFalse', () => {
    expect(store.canSubmit()).toBe(false);
  });

  it('CanSubmit_WhenOnlyTheUsernameIsTyped_IsFalse', () => {
    store.username.set('euge');

    expect(store.canSubmit()).toBe(false);
  });

  it('CanSubmit_WhenTheUsernameIsOnlyWhitespace_IsFalse', () => {
    store.username.set('   ');
    store.password.set('una-contrasena');

    expect(store.canSubmit()).toBe(false);
  });

  it('CanSubmit_WhenBothFieldsAreTyped_IsTrue', () => {
    typeValidCredentials();

    expect(store.canSubmit()).toBe(true);
  });

  it('Submit_WhileTheRequestIsInFlight_CannotBeSentAgain', () => {
    logIn.mockReturnValue(new Subject<StaffSession>());
    typeValidCredentials();

    store.submit('bar-alfa');
    store.submit('bar-alfa');

    expect(store.canSubmit()).toBe(false);
    expect(logIn).toHaveBeenCalledTimes(1);
  });

  it('Submit_WhenTheUsernameHasPadding_SendsItTrimmed', () => {
    logIn.mockReturnValue(new Subject<StaffSession>());
    store.username.set('  euge  ');
    store.password.set('una-contrasena');

    store.submit('bar-alfa');

    expect(logIn).toHaveBeenCalledWith('bar-alfa', {
      username: 'euge',
      password: 'una-contrasena',
    });
  });

  it('Submit_WhenCredentialsAreValid_RemembersTheSession', () => {
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);
    typeValidCredentials();

    store.submit('bar-alfa');
    response.next(aSession);

    expect(sessions.session()).toEqual(aSession);
    expect(sessions.hasSession()).toBe(true);
  });

  it('Submit_WhenCredentialsAreValid_LeavesTheLoginScreen', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const response = new Subject<StaffSession>();
    logIn.mockReturnValue(response);
    typeValidCredentials();

    store.submit('bar-alfa');
    response.next(aSession);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'personal']);
  });

  it('Submit_WhenCredentialsAreWrong_ReportsOneFailureForBothCases', () => {
    logIn.mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    typeValidCredentials();

    store.submit('bar-alfa');

    expect(store.status()).toBe('invalidCredentials');
  });

  it('Submit_WhenCredentialsAreWrong_ClearsThePasswordAndKeepsTheUsername', () => {
    logIn.mockReturnValue(throwError(() => rejectedWith(ProblemTypes.invalidCredentials)));
    typeValidCredentials();

    store.submit('bar-alfa');

    expect(store.password()).toBe('');
    expect(store.username()).toBe('euge');
  });

  it('Submit_WhenTheApiCannotBeReached_DoesNotBlameTheCredentials', () => {
    logIn.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    typeValidCredentials();

    store.submit('bar-alfa');

    expect(store.status()).toBe('unreachable');
  });

  it('StartAfterExpiry_WhenTheShiftTokenRanOut_IsNotTheSameAsAFailedLogin', () => {
    store.startAfterExpiry();

    expect(store.status()).toBe('sessionExpired');
  });
});
