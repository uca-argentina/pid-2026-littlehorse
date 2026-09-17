import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { anonymously } from '../../core/auth/anonymous-request';
import type { StaffCredentials, StaffSession } from '../../core/auth/staff-session';

@Injectable({ providedIn: 'root' })
export class StaffLoginService {
  private readonly http = inject(HttpClient);

  /**
   * The venue is in the path because this is the one request a staff member
   * makes before they have a token. Knowing a slug grants nothing: the
   * credentials still have to be right.
   *
   * Sent anonymously on purpose. The API prefers a token's venue claim over the
   * slug, so a session left behind by whoever used this tablet last would send
   * the login to that venue instead of this one.
   */
  logIn(venueSlug: string, credentials: StaffCredentials): Observable<StaffSession> {
    return this.http.post<StaffSession>(`/api/${venueSlug}/auth/login`, credentials, {
      context: anonymously(),
    });
  }
}
