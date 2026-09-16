import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';
import { BrowserStore } from '../storage/browser-store';

export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'drinkit.theme';

/**
 * Which palette the app shows. Always dark unless a person explicitly picks
 * light through the sun/moon toggle (shared/theme-toggle) — never through the
 * device's own light/dark setting, which the app deliberately never reads.
 * The venue is dark on purpose (see styles.scss); a light screen a customer
 * or the KDS never asked for would blind whoever is holding it.
 *
 * Through BrowserStore rather than localStorage directly: unlike a staff
 * session this is not shift-scoped, it is the person's own preference and
 * outlives the tab, so it belongs in the same seam Cart already uses.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStorage {
  private readonly document = inject(DOCUMENT);

  private readonly store = inject(BrowserStore);

  private readonly current = signal<Theme>(this.readStored());

  readonly theme = this.current.asReadonly();

  constructor() {
    // The one place that reacts to the signal: stamps <html data-theme="…">,
    // which is all styles.scss's light palette is keyed on, and persists the
    // choice for the next visit.
    effect(() => {
      const theme = this.current();

      this.document.documentElement.setAttribute('data-theme', theme);
      this.store.write(STORAGE_KEY, theme);
    });
  }

  toggle(): void {
    this.current.update((theme) => (theme === 'dark' ? 'light' : 'dark'));
  }

  private readStored(): Theme {
    return this.store.read(STORAGE_KEY) === 'light' ? 'light' : 'dark';
  }
}
