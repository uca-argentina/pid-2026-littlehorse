import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ThemeStorage } from './theme-storage';

const STORAGE_KEY = 'drinkit.theme';

@Component({ template: '' })
class Host {
  readonly theme = inject(ThemeStorage);
}

async function openHost() {
  const fixture = TestBed.createComponent(Host);

  await fixture.whenStable();

  return fixture;
}

// jsdom in this project's test environment has no working Storage: the
// origin it renders under does not implement one. The closest a test can get
// is a stand-in with the same shape, same as URL's createObjectURL elsewhere.
function fakeLocalStorage(): Storage {
  const data = new Map<string, string>();

  return {
    getItem: (key) => data.get(key) ?? null,
    setItem: (key, value) => void data.set(key, value),
    removeItem: (key) => void data.delete(key),
    clear: () => data.clear(),
    key: (index) => Array.from(data.keys())[index] ?? null,
    get length() {
      return data.size;
    },
  };
}

describe('ThemeStorage', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', fakeLocalStorage());
    document.documentElement.removeAttribute('data-theme');
  });

  afterEach(() => vi.unstubAllGlobals());

  // The device's own light/dark preference is never read: the venue is dark
  // on purpose, and only an explicit toggle may change that.
  it('starts dark when nothing was chosen before', async () => {
    const fixture = await openHost();

    expect(fixture.componentInstance.theme.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('starts with what was chosen on an earlier visit', async () => {
    localStorage.setItem(STORAGE_KEY, 'light');

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
    expect(localStorage.getItem(STORAGE_KEY)).toBe('light');

    theme.toggle();
    await fixture.whenStable();

    expect(theme.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('dark');
  });
});
