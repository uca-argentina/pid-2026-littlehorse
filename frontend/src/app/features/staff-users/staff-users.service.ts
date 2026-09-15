import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import type { components } from '../../core/api/schema';
import type { StaffRole } from '../../core/staff/staff-roles';

/** Taken from the generated contract, so nothing here can drift from the API. */
export type StaffUser = components['schemas']['StaffUserResponse'];

export type NewStaffUser = components['schemas']['CreateStaffUserRequest'];

/**
 * No venue in the path, unlike the login request. Everything here is done with
 * a token, and the token carries the venue: putting it in the URL would offer
 * a knob that must never be turned.
 */
export const STAFF_USERS_URL = '/api/staff/users';

@Injectable({ providedIn: 'root' })
export class StaffUsersService {
  private readonly http = inject(HttpClient);

  create(user: NewStaffUser): Observable<StaffUser> {
    return this.http.post<StaffUser>(STAFF_USERS_URL, user);
  }

  changeRole(id: string, role: StaffRole): Observable<StaffUser> {
    return this.http.put<StaffUser>(`${STAFF_USERS_URL}/${id}/role`, { role });
  }

  resetPassword(id: string, password: string): Observable<StaffUser> {
    return this.http.put<StaffUser>(`${STAFF_USERS_URL}/${id}/password`, { password });
  }

  /**
   * POST and not DELETE, because nothing is deleted. The row stays so the
   * orders that person prepared keep pointing at their account.
   */
  deactivate(id: string): Observable<StaffUser> {
    return this.http.post<StaffUser>(`${STAFF_USERS_URL}/${id}/deactivate`, {});
  }

  reactivate(id: string): Observable<StaffUser> {
    return this.http.post<StaffUser>(`${STAFF_USERS_URL}/${id}/reactivate`, {});
  }
}
