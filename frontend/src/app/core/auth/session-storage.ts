import { Injectable, signal } from '@angular/core';
import type { StaffSession } from './staff-session';

const STORAGE_KEY = 'drinkit.staff-session';

/** Why a session ended, which is what decides how the login screen reads. */
type SessionEnding = 'signedOut' | 'expired';

/**
 * Keeps the shift's session. In sessionStorage rather than localStorage: the
 * tablet behind the bar is shared, and a session must not outlive the tab that
 * opened it.
 */
@Injectable({ providedIn: 'root' })
export class SessionStorage {
  private readonly current = signal<StaffSession | null>(this.readStored());

  private readonly endedByExpiry = signal(false);

  readonly session = this.current.asReadonly();

  /** True when the API rejected the last token, not when someone signed out. */
  readonly expired = this.endedByExpiry.asReadonly();

  remember(session: StaffSession): void {
    this.endedByExpiry.set(false);
    this.current.set(session);
    this.write(session);
  }

  forget(ending: SessionEnding = 'signedOut'): void {
    this.endedByExpiry.set(ending === 'expired');
    this.current.set(null);
    this.write(null);
  }

  /**
   * Deliberately does NOT compare expiresAt against the device clock. The bar's
   * tablet can be minutes or hours off, and judging a fresh token by a skewed
   * clock sends someone who just signed in straight back to the login screen,
   * over and over, with nothing on screen to explain it.
   *
   * The API is the authority on whether a token is still good: it answers 401
   * and the interceptor drops the session then. Worst case a screen opens and
   * its first request bounces, which is one round trip and self-correcting.
   */
  hasSession(): boolean {
    return this.current() !== null;
  }

  private readStored(): StaffSession | null {
    try {
      const stored = sessionStorage.getItem(STORAGE_KEY);

      return stored === null ? null : (JSON.parse(stored) as StaffSession);
    } catch {
      // Private windows and locked-down browsers throw on access, and a
      // half-written value throws on parse. Either way there is no session.
      return null;
    }
  }

  private write(session: StaffSession | null): void {
    try {
      if (session === null) sessionStorage.removeItem(STORAGE_KEY);
      else sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch {
      // Storage is a convenience, not the source of truth: the signal above
      // already holds the session for this page load.
    }
  }
}
