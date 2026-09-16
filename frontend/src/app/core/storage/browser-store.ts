import { Injectable } from '@angular/core';

/**
 * What the browser remembers between visits, behind one seam.
 *
 * Two reasons it is a service and not a call to localStorage where it is
 * needed. Reading and writing both throw in a private window, in a browser set
 * to block site data, and when the quota is full, so every caller would carry
 * the same try/catch. And a test has no browser store at all: the runner is
 * Node, `localStorage` is undefined there, and anything that touches it
 * directly fails for a reason that has nothing to do with the code under test.
 */
@Injectable({ providedIn: 'root' })
export class BrowserStore {
  read(key: string): string | null {
    try {
      return localStorage.getItem(key);
    } catch {
      // No store, or one that refuses to answer. Nothing was remembered.
      return null;
    }
  }

  write(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
    } catch {
      // Nothing will survive this visit. Whatever is on screen still holds,
      // and refusing to work at all would be worse than forgetting.
    }
  }
}
