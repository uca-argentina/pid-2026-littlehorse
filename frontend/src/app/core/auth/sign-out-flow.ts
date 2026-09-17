import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { SessionStorage } from './session-storage';

/**
 * Ending the shift: forgetting the session and leaving for the login are two
 * steps that must not come apart, so they live here once and every button
 * that says "Salir" calls this.
 */
@Injectable({ providedIn: 'root' })
export class SignOutFlow {
  private readonly sessions = inject(SessionStorage);

  private readonly router = inject(Router);

  /** The venue never comes from a screen: it is the one in the address. */
  leave(venueSlug: string): void {
    this.sessions.forget();
    void this.router.navigate([venueSlug, 'staff', 'login']);
  }
}
