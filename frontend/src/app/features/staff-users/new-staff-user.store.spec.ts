import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { NewStaffUserStore } from './new-staff-user.store';
import { StaffUsersService } from './staff-users.service';
import type { StaffUser } from './staff-users.service';

function rejectedWith(status: number, type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: { type } });
}

const created: StaffUser = {
  id: '0199a0d2-0000-7000-8000-000000000001',
  username: 'martin.p',
  role: 'Waiter',
  isActive: true,
};

const aNewUser = { username: 'martin.p', password: 'a long enough one', role: 'Waiter' };

describe('NewStaffUserStore', () => {
  let store: NewStaffUserStore;
  let create: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(() => {
    create = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        NewStaffUserStore,
        { provide: StaffUsersService, useValue: { create } },
      ],
    });

    store = TestBed.inject(NewStaffUserStore);
    router = TestBed.inject(Router);
  });

  // Two taps on a slow connection must not create the same person twice.
  it('does not send twice while a request is in flight', () => {
    create.mockReturnValue(new Subject<StaffUser>());

    store.submit('bar-alfa', aNewUser);
    store.submit('bar-alfa', aNewUser);

    expect(create).toHaveBeenCalledTimes(1);
  });

  it('goes back to the listing once the person exists', async () => {
    create.mockReturnValue(of(created));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewUser);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff', 'users']);
    expect(store.status()).toBe('idle');
  });

  // Criterion 3. The one failure the administrator can fix on the spot, so it
  // has to be told apart from everything else that can go wrong.
  it('says the username is taken when this venue already has it', () => {
    create.mockReturnValue(throwError(() => rejectedWith(409, ProblemTypes.usernameTaken)));

    store.submit('bar-alfa', aNewUser);

    expect(store.status()).toBe('usernameTaken');
  });

  it('says the password is too short when the API refuses it', () => {
    create.mockReturnValue(throwError(() => rejectedWith(400, ProblemTypes.passwordTooShort)));

    store.submit('bar-alfa', aNewUser);

    expect(store.status()).toBe('passwordTooShort');
  });

  // A broken domain rule the form did not catch, a network that dropped, a 500.
  // None of them are the administrator's fault and none are worth their own
  // message: what matters is that nothing was created.
  it('falls back to a single failure for anything else', () => {
    create.mockReturnValue(throwError(() => rejectedWith(500, 'about:blank')));

    store.submit('bar-alfa', aNewUser);

    expect(store.status()).toBe('unreachable');
  });

  it('clears a previous failure when the next attempt starts', () => {
    create.mockReturnValueOnce(throwError(() => rejectedWith(409, ProblemTypes.usernameTaken)));
    store.submit('bar-alfa', aNewUser);

    create.mockReturnValueOnce(new Subject<StaffUser>());
    store.submit('bar-alfa', { ...aNewUser, username: 'martin.pe' });

    expect(store.status()).toBe('sending');
  });

  /**
   * A navigation that silently refuses would leave the administrator looking at
   * a form that seemingly did nothing, when the person was in fact created.
   */
  it('says so when it cannot reach the listing after creating the person', async () => {
    create.mockReturnValue(of(created));
    vi.spyOn(router, 'navigate').mockResolvedValue(false);

    store.submit('bar-alfa', aNewUser);
    await Promise.resolve();

    expect(store.status()).toBe('unreachable');
  });
});
