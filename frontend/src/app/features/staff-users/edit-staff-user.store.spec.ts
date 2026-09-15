import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { EditStaffUserStore } from './edit-staff-user.store';
import { StaffUsersService } from './staff-users.service';
import type { StaffUser } from './staff-users.service';

const martin: StaffUser = { id: 'id-1', username: 'martin.p', role: 'Waiter', isActive: true };

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

describe('EditStaffUserStore', () => {
  let store: EditStaffUserStore;
  let staffUsers: Record<string, ReturnType<typeof vi.fn>>;

  function open(overrides: Record<string, ReturnType<typeof vi.fn>> = {}) {
    staffUsers = {
      changeRole: vi.fn().mockReturnValue(of(martin)),
      resetPassword: vi.fn().mockReturnValue(of(martin)),
      deactivate: vi.fn().mockReturnValue(of({ ...martin, isActive: false })),
      reactivate: vi.fn().mockReturnValue(of(martin)),
      ...overrides,
    };

    TestBed.configureTestingModule({
      providers: [EditStaffUserStore, { provide: StaffUsersService, useValue: staffUsers }],
    });

    store = TestBed.inject(EditStaffUserStore);
  }

  describe('changing the role', () => {
    it('sends the role that was picked', () => {
      open();

      store.changeRole('id-1', 'Kds');

      expect(staffUsers['changeRole']).toHaveBeenCalledWith('id-1', 'Kds');
    });

    it('says it worked, so the screen can confirm it', () => {
      open();

      store.changeRole('id-1', 'Kds');

      expect(store.roleStatus()).toBe('saved');
    });

    // Two taps on a slow connection must not send the same change twice.
    it('does not send twice while a request is in flight', () => {
      open({ changeRole: vi.fn().mockReturnValue(new Subject<StaffUser>()) });

      store.changeRole('id-1', 'Kds');
      store.changeRole('id-1', 'Kds');

      expect(staffUsers['changeRole']).toHaveBeenCalledTimes(1);
    });

    // The one refusal an administrator can act on: hand the role to somebody
    // else first, then step down.
    it('says so when it would leave the venue without an administrator', () => {
      open({ changeRole: rejectedWith(409, ProblemTypes.lastAdministrator) });

      store.changeRole('id-1', 'Waiter');

      expect(store.roleStatus()).toBe('lastAdministrator');
    });

    it('falls back to a single failure for anything else', () => {
      open({ changeRole: rejectedWith(500, 'about:blank') });

      store.changeRole('id-1', 'Kds');

      expect(store.roleStatus()).toBe('unreachable');
    });
  });

  describe('resetting the password', () => {
    it('sends the new password', () => {
      open();

      store.resetPassword('id-1', 'a long enough one');

      expect(staffUsers['resetPassword']).toHaveBeenCalledWith('id-1', 'a long enough one');
    });

    it('says it worked', () => {
      open();

      store.resetPassword('id-1', 'a long enough one');

      expect(store.passwordStatus()).toBe('saved');
    });

    it('says the password is too short when the API refuses it', () => {
      open({ resetPassword: rejectedWith(400, ProblemTypes.passwordTooShort) });

      store.resetPassword('id-1', 'corta');

      expect(store.passwordStatus()).toBe('passwordTooShort');
    });
  });

  describe('taking access away and giving it back', () => {
    it('deactivates the person the administrator picked', () => {
      open();

      store.deactivate('id-1');

      expect(staffUsers['deactivate']).toHaveBeenCalledWith('id-1');
      expect(store.accessStatus()).toBe('saved');
    });

    it('reactivates them', () => {
      open();

      store.reactivate('id-1');

      expect(staffUsers['reactivate']).toHaveBeenCalledWith('id-1');
      expect(store.accessStatus()).toBe('saved');
    });

    // The venue must never be left unable to administer itself.
    it('says so when it would take the last administrator', () => {
      open({ deactivate: rejectedWith(409, ProblemTypes.lastAdministrator) });

      store.deactivate('id-1');

      expect(store.accessStatus()).toBe('lastAdministrator');
    });

    it('hands back what the API answered, so the screen shows the new state', () => {
      open();

      store.deactivate('id-1');

      expect(store.updated()?.isActive).toBe(false);
    });
  });

  // Each action reports on its own. A failed password reset must not make the
  // role look like it failed too.
  it('keeps the three outcomes apart', () => {
    open({ resetPassword: rejectedWith(400, ProblemTypes.passwordTooShort) });

    store.changeRole('id-1', 'Kds');
    store.resetPassword('id-1', 'corta');

    expect(store.roleStatus()).toBe('saved');
    expect(store.passwordStatus()).toBe('passwordTooShort');
    expect(store.accessStatus()).toBe('idle');
  });
});
