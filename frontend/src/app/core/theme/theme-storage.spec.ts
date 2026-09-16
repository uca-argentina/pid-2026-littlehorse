import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BrowserStore } from '../storage/browser-store';
import { ThemeStorage } from './theme-storage';

const STORAGE_KEY = 'drinkit.theme';

@Component({ template: '' })
class Host {
  readonly theme = inject(ThemeStorage);
}

// The runner is Node and there is no localStorage there, so a spec that
// reached for one would fail for a reason that has nothing to do with the
// theme. A fresh one per test also keeps a choice in one case out of the next.
class StoreInMemory extends BrowserStore {
  readonly entries = new Map<string, string>();

  override read(key: string): string | null {
    return this.entries.get(key) ?? null;
  }

  override write(key: string, value: string): void {
    this.entries.set(key, value);
  }
}

let store: StoreInMemory;

async function openHost() {
  TestBed.configureTestingModule({ providers: [{ provide: BrowserStore, useValue: store }] });

  const fixture = TestBed.createComponent(Host);

  await fixture.whenStable();

  return fixture;
}

describe('ThemeStorage', () => {
  beforeEach(() => {
    store = new StoreInMemory();
    document.documentElement.removeAttribute('data-theme');
  });

  // The device's own light/dark preference is never read: the venue is dark
  // on purpose, and only an explicit toggle may change that.
  it('starts dark when nothing was chosen before', async () => {
    const fixture = await openHost();

    expect(fixture.componentInstance.theme.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('starts with what was chosen on an earlier visit', async () => {
    store.entries.set(STORAGE_KEY, 'light');

    const fixture = await openHost();

    expect(fixture.componentInstance.theme.theme()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('toggles between dark and light, and remembers the choice', async () => {
    const fixture = await openHost();
    const { theme } = fixture.componentInstance;

    theme.toggle();
    await fixture.whenStable();

    expect(theme.theme()).toBe('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(store.entries.get(STORAGE_KEY)).toBe('light');

    theme.toggle();
    await fixture.whenStable();

    expect(theme.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(store.entries.get(STORAGE_KEY)).toBe('dark');
  });
});
