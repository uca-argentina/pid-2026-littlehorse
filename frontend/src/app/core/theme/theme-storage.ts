import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';

export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'drinkit.theme';

/**
 * Which palette the app shows. Always dark unless a person explicitly picks
 * light through the sun/moon toggle in AdminHeader — never through the
 * device's own light/dark setting, which the app deliberately never reads.
 * The venue is dark on purpose (see styles.scss); a light screen a customer
 * or the KDS never asked for would blind whoever is holding it.
 *
 * In localStorage, not sessionStorage: unlike a staff session this is not
 * shift-scoped, it is the person's own preference and outlives the tab.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStorage {
  private readonly document = inject(DOCUMENT);

  private readonly current = signal<Theme>(this.readStored());

  readonly theme = this.current.asReadonly();

  constructor() {
    // The one place that reacts to the signal: stamps <html data-theme="…">,
    // which is all styles.scss's light palette is keyed on, and persists the
    // choice for the next visit.
    effect(() => {
      const theme = this.current();

      this.document.documentElement.setAttribute('data-theme', theme);
      this.write(theme);
    });
  }

  toggle(): void {
    this.current.update((theme) => (theme === 'dark' ? 'light' : 'dark'));
  }

  private readStored(): Theme {
    try {
      return localStorage.getItem(STORAGE_KEY) === 'light' ? 'light' : 'dark';
    } catch {
      // Private windows and locked-down browsers throw on access. Dark is
      // the default either way.
      return 'dark';
    }
  }

  private write(theme: Theme): void {
    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // Storage is a convenience, not the source of truth: the signal above
      // already holds the preference for this page load.
    }
  }
}
