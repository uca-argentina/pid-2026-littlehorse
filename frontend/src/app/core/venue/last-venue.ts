import { Injectable } from '@angular/core';

const STORAGE_KEY = 'drinkit.last-venue';

/**
 * Remembers which venue this device was last used at.
 *
 * The installed PWA always launches at the root, because a web manifest cannot
 * carry a venue. Without this the app would open on a screen that can only say
 * "scan your QR again", to someone whose phone is already in the venue.
 *
 * In localStorage, not sessionStorage: it has to survive closing the app, which
 * is the whole point. It holds a slug that was already public in the address
 * bar, never anything from a session.
 */
@Injectable({ providedIn: 'root' })
export class LastVenue {
  remember(slug: string): void {
    try {
      localStorage.setItem(STORAGE_KEY, slug);
    } catch {
      // Private windows throw. Losing this only costs a wrong landing screen.
    }
  }

  read(): string | null {
    try {
      return localStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }
}
